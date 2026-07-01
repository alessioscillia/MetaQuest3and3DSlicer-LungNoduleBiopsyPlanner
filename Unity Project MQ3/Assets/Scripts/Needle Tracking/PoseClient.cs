using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.InputSystem;
using Meta.XR.MRUtilityKit;
using Meta.XR; // Per accedere a PassthroughCameraAccess


[Serializable]

public class PoseResponse
{
    public bool   detected;
    public string reason;
    public float[] rvec;
    public float[] tvec;
    public float[] rmat;
    public float  reproj_error_px;
    public int    n_inliers;
}

public class PoseClient : MonoBehaviour
{
    [Header("Compensazione Lente Fisica (Metri)")]
    [Tooltip("Offset locale tra l'occhio virtuale e la telecamera fisica del Quest 3. X=Destra, Y=Alto, Z=Avanti")]
    public Vector3 cameraPhysicalOffset = Vector3.zero;

    // --- VARIABILE PER LO SPOSTAMENTO DELL'ORIGINE ---
    [Header("Offset Origine Marker (Metri)")]
    [Tooltip("Sposta l'origine tracciata. X=Rosso, Y=Verde (Alto), Z=Blu (Avanti). Esempio: 2cm in alto e 12cm avanti = (0, 0.02, 0.12)")]
    public Vector3 markerLocalOffset = new Vector3(0f, 0f, 0f); 
    // -----------------------------------------------------

    [Header("Connessione al server Python")]
    public string serverIP   = "127.0.0.1";
    public int    serverPort = 5000;

    [Header("Frequenza di campionamento")]
    public float captureIntervalSeconds = 0.1f;

    [Header("Riferimento camera")]
    public CameraFrameCapture cameraCapture;

    [Header("Debug")]
    public bool logPoseData = true;
    [Header("Meta Passthrough API")]
    [Tooltip("Trascina qui il GameObject che contiene lo script PassthroughCameraAccess di Meta")]
    public PassthroughCameraAccess metaCameraAccess;

    public float LastReprojError { get; private set; } = -1f;
    public int   LastNInliers    { get; private set; } = 0;

    public event Action<Vector3, Quaternion> OnPoseDetected;
    public event Action OnMarkerLost;
    public event Action OnServerConnectionFailed;

    private bool   _running  = false;
    private string _poseUrl;
    private string _healthUrl;
    private bool _requestDebugNextFrame = false;

    void Start()
    {
        _poseUrl   = $"http://{serverIP}:{serverPort}/pose";
        _healthUrl = $"http://{serverIP}:{serverPort}/health";

        if (cameraCapture == null)
            cameraCapture = GetComponent<CameraFrameCapture>();

        if (metaCameraAccess == null && cameraCapture != null)
            metaCameraAccess = cameraCapture.metaCameraAccess;

        if (cameraCapture == null)
        {
            Debug.LogError("[PoseClient] CameraFrameCapture non trovato! Aggiungilo allo stesso GameObject.");
            return;
        }
        if (metaCameraAccess == null)
        {
            Debug.LogError("[PoseClient] PassthroughCameraAccess non assegnato!");
            return;
        }
    }

