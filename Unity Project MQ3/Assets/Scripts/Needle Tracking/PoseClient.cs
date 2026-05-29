using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.InputSystem;

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
    public Vector3 cameraPhysicalOffset = new Vector3(0f, -0.04f, 0.04f);

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

        if (cameraCapture == null)
        {
            Debug.LogError("[PoseClient] CameraFrameCapture non trovato! Aggiungilo allo stesso GameObject.");
            return;
        }

        Debug.Log($"[PoseClient] ✓ Server raggiungibile — tracking in attesa del comando UI.");
    }

    void Update()
    {
        if (OVRInput.GetDown(OVRInput.Button.One)) 
        {
            _requestDebugNextFrame = true;
            Debug.LogWarning("[PoseClient] Hai premuto A! Il prossimo frame salverà il debug sul PC.");
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
        while (_running)
        {
            yield return new WaitForSeconds(captureIntervalSeconds);

            if (!cameraCapture.IsReady)
                continue;

            byte[] jpeg = cameraCapture.CaptureFrameAsJpeg();
            if (jpeg == null)
                continue;

            yield return StartCoroutine(SendFrame(jpeg));
        }
    }

    IEnumerator SendFrame(byte[] jpeg)
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

            ProcessResponse(req.downloadHandler.text);
        }
    }

    void ProcessResponse(string json)
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

        const float MAX_REPROJ_PX = 0.8f;
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

        posInCamera += cameraPhysicalOffset; 

        OVRPlugin.Posef cameraPose = OVRPlugin.GetNodePose(
            OVRPlugin.Node.EyeLeft, 
            OVRPlugin.Step.Render
        );

        Vector3 camPos = cameraPose.Position.FromFlippedZVector3f();
        Quaternion camRot = cameraPose.Orientation.FromFlippedZQuatf();

        Vector3 worldPos = camPos + camRot * posInCamera;
        Quaternion worldRot = camRot * rotInCamera;

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