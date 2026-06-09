using System;
using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Meta.XR;

/// <summary>
/// Cattura frame per la calibrazione della camera passthrough del Quest 3.
///
/// Funzioni:
///   - Preview live della camera fisica su un pannello UI in-headset
///   - Premi A per inviare il frame corrente al server
///   - Premi B per estrarre le intrinseche hardware MRUK, correggerle per crop/risoluzione e inviarle al server
///   - Premi Y per eliminare l'ultimo frame inviato
/// </summary>
public class CalibrationCapture : MonoBehaviour
{
    // -----------------------------------------------------------------------
    // Inspector
    // -----------------------------------------------------------------------

    [Header("Server")]
    public string serverIP = "127.0.0.1";
    public int serverPort = 5000;

    [Header("Riferimento camera")]
    public CameraFrameCapture cameraCapture;

    [Header("Meta Passthrough API")]
    [Tooltip("Trascina qui il GameObject che contiene lo script PassthroughCameraAccess")]
    public PassthroughCameraAccess metaCameraAccess;

    [Header("Preview live")]
    [Tooltip("RawImage su un Canvas World Space — mostra il feed della camera fisica")]
    public RawImage previewImage;

    [Tooltip("Aspect ratio del pannello preview. Se possibile viene aggiornato automaticamente dalla LiveTexture.")]
    public float previewAspect = 4f / 3f;

    // -----------------------------------------------------------------------
    // Stato interno
    // -----------------------------------------------------------------------

    private int _framesSent = 0;
    private int _lastIdx = -1;

    private bool _serverOk = false;
    private bool _sendingFrame = false;
    private bool _sendingCalibration = false;
    private bool _deleting = false;

    private string _statusMsg = "Connessione al server…";

    private string _uploadUrl;
    private string _deleteUrl;
    private string _calibUrl;
    private string _healthUrl;

    // -----------------------------------------------------------------------
    // Unity lifecycle
    // -----------------------------------------------------------------------

    void Start()
    {
        _uploadUrl = $"http://{serverIP}:{serverPort}/save_frame";
        _deleteUrl = $"http://{serverIP}:{serverPort}/delete_last_frame";
        _calibUrl = $"http://{serverIP}:{serverPort}/upload_calib";
        _healthUrl = $"http://{serverIP}:{serverPort}/health";

        if (cameraCapture == null)
            cameraCapture = GetComponent<CameraFrameCapture>();

        if (cameraCapture == null)
        {
            _statusMsg = "Errore: CameraFrameCapture mancante.";
            Debug.LogError("[CalibrationCapture] CameraFrameCapture non trovato.");
            return;
        }

        StartCoroutine(CheckServer());
        StartCoroutine(WaitAndBindPreview());
    }

    void Update()
    {
        if (!_serverOk)
            return;

        // A — cattura e invia frame di calibrazione
        if (!_sendingFrame && OVRInput.GetDown(OVRInput.Button.One))
        {
            if (cameraCapture == null || !cameraCapture.IsReady)
            {
                _statusMsg = "Camera non pronta, riprova…";
                Debug.LogWarning("[CalibrationCapture] Camera non pronta.");
                return;
            }

            byte[] jpeg = cameraCapture.CaptureFrameAsJpeg();

            if (jpeg != null)
                StartCoroutine(SendFrame(jpeg));
            else
            {
                _statusMsg = "Errore: frame JPEG nullo.";
                Debug.LogWarning("[CalibrationCapture] CaptureFrameAsJpeg() ha restituito null.");
            }
        }

        // B — estrae intrinseche MRUK, corregge crop/risoluzione e invia YAML al server
        if (!_sendingCalibration && OVRInput.GetDown(OVRInput.Button.Two))
        {
            StartCoroutine(SendHardwareIntrinsicsToServer());
        }

        // Y — elimina ultimo frame inviato
        if (!_deleting && _lastIdx >= 0 && OVRInput.GetDown(OVRInput.Button.Four))
        {
            StartCoroutine(DeleteLastFrame());
        }
    }

    // -----------------------------------------------------------------------
    // Connessione server
    // -----------------------------------------------------------------------

