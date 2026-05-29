#!/usr/bin/env python3
"""
pose_server.py — Server HTTP per la stima della posa del marker cilindrico.

Riceve un frame JPEG via POST /pose, esegue la pipeline cylmarker completa
(segmentazione → rilevamento keypoint → solvePnPRansac) e restituisce la
posa come JSON.

Avvio:
    python pose_server.py            # cerca i dati in ./data
    python pose_server.py /percorso/data

Richiede:
    pip install flask opencv-python numpy pyyaml scikit-spatial parse
    + il pacchetto cylmarker dalla repo
"""

import sys
import os
import traceback
import time

import cv2 as cv
import numpy as np
from flask import Flask, request, jsonify

# Importa i moduli cylmarker (devono essere nel PYTHONPATH o nella stessa cartella)
from cylmarker import load_data, keypoints
from cylmarker import img_segmentation

app = Flask(__name__)

# ---------------------------------------------------------------------------
# Dati globali caricati all'avvio
# ---------------------------------------------------------------------------
cam_matrix      = None
dist_coeff      = None
config_data     = None
data_pttrn      = None
data_marker     = None
sqnc_max_ind    = None
sequence_length = None

DEBUG_DIR = "debug_frames"


def load_all_data(data_path: str):
    """Carica tutti i file YAML necessari alla pipeline."""
    global cam_matrix, dist_coeff, config_data
    global data_pttrn, data_marker, sqnc_max_ind, sequence_length

    if not os.path.isdir(data_path):
        raise FileNotFoundError(f"Directory dati non trovata: {data_path}")

    # config.yaml + camera_calibration.yaml
    data_config, data_cam_calib = load_data.load_config_and_cam_calib_data(data_path)
    config_data = data_config

    # Matrice intrinseca 3x3
    cam_matrix = np.reshape(np.array(data_cam_calib['intrinsic']), (3, 3))

    # Coefficienti di distorsione (il Quest restituisce già [0,0,0,0,0])
    dist_coeff = np.array(data_cam_calib['distortion'], dtype=np.float64)

    # pattern.yaml + marker.yaml
    data_pttrn, data_marker = load_data.load_pttrn_and_marker_data(data_path)
    sqnc_max_ind    = len(data_pttrn) - 1
    sequence_length = len(data_pttrn['sequence_0']['code'])

    print(f"[SERVER] Dati caricati da: {os.path.abspath(data_path)}")
    print(f"[SERVER] Matrice intrinseca:\n{cam_matrix}")
    print(f"[SERVER] Sequenze nel pattern: {sqnc_max_ind + 1} | lunghezza sequenza: {sequence_length}")
    print(f"[SERVER] Distorsione: {dist_coeff}")


# ---------------------------------------------------------------------------
# Calcolo errore di riproiezione (solo sugli inlier)
# ---------------------------------------------------------------------------
def compute_reproj_error(pts3d, pts2d, rvec, tvec, inliers):
    n = inliers.shape[0]
    pts3d_in = np.array([pts3d[i[0]] for i in inliers], dtype=np.float64)
    pts2d_in = np.array([pts2d[i[0]] for i in inliers], dtype=np.float64)

    # dist_coeff = None perché l'immagine è già stata undistorta (o è già corretta)
    pts2d_proj, _ = cv.projectPoints(pts3d_in, rvec, tvec, cam_matrix, None)
    pts2d_det = pts2d_in.reshape(pts2d_proj.shape)

    se = (pts2d_proj - pts2d_det) ** 2
    return float(np.sqrt(np.sum(se) / n))