    void Update()
    {
        // Debug del tracciamento (Tasto X o SPAZIO)
        if (OVRInput.GetDown(OVRInput.Button.Three)) 
        {
            _requestDebugNextFrame = true;
            Debug.LogWarning("[PoseClient] Hai premuto X! Il prossimo frame salverà il debug sul PC.");
        }

        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            _requestDebugNextFrame = true;
            Debug.LogWarning("[PoseClient] Hai premuto SPAZIO! Il prossimo frame salverà il debug sul PC.");
        }
    }

    IEnumerator CheckServerThenStart()
    {
        Debug.Log($"[PoseClient] Verifica connessione a {_healthUrl} …");

        using (UnityWebRequest req = UnityWebRequest.Get(_healthUrl))
        {
            req.timeout = 5;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"[PoseClient] ✓ Server raggiungibile. Avvio tracking.");
                _running = true;
                StartCoroutine(CaptureLoop());
            }
            else
            {
                Debug.LogError(
                    $"[PoseClient] ✗ Server NON raggiungibile ({req.error}).\n" +
                    $"  → Controlla che pose_server.py sia in esecuzione sul PC\n" +
                    $"  → IP={serverIP}  porta={serverPort}");

                OnServerConnectionFailed?.Invoke();
            }
        }
    }

    IEnumerator CaptureLoop()
    {
        Debug.Log("[PoseClient] CaptureLoop AVVIATO.");

        int sentFrames = 0;
        float nextDebugLogTime = 0f;

        while (_running)
        {
            yield return new WaitForSeconds(captureIntervalSeconds);

            if (Time.time >= nextDebugLogTime)
            {
                nextDebugLogTime = Time.time + 1.0f;

                string metaState = metaCameraAccess == null
                    ? "NULL"
                    : $"IsPlaying={metaCameraAccess.IsPlaying}";

                string cameraState = cameraCapture == null
                    ? "NULL"
                    : $"IsReady={cameraCapture.IsReady}";

                Debug.Log($"[PoseClient] Loop status | running={_running} | camera={cameraState} | meta={metaState} | sentFrames={sentFrames}");
            }

            if (cameraCapture == null)
            {
                Debug.LogWarning("[PoseClient] cameraCapture NULL: impossibile catturare frame.");
                continue;
            }

            if (!cameraCapture.IsReady)
            {
                // Non spammiamo: il log dettagliato sopra esce ogni 1 secondo.
                continue;
            }

            byte[] jpeg = cameraCapture.CaptureFrameAsJpeg();
            if (jpeg == null || jpeg.Length == 0)
            {
                if (Time.time >= nextDebugLogTime)
                    Debug.LogWarning("[PoseClient] CaptureFrameAsJpeg() ha restituito null o array vuoto.");
                continue;
            }

            if (metaCameraAccess == null)
            {
                Debug.LogWarning("[PoseClient] metaCameraAccess NULL: salto frame.");
                continue;
            }

            if (!metaCameraAccess.IsPlaying)
            {
                // Non spammiamo: il log dettagliato sopra esce ogni 1 secondo.
                continue;
            }

            Pose cameraPoseAtFrame = metaCameraAccess.GetCameraPose();

            sentFrames++;

            if (logPoseData && (sentFrames <= 5 || sentFrames % 30 == 0))
            {
                Debug.Log($"[PoseClient] Invio frame #{sentFrames} a {_poseUrl} | jpeg={jpeg.Length} bytes");
            }

            yield return StartCoroutine(SendFrame(jpeg, cameraPoseAtFrame));
        }

        Debug.Log("[PoseClient] CaptureLoop TERMINATO.");
    }

    IEnumerator SendFrame(byte[] jpeg, Pose cameraPoseAtFrame)
    {
        string currentUrl = _poseUrl;
        
        if (_requestDebugNextFrame)
        {
            currentUrl += "?debug=true";
            _requestDebugNextFrame = false; 
        }

        using (UnityWebRequest req = new UnityWebRequest(currentUrl, "POST"))
        {
            req.uploadHandler   = new UploadHandlerRaw(jpeg);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "image/jpeg");
            req.timeout = 5;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                if (logPoseData)
                    Debug.LogWarning($"[PoseClient] Errore HTTP: {req.error}");
                yield break;
            }

            ProcessResponse(req.downloadHandler.text, cameraPoseAtFrame);
        }
    }

    void ProcessResponse(string json, Pose cameraPoseAtFrame)
    {
        PoseResponse resp;
        try
        {
            resp = JsonUtility.FromJson<PoseResponse>(json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PoseClient] Errore parsing JSON: {e.Message}\nJSON: {json}");
            return;
        }

        if (!resp.detected)
        {
            if (logPoseData)
                Debug.Log($"[PoseClient] Marker non rilevato — motivo: {resp.reason}");
            OnMarkerLost?.Invoke();
            return;
        }

        const float MAX_REPROJ_PX = 3.0f;
        const int   MIN_INLIERS   = 12;

        if (resp.reproj_error_px > MAX_REPROJ_PX || resp.n_inliers < MIN_INLIERS)
        {
            if (logPoseData)
                Debug.Log($"[PoseClient] Stima scartata — reproj={resp.reproj_error_px:F2}px " +
                        $"inliers={resp.n_inliers} (sotto soglia)");
            return;
        }

        Vector3 posInCamera = OpenCVTranslationToUnity(resp.tvec);
        Quaternion rotInCamera = OpenCVRotationMatrixToUnity(resp.rmat);

        // Ora usiamo la posa della camera fisica reale, non quella dell'occhio sinistro.
        // Quindi NON aggiungiamo cameraPhysicalOffset.
        Vector3 worldPos = cameraPoseAtFrame.position + cameraPoseAtFrame.rotation * posInCamera;
        Quaternion worldRot = cameraPoseAtFrame.rotation * rotInCamera;

        // =========================================================================
        // QUI AVVIENE LO SPOSTAMENTO MATEMATICO DELL'ORIGINE
        // Spostiamo la posizione finale calcolata lungo gli assi ruotati del marker
        // =========================================================================
        worldPos += worldRot * markerLocalOffset;

        LastReprojError = resp.reproj_error_px;
        LastNInliers    = resp.n_inliers;

        if (logPoseData)
            Debug.Log($"[PoseClient] pos={worldPos:F4}  rot={worldRot.eulerAngles:F1}  " +
                      $"reproj={LastReprojError:F2}px  inliers={LastNInliers}");

        // L'evento ora trasmette la nuova posizione (worldPos modificata) a AxisVisualizer e agli altri
        OnPoseDetected?.Invoke(worldPos, worldRot);
    }

    static Vector3 OpenCVTranslationToUnity(float[] t)
    {
        return new Vector3(t[0], -t[1], t[2]);
    }

    static Quaternion OpenCVRotationMatrixToUnity(float[] r)
    {
        Matrix4x4 m = Matrix4x4.identity;

        m.m00 =  r[0];  m.m01 = -r[1];  m.m02 =  r[2];
        m.m10 = -r[3];  m.m11 =  r[4];  m.m12 = -r[5];
        m.m20 =  r[6];  m.m21 = -r[7];  m.m22 =  r[8];

        return m.rotation;
    }

    void OnDestroy() => _running = false;

    public bool IsTrackingEnabled => _running;
    public void SetTrackingEnabled(bool enabled)
    {
        if (enabled && !_running)
        {
            StartCoroutine(CheckServerThenStart());
            Debug.Log("[PoseClient] Connessione al server in corso...");
        }
        else if (!enabled && _running)
        {
            _running = false;
            OnMarkerLost?.Invoke();
            Debug.Log("[PoseClient] Tracking fermato.");
        }
    }
}