    IEnumerator CheckServer()
    {
        using (UnityWebRequest req = UnityWebRequest.Get(_healthUrl))
        {
            req.timeout = 5;
            yield return req.SendWebRequest();

            _serverOk = req.result == UnityWebRequest.Result.Success;

            if (_serverOk)
            {
                _statusMsg = "Server OK — A=Scatta | B=Invia intrinseche | Y=Annulla";
                Debug.Log("[CalibrationCapture] Server raggiungibile.");
            }
            else
            {
                _statusMsg = $"Server NON raggiungibile: {req.error}";
                Debug.LogError($"[CalibrationCapture] Server NON raggiungibile: {req.error}");
            }
        }
    }

    // -----------------------------------------------------------------------
    // Preview live
    // -----------------------------------------------------------------------

    IEnumerator WaitAndBindPreview()
    {
        if (previewImage == null)
            yield break;

        yield return new WaitUntil(() =>
            cameraCapture != null &&
            cameraCapture.LiveTexture != null &&
            cameraCapture.IsReady
        );

        previewImage.texture = cameraCapture.LiveTexture;

        int texW = cameraCapture.LiveTexture.width;
        int texH = cameraCapture.LiveTexture.height;

        if (texW > 0 && texH > 0)
            previewAspect = (float)texW / texH;

        Debug.Log(
            $"[CalibrationCapture] LiveTexture collegata: {texW} x {texH}, aspect={previewAspect:F4}"
        );

        RectTransform rt = previewImage.GetComponent<RectTransform>();

        if (rt != null)
        {
            float h = rt.sizeDelta.y;
            rt.sizeDelta = new Vector2(h * previewAspect, h);
        }
    }

    // -----------------------------------------------------------------------
    // Correzione intrinseche MRUK rispetto a crop e risoluzione effettiva
    // -----------------------------------------------------------------------

    private Rect CalcSensorCropRegion(Vector2Int sensorResolutionInt, Vector2Int targetResolutionInt)
    {
        Vector2 sensorResolution = (Vector2)sensorResolutionInt;
        Vector2 targetResolution = (Vector2)targetResolutionInt;

        Vector2 scaleFactor = targetResolution / sensorResolution;
        scaleFactor /= Mathf.Max(scaleFactor.x, scaleFactor.y);

        return new Rect(
            sensorResolution.x * (1f - scaleFactor.x) * 0.5f,
            sensorResolution.y * (1f - scaleFactor.y) * 0.5f,
            sensorResolution.x * scaleFactor.x,
            sensorResolution.y * scaleFactor.y
        );
    }

    private Vector2Int GetTargetImageResolution()
    {
        /*
         * Questa deve essere la risoluzione dell'immagine che arriva davvero a OpenCV.
         *
         * Se CameraFrameCapture.CaptureFrameAsJpeg() salva esattamente la LiveTexture,
         * allora LiveTexture.width/height sono la scelta corretta.
         *
         * Se invece CameraFrameCapture ridimensiona o ruota il frame prima del JPEG,
         * bisogna usare la risoluzione finale del JPEG.
         */

        if (cameraCapture != null &&
            cameraCapture.LiveTexture != null &&
            cameraCapture.LiveTexture.width > 0 &&
            cameraCapture.LiveTexture.height > 0)
        {
            return new Vector2Int(
                cameraCapture.LiveTexture.width,
                cameraCapture.LiveTexture.height
            );
        }

        if (metaCameraAccess != null &&
            metaCameraAccess.CurrentResolution.x > 0 &&
            metaCameraAccess.CurrentResolution.y > 0)
        {
            return metaCameraAccess.CurrentResolution;
        }

        return Vector2Int.zero;
    }

