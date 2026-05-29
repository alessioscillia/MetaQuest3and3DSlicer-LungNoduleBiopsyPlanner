#!/usr/bin/env python3
"""
calibrate_camera.py — Calibra la camera passthrough del Quest 3.

Analogo al codice di riferimento cylmarker (calib_camera.py ROS),
con le stesse scelte di parametri:
  - cornerSubPix window:  (5,5)
  - criteri:              300 iterazioni, tolleranza 0.00001
  - findChessboardCorners flags: ADAPTIVE_THRESH + NORMALIZE_IMAGE

Uso:
    python calibrate_camera.py

Lancia nella stessa cartella di pose_server.py, dove si trova calib_frames/.

⚙️  UNICI PARAMETRI DA ADATTARE:
"""

CHECKERBOARD_COLS = 8      # angoli interni orizzontali  (= quadrati_orizzontali - 1)
CHECKERBOARD_ROWS = 7      # angoli interni verticali    (= quadrati_verticali - 1)
SQUARE_SIZE_MM    = 10.0   # lato di ogni quadrato in mm — misuralo con un righello

"""
Esempio: scacchiera 10×7 quadrati → COLS=9, ROWS=6
"""

# ---------------------------------------------------------------------------
import os, sys, glob
import cv2 as cv
import numpy as np

SAVE_DIR    = "calib_frames"
OUTPUT_YAML = "camera_calibration.yaml"

# Stessi parametri del codice di riferimento
CRITERIA = (cv.TERM_CRITERIA_EPS + cv.TERM_CRITERIA_MAX_ITER, 300, 0.00001)
CB_FLAGS = cv.CALIB_CB_ADAPTIVE_THRESH + cv.CALIB_CB_NORMALIZE_IMAGE
SIZE     = (CHECKERBOARD_COLS, CHECKERBOARD_ROWS)

# Punti 3D della scacchiera (z=0, unità mm) — stesso approccio del riferimento
objp = np.zeros((CHECKERBOARD_ROWS * CHECKERBOARD_COLS, 3), np.float32)
objp[:, :2] = np.mgrid[
    0 : CHECKERBOARD_COLS * SQUARE_SIZE_MM : SQUARE_SIZE_MM,
    0 : CHECKERBOARD_ROWS * SQUARE_SIZE_MM : SQUARE_SIZE_MM
].T.reshape(-1, 2)


# ---------------------------------------------------------------------------
def calib_monocular(paths):
    """Replica esatta della funzione calib_monocular() del codice di riferimento."""
    obj_points = []
    img_points = []
    gray_shape = None
    used = 0
    skipped = 0

    for fname in paths:
        im   = cv.imread(fname)
        gray = cv.cvtColor(im, cv.COLOR_BGR2GRAY)

        if gray_shape is None:
            gray_shape = gray.shape[::-1]   # (w, h)

        ret, corners = cv.findChessboardCorners(gray, SIZE, CB_FLAGS)

        if ret:
            obj_points.append(objp)
            # window (5,5) come nel riferimento — più preciso di (11,11)
            corners_ref = cv.cornerSubPix(gray, corners, (5, 5), (-1, -1), CRITERIA)
            img_points.append(corners_ref)
            used += 1
            print(f"  ✓  {os.path.basename(fname)}")

            # Salva anteprima con angoli disegnati
            preview = im.copy()
            cv.drawChessboardCorners(preview, SIZE, corners_ref, ret)
            cv.imwrite(fname.replace(".jpg", "_corners.jpg"), preview)
        else:
            skipped += 1
            print(f"  ✗  {os.path.basename(fname)}  (scacchiera non trovata — scartato)")

    print(f"\n[INFO] Usati: {used} | Scartati: {skipped}")

    if used < 10:
        print("\n[ERRORE] Servono almeno 10 frame validi.")
        print("         Raccogline altri e riprova.")
        sys.exit(1)

    _, K, D, _, _ = cv.calibrateCamera(
        obj_points, img_points, gray_shape, None, None
    )
    return K, D


# ---------------------------------------------------------------------------
def save_yaml(K, D):
    yaml_content = (
        f"intrinsic: {K.tolist()}\n"
        f"distortion: {D[0].tolist()}\n"
    )
    with open(OUTPUT_YAML, 'w') as f:
        f.write(yaml_content)
    print(f"\n[INFO] Salvato: {os.path.abspath(OUTPUT_YAML)}")
    print(f"       fx={K[0,0]:.2f}  fy={K[1,1]:.2f}  "
          f"cx={K[0,2]:.2f}  cy={K[1,2]:.2f}")
    print(f"       distorsione: {D[0].tolist()}")


# ---------------------------------------------------------------------------
if __name__ == '__main__':
    print("=" * 60)
    print("  Calibrazione camera passthrough Quest 3")
    print(f"  Scacchiera: {CHECKERBOARD_COLS}×{CHECKERBOARD_ROWS} angoli interni")
    print(f"  Lato quadrato: {SQUARE_SIZE_MM} mm")
    print("=" * 60 + "\n")

    paths = sorted(glob.glob(os.path.join(SAVE_DIR, "frame_*.jpg")))
    if not paths:
        print(f"[ERRORE] Nessun frame in '{SAVE_DIR}/'.")
        print("         Avvia pose_server.py e cattura i frame dal visore.")
        sys.exit(1)
    print(f"[INFO] Trovati {len(paths)} frame in '{SAVE_DIR}/'\n")

    K, D = calib_monocular(paths)

    # Stampa nello stesso formato del codice di riferimento
    np.set_printoptions(threshold=sys.maxsize, suppress=True)
    print("\nCopy and paste these values into camera_calibration.yaml:\n")
    print(f"intrinsic: {K.tolist()}")
    print(f"distortion: {D[0].tolist()}")

    save_yaml(K, D)

    print("\n✓ Copia camera_calibration.yaml in data/ e riavvia pose_server.py.\n")