# ---------------------------------------------------------------------------
# Endpoint principale
# ---------------------------------------------------------------------------
@app.route('/pose', methods=['POST'])
def estimate_pose():
    # Controlla se la richiesta arriva con ?debug=true
    is_debug = request.args.get('debug', '').lower() == 'true'
    
    # Prepara il prefisso per i file di debug
    base_debug_name = ""
    if is_debug:
        os.makedirs(DEBUG_DIR, exist_ok=True)
        timestamp = int(time.time() * 1000)
        base_debug_name = os.path.join(DEBUG_DIR, f"debug_{timestamp}")

    # 1. Decodifica l'immagine JPEG dal body della richiesta
    raw = request.data
    if not raw:
        return jsonify({'error': 'Nessun dato ricevuto nel body'}), 400

    nparr = np.frombuffer(raw, np.uint8)
    im = cv.imdecode(nparr, cv.IMREAD_COLOR)
    if im is None:
        return jsonify({'error': 'Impossibile decodificare il JPEG'}), 400

    if is_debug:
        cv.imwrite(f"{base_debug_name}_01_raw.jpg", im)

    try:
        # 2. Undistort (se i coefficienti non sono tutti zero)
        if not np.all(dist_coeff == 0):
            im = cv.undistort(im, cam_matrix, dist_coeff)
        d_pnp = None  # dopo undistort non serve più passare dist_coeff a solvePnP

        # 3. Segmentazione marker (sfondo + primo piano)
        mask_bg, mask_fg = img_segmentation.marker_segmentation(im, config_data)
        
        # --- MODIFICA DEBUG: Salviamo ENTRAMBE le maschere ---
        if is_debug:
            if mask_bg is not None:
                # 02: La maschera verde del marker (Background)
                cv.imwrite(f"{base_debug_name}_02_mask_bg.jpg", mask_bg)
            if mask_fg is not None:
                # 03: La maschera dei keypoints (Foreground)
                cv.imwrite(f"{base_debug_name}_03_mask_fg.jpg", mask_fg)
                
        if mask_bg is None:
            return jsonify({'detected': False, 'reason': 'marker_not_segmented'})

        # 4. Rilevamento e identificazione keypoint (Logica Cylmarker)
        
        # --- NUOVA MODIFICA DEBUG: DISEGNA TUTTI I KEYPOINT GREZZI ---
        if is_debug and mask_fg is not None:
            im_all_kpts = im.copy()
            # Trova tutti i contorni (i buchi) isolati nella maschera foreground
            contours, _ = cv.findContours(mask_fg, cv.RETR_EXTERNAL, cv.CHAIN_APPROX_NONE)
            raw_kpts_count = 0
            
            for cnt in contours:
                # Calcola il centro di massa (centroid) di ogni macchia
                M = cv.moments(cnt)
                if M["m00"] != 0:
                    cX = int(M["m10"] / M["m00"])
                    cY = int(M["m01"] / M["m00"])
                    # Disegna un cerchio rosso su TUTTI i candidati keypoint
                    cv.circle(im_all_kpts, (cX, cY), radius=4, color=(0, 0, 255), thickness=-1)
                    raw_kpts_count += 1
                    
            # Salviamo questo frame come 03b, in modo da averlo SEMPRE
            cv.imwrite(f"{base_debug_name}_03b_all_raw_keypoints.jpg", im_all_kpts)
            print(f"[DEBUG] Trovati {raw_kpts_count} keypoint grezzi prima del filtraggio.")
        # -------------------------------------------------------------

        pttrn = keypoints.find_keypoints(
            im, mask_fg, config_data,
            sqnc_max_ind, sequence_length,
            data_pttrn, data_marker
        )
        
        if pttrn is None:
            return jsonify({'detected': False, 'reason': 'keypoints_not_found'})

        # 5. Raccolta corrispondenze 3D-2D (Se la sequenza è valida)
        pts3d, pts2d = pttrn.get_data_for_pnp_solver()

        if is_debug and pts2d is not None and len(pts2d) > 0:
            # Questo disegna i pallini SOLO sui keypoint validati dalla sequenza (Verdi, per distinguerli)
            im_kpts_validated = im.copy()
            pts2d_flat = pts2d.reshape(-1, 2)
            for p in pts2d_flat:
                # Disegna un cerchio VERDE sui keypoint che formano effettivamente il marker
                cv.circle(im_kpts_validated, (int(p[0]), int(p[1])), radius=4, color=(0, 255, 0), thickness=-1)
            # 04: Frame originale con i keypoint validati
            cv.imwrite(f"{base_debug_name}_04_keypoints_validated.jpg", im_kpts_validated)
        # 6. solvePnPRansac
        valid, rvec, tvec, inliers = cv.solvePnPRansac(
            pts3d, pts2d,
            cam_matrix, d_pnp,
            None, None,
            False,        # useExtrinsicGuess
            1000,         # iterationsCount
            3.0,          # reprojectionError [px]
            0.9999,       # confidence
            None,
            cv.SOLVEPNP_SQPNP
        )

        if not valid or inliers is None:
            return jsonify({'detected': False, 'reason': 'pnp_failed'})

        # 7. Calcolo metriche
        reproj_err = compute_reproj_error(pts3d, pts2d, rvec, tvec, inliers)
        n_inliers   = int(inliers.shape[0])

        # 8. Conversione rvec → matrice di rotazione 3x3
        rmat, _ = cv.Rodrigues(rvec)

        # 9. Conversione unità: mm → m  (i punti 3D del marker sono in mm)
        tvec_m = (tvec * 0.001).flatten()

        return jsonify({
            'detected':        True,
            'rvec':            rvec.flatten().tolist(),   # Rodrigues (3 elem)
            'tvec':            tvec_m.tolist(),            # traslazione in METRI (3 elem)
            'rmat':            rmat.flatten().tolist(),    # rot matrix flat row-major (9 elem)
            'reproj_error_px': reproj_err,
            'n_inliers':       n_inliers
        })

    except Exception as e:
        traceback.print_exc()
        return jsonify({'error': str(e)}), 500