    private bool TryBuildCorrectedCalibrationYaml(out string yamlContent, out string debugMessage)
    {
        yamlContent = "";
        debugMessage = "";

        if (metaCameraAccess == null || !metaCameraAccess.IsPlaying)
        {
            debugMessage = "PassthroughCameraAccess mancante o non avviato.";
            return false;
        }

        var intrinsics = metaCameraAccess.Intrinsics;

        Vector2Int sensorRes = intrinsics.SensorResolution;
        Vector2Int currentRes = metaCameraAccess.CurrentResolution;
        Vector2Int targetRes = GetTargetImageResolution();

        if (sensorRes.x <= 0 || sensorRes.y <= 0)
        {
            debugMessage = $"SensorResolution non valida: {sensorRes.x} x {sensorRes.y}";
            return false;
        }

        if (targetRes.x <= 0 || targetRes.y <= 0)
        {
            debugMessage = $"Target image resolution non valida: {targetRes.x} x {targetRes.y}";
            return false;
        }

        Rect crop = CalcSensorCropRegion(sensorRes, targetRes);

        if (crop.width <= 0f || crop.height <= 0f)
        {
            debugMessage = $"Crop non valido: x={crop.x}, y={crop.y}, w={crop.width}, h={crop.height}";
            return false;
        }

        float sx = targetRes.x / crop.width;
        float sy = targetRes.y / crop.height;

        float fxRaw = intrinsics.FocalLength.x;
        float fyRaw = intrinsics.FocalLength.y;
        float cxRaw = intrinsics.PrincipalPoint.x;
        float cyRaw = intrinsics.PrincipalPoint.y;

        float fxCorr = fxRaw * sx;
        float fyCorr = fyRaw * sy;
        float cxCorr = (cxRaw - crop.x) * sx;
        float cyCorr = (cyRaw - crop.y) * sy;

        debugMessage = string.Format(
            CultureInfo.InvariantCulture,
            "[CHECK CROP / INTRINSECHE]\n" +
            "RequestedResolution = {0} x {1}\n" +
            "CurrentResolution   = {2} x {3}\n" +
            "SensorResolution    = {4} x {5}\n" +
            "TargetResolution    = {6} x {7}\n\n" +

            "Crop sensor-space: x={8:F4}, y={9:F4}, w={10:F4}, h={11:F4}\n" +
            "Scale: sx={12:F6}, sy={13:F6}\n\n" +

            "RAW:  fx={14:F4}, fy={15:F4}, cx={16:F4}, cy={17:F4}\n" +
            "CORR: fx={18:F4}, fy={19:F4}, cx={20:F4}, cy={21:F4}",
            metaCameraAccess.RequestedResolution.x,
            metaCameraAccess.RequestedResolution.y,
            currentRes.x,
            currentRes.y,
            sensorRes.x,
            sensorRes.y,
            targetRes.x,
            targetRes.y,
            crop.x,
            crop.y,
            crop.width,
            crop.height,
            sx,
            sy,
            fxRaw,
            fyRaw,
            cxRaw,
            cyRaw,
            fxCorr,
            fyCorr,
            cxCorr,
            cyCorr
        );

        yamlContent = string.Format(
            CultureInfo.InvariantCulture,
            "intrinsic: [[{0:F4}, 0.0, {1:F4}], [0.0, {2:F4}, {3:F4}], [0.0, 0.0, 1.0]]\n" +
            "distortion: [0.0, 0.0, 0.0, 0.0, 0.0]\n",
            fxCorr,
            cxCorr,
            fyCorr,
            cyCorr
        );

        return true;
    }

    // -----------------------------------------------------------------------
    // Invio calibrazione hardware corretta al server
    // -----------------------------------------------------------------------

    IEnumerator SendHardwareIntrinsicsToServer()
    {
        _sendingCalibration = true;
        _statusMsg = "Lettura intrinseche hardware MRUK…";

        Debug.Log("[CalibrationCapture] Lettura intrinseche hardware MRUK dalla fotocamera...");

        string yamlContent;
        string debugMessage;

        bool ok = TryBuildCorrectedCalibrationYaml(out yamlContent, out debugMessage);

        if (!ok)
        {
            _statusMsg = $"Errore intrinseche: {debugMessage}";
            Debug.LogError($"[CalibrationCapture] {debugMessage}");
            _sendingCalibration = false;
            yield break;
        }

        Debug.LogWarning(debugMessage);
        Debug.LogWarning($"[CalibrationCapture] YAML generato:\n{yamlContent}");

        _statusMsg = "Invio calibrazione hardware corretta…";

        using (UnityWebRequest req = new UnityWebRequest(_calibUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(yamlContent);

            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "text/plain");
            req.timeout = 5;

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                _statusMsg = "✓ Calibrazione inviata. Server aggiornato.";
                Debug.LogWarning("[CalibrationCapture] ✓ YAML intrinseche corrette inviato al server.");
            }
            else
            {
                _statusMsg = $"Errore invio intrinseche: {req.error}";
                Debug.LogError($"[CalibrationCapture] ✗ Errore invio intrinseche: {req.error}");
            }
        }

        _sendingCalibration = false;
    }

    // -----------------------------------------------------------------------
    // Invio frame di calibrazione
    // -----------------------------------------------------------------------

    IEnumerator SendFrame(byte[] jpeg)
    {
        _sendingFrame = true;
        _statusMsg = "Invio frame…";

        using (UnityWebRequest req = new UnityWebRequest(_uploadUrl, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(jpeg);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "image/jpeg");
            req.timeout = 5;

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    SaveFrameResponse resp = JsonUtility.FromJson<SaveFrameResponse>(
                        req.downloadHandler.text
                    );

                    _lastIdx = resp.index;
                    _framesSent = resp.total;

                    _statusMsg =
                        $"✓ Frame_{resp.index:D3} salvato | Totale: {resp.total} | Y=annulla";

                    Debug.Log(
                        $"[CalibrationCapture] Frame_{resp.index:D3} salvato. Totale server: {resp.total}"
                    );
                }
                catch (Exception e)
                {
                    _statusMsg = "Frame inviato, ma risposta server non leggibile.";
                    Debug.LogWarning(
                        $"[CalibrationCapture] Errore parsing risposta /save_frame: {e.Message}\n" +
                        $"Risposta: {req.downloadHandler.text}"
                    );
                }
            }
            else
            {
                _statusMsg = $"Errore invio frame: {req.error}";
                Debug.LogError($"[CalibrationCapture] Errore invio frame: {req.error}");
            }
        }

        _sendingFrame = false;
    }

    // -----------------------------------------------------------------------
    // Eliminazione ultimo frame
    // -----------------------------------------------------------------------

    IEnumerator DeleteLastFrame()
    {
        _deleting = true;
        _statusMsg = $"Eliminazione frame_{_lastIdx:D3}…";

        using (UnityWebRequest req = UnityWebRequest.Delete($"{_deleteUrl}?index={_lastIdx}"))
        {
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = 5;

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    DeleteFrameResponse resp = JsonUtility.FromJson<DeleteFrameResponse>(
                        req.downloadHandler.text
                    );

                    _framesSent = resp.total;
                    _statusMsg = $"Frame_{_lastIdx:D3} eliminato | Rimasti: {resp.total}";
                    Debug.Log($"[CalibrationCapture] Frame_{_lastIdx:D3} eliminato.");

                    _lastIdx = -1;
                }
                catch (Exception e)
                {
                    _statusMsg = "Frame eliminato, ma risposta server non leggibile.";
                    Debug.LogWarning(
                        $"[CalibrationCapture] Errore parsing risposta /delete_last_frame: {e.Message}\n" +
                        $"Risposta: {req.downloadHandler.text}"
                    );

                    _lastIdx = -1;
                }
            }
            else
            {
                _statusMsg = $"Errore eliminazione: {req.error}";
                Debug.LogError($"[CalibrationCapture] Errore eliminazione frame: {req.error}");
            }
        }

        _deleting = false;
    }

    // -----------------------------------------------------------------------
    // HUD
    // -----------------------------------------------------------------------

    void OnGUI()
    {
        GUIStyle s = new GUIStyle(GUI.skin.box)
        {
            fontSize = 23,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(14, 14, 8, 8)
        };

        s.normal.textColor = _serverOk
            ? Color.white
            : new Color(1f, 0.35f, 0.35f);

        GUI.Box(
            new Rect(10, 10, 1000, 44),
            $"  Frame inviati: {_framesSent}    {_statusMsg}",
            s
        );

        GUIStyle si = new GUIStyle(s)
        {
            fontSize = 18,
            fontStyle = FontStyle.Normal
        };

        si.normal.textColor = new Color(0.8f, 0.8f, 0.8f);

        GUI.Box(
            new Rect(10, 58, 1000, 34),
            "  A=Cattura frame | B=Invia intrinseche corrette | Y=Elimina ultimo",
            si
        );
    }

    // -----------------------------------------------------------------------
    // Classi per parsing JSON server
    // -----------------------------------------------------------------------

    [Serializable]
    private class SaveFrameResponse
    {
        public string saved;
        public int index;
        public int total;
    }

    [Serializable]
    private class DeleteFrameResponse
    {
        public string deleted;
        public int total;
    }
}