# ---------------------------------------------------------------------------
# Salvataggio frame per calibrazione checkerboard
# ---------------------------------------------------------------------------
import glob

CALIB_DIR = "calib_frames"

@app.route('/save_frame', methods=['POST'])
def save_frame():
    raw = request.data
    if not raw:
        return jsonify({'error': 'Nessun dato ricevuto'}), 400

    nparr = np.frombuffer(raw, np.uint8)
    im = cv.imdecode(nparr, cv.IMREAD_COLOR)
    if im is None:
        return jsonify({'error': 'Impossibile decodificare il JPEG'}), 400

    os.makedirs(CALIB_DIR, exist_ok=True)

    # Indice progressivo — non sovrascrive mai i frame esistenti
    existing = glob.glob(os.path.join(CALIB_DIR, "frame_*.jpg"))
    indices  = []
    for p in existing:
        try:
            indices.append(int(os.path.splitext(os.path.basename(p))[0].split("_")[1]))
        except (ValueError, IndexError):
            pass
    idx      = max(indices) + 1 if indices else 0
    filename = f"frame_{idx:03d}.jpg"
    filepath = os.path.join(CALIB_DIR, filename)

    cv.imwrite(filepath, im)
    total = len(glob.glob(os.path.join(CALIB_DIR, "frame_*.jpg")))
    print(f"[SERVER] Calibrazione: salvato {filename} | {im.shape[1]}×{im.shape[0]} | totale: {total}")

    return jsonify({'saved': filename, 'index': idx, 'total': total})


@app.route('/delete_last_frame', methods=['DELETE'])
def delete_last_frame():
    idx = request.args.get('index', type=int)
    if idx is None:
        return jsonify({'error': 'Parametro index mancante'}), 400
    filepath = os.path.join(CALIB_DIR, f"frame_{idx:03d}.jpg")
    if os.path.exists(filepath):
        os.remove(filepath)
        total = len(glob.glob(os.path.join(CALIB_DIR, "frame_*.jpg")))
        print(f"[SERVER] Calibrazione: eliminato frame_{idx:03d}.jpg | rimasti: {total}")
        return jsonify({'deleted': f"frame_{idx:03d}.jpg", 'total': total})
    return jsonify({'error': f"frame_{idx:03d}.jpg non trovato"}), 404


# ---------------------------------------------------------------------------
# Health check (usato da PoseClient.cs per verificare la connessione)
# ---------------------------------------------------------------------------
@app.route('/health', methods=['GET'])
def health():
    return jsonify({
        'status':          'ok',
        'sequences':       sqnc_max_ind + 1,
        'sequence_length': sequence_length
    })


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------
if __name__ == '__main__':
    data_path = sys.argv[1] if len(sys.argv) > 1 else 'data'
    load_all_data(data_path)
    print(f"\n[SERVER] In ascolto su http://0.0.0.0:5000")
    print(f"[SERVER] Sul Quest, usa l'IP del tuo PC nella LAN WiFi.\n")
    app.run(host='0.0.0.0', port=5000, debug=False, threaded=False)