using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.InputSystem;

// ---------------------------------------------------------------------------
// Struttura di deserializzazione JSON — deve rispecchiare esattamente
// il dizionario restituito da pose_server.py
// ---------------------------------------------------------------------------
[Serializable]
public class PoseResponse
{
    public bool   detected;
    public string reason;             // solo se detected == false
    public float[] rvec;              // vettore di Rodrigues (3 elem) — non usato in Unity ma utile per debug
    public float[] tvec;              // traslazione in METRI (3 elem): [tx, ty, tz]
    public float[] rmat;              // matrice di rotazione flat row-major (9 elem): [R00,R01,R02, R10,...]
    public float   reproj_error_px;
    public int     n_inliers;
}

// ---------------------------------------------------------------------------
/// <summary>
/// Invia periodicamente un frame JPEG al server Python di stima della posa,
/// converte il risultato nel sistema di coordinate Unity e notifica gli ascoltatori.
///
/// SETUP in Unity:
///   1. Aggiungi questo script allo stesso GameObject di CameraFrameCapture,
///      oppure assegna il riferimento manualmente nell'Inspector.
///   2. Inserisci l'IP del PC che esegue pose_server.py nella LAN WiFi.
///   3. Collega AxisVisualizer ai due eventi OnPoseDetected / OnMarkerLost.
/// </summary>
// ---------------------------------------------------------------------------
public class PoseClient : MonoBehaviour
{

    [Header("Compensazione Lente Fisica (Metri)")]
    [Tooltip("Offset locale tra l'occhio virtuale e la telecamera fisica del Quest 3. X=Destra, Y=Alto, Z=Avanti")]
    public Vector3 cameraPhysicalOffset = new Vector3(0f, -0.04f, 0.04f); // Valori di partenza stimati
    // -----------------------------------------------------------------------
    // Inspector
    // -----------------------------------------------------------------------
    [Header("Connessione al server Python")]
    [Tooltip("IP del PC nella stessa rete WiFi del Quest. Esempio: 192.168.1.50")]
    public string serverIP   = "127.0.0.1";
    public int    serverPort = 5000;

    [Header("Frequenza di campionamento")]
    [Tooltip("Intervallo in secondi tra un invio e il successivo (0.1 = 10 fps)")]
    public float captureIntervalSeconds = 0.1f;

    [Header("Riferimento camera")]
    [Tooltip("Se non assegnato, viene cercato automaticamente sul GameObject")]
    public CameraFrameCapture cameraCapture;

    [Header("Debug")]
    public bool logPoseData = true;

    // -----------------------------------------------------------------------
    // Stato pubblico (leggibile da AxisVisualizer)
    // -----------------------------------------------------------------------
    public float LastReprojError { get; private set; } = -1f;
    public int   LastNInliers    { get; private set; } = 0;

    // -----------------------------------------------------------------------
    // Eventi
    // -----------------------------------------------------------------------
    /// <summary>Fired quando la posa viene stimata con successo. Parametri: posizione e rotazione in world space Unity.</summary>
    public event Action<Vector3, Quaternion> OnPoseDetected;

    /// <summary>Fired quando il marker non è rilevabile nel frame corrente.</summary>
    public event Action OnMarkerLost;

    // -----------------------------------------------------------------------
    // Privati
    // -----------------------------------------------------------------------
    private bool   _running  = false;
    private string _poseUrl;
    private string _healthUrl;
    private bool _requestDebugNextFrame = false;

    // -----------------------------------------------------------------------
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
        // 1. Lettura dal controller del visore (tramite Meta XR)
        if (OVRInput.GetDown(OVRInput.Button.One)) 
        {
            _requestDebugNextFrame = true;
            Debug.LogWarning("[PoseClient] Hai premuto A! Il prossimo frame salverà il debug sul PC.");
        }

        // 2. Fallback per testare nell'Editor Unity (usando il NUOVO Input System)
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            _requestDebugNextFrame = true;
            Debug.LogWarning("[PoseClient] Hai premuto SPAZIO! Il prossimo frame salverà il debug sul PC.");
        }
    }
    // -----------------------------------------------------------------------
    // Verifica che il server sia raggiungibile prima di iniziare
    // -----------------------------------------------------------------------
    IEnumerator CheckServerThenStart()
    {
        Debug.Log($"[PoseClient] Verifica connessione a {_healthUrl} …");

        using (UnityWebRequest req = UnityWebRequest.Get(_healthUrl))
        {
            req.timeout = 5;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"[PoseClient] ✓ Server raggiungibile. Risposta: {req.downloadHandler.text}");
                _running = true;
                StartCoroutine(CaptureLoop());
            }
            else
            {
                Debug.LogError(
                    $"[PoseClient] ✗ Server NON raggiungibile ({req.error}).\n" +
                    $"  → Controlla che pose_server.py sia in esecuzione sul PC\n" +
                    $"  → Controlla che IP={serverIP} sia corretto\n" +
                    $"  → Verifica che PC e Quest siano sulla stessa rete WiFi");
            }
        }
    }

    // -----------------------------------------------------------------------
    // Loop principale: cattura → invio → parsing
    // -----------------------------------------------------------------------
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
        // Costruisci l'URL dinamicamente in base al flag
        string currentUrl = _poseUrl;
        
        if (_requestDebugNextFrame)
        {
            currentUrl += "?debug=true";
            _requestDebugNextFrame = false; // Resetta il flag subito dopo
        }

        // Usa currentUrl invece di _poseUrl
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

    // -----------------------------------------------------------------------
    // Parsing JSON e conversione di coordinate
    // -----------------------------------------------------------------------
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

        // Filtra stime inaffidabili prima di usarle
        const float MAX_REPROJ_PX = 0.8f;
        const int   MIN_INLIERS   = 12;

        if (resp.reproj_error_px > MAX_REPROJ_PX || resp.n_inliers < MIN_INLIERS)
        {
            if (logPoseData)
                Debug.Log($"[PoseClient] Stima scartata — reproj={resp.reproj_error_px:F2}px " +
                        $"inliers={resp.n_inliers} (sotto soglia)");
            // Non aggiornare la posa: mantieni l'ultima valida
            return;
        }


        // LOG GREZZO — prima di qualsiasi conversione
        Debug.Log($"[DEBUG RAW] tvec grezzo (m): x={resp.tvec[0]:F4} y={resp.tvec[1]:F4} z={resp.tvec[2]:F4}");
        Debug.Log($"[DEBUG RAW] rvec Rodrigues: {resp.rvec[0]:F4} {resp.rvec[1]:F4} {resp.rvec[2]:F4}");


        // 1. Converti posa da spazio-camera OpenCV a spazio-camera Unity
        Vector3 posInCamera = OpenCVTranslationToUnity(resp.tvec);
        Quaternion rotInCamera = OpenCVRotationMatrixToUnity(resp.rmat);

        // 2. APPLICA L'OFFSET HARDCODED AL PUNTO DI VISTA DELLA CAMERA
        // Dato che posInCamera è "quanto è distante il marker dalla telecamera", 
        // spostiamo idealmente la telecamera in avanti e in basso rispetto all'occhio.
        posInCamera += cameraPhysicalOffset; 

        // 3. Ottieni il transform della pupilla
        OVRPlugin.Posef cameraPose = OVRPlugin.GetNodePose(
            OVRPlugin.Node.EyeLeft, // Che ora sappiamo essere la camera 0
            OVRPlugin.Step.Render
        );

        // 4. Converti da OVRPlugin (Unity coordinate)
        Vector3 camPos = cameraPose.Position.FromFlippedZVector3f();
        Quaternion camRot = cameraPose.Orientation.FromFlippedZQuatf();

        // 5. Applica la trasformazione finale nel mondo
        Vector3 worldPos = camPos + camRot * posInCamera;
        Quaternion worldRot = camRot * rotInCamera;
        LastReprojError = resp.reproj_error_px;
        LastNInliers    = resp.n_inliers;

        if (logPoseData)
            Debug.Log($"[PoseClient] pos={worldPos:F4}  rot={worldRot.eulerAngles:F1}  " +
                      $"reproj={LastReprojError:F2}px  inliers={LastNInliers}");

        OnPoseDetected?.Invoke(worldPos, worldRot);
    }

    // -----------------------------------------------------------------------
    // Conversione sistemi di riferimento
    // -----------------------------------------------------------------------

    /// <summary>
    /// OpenCV camera frame: X→destra, Y↓basso, Z→avanti (destrorso)
    /// Unity  camera frame: X→destra, Y↑alto,  Z→avanti (sinistrorso)
    ///
    /// Conversione: nega Y.
    /// Il tvec arriva già in METRI dal server.
    /// </summary>
    static Vector3 OpenCVTranslationToUnity(float[] t)
    {
        return new Vector3(t[0], -t[1], t[2]);
    }

    /// <summary>
    /// Converte la matrice di rotazione 3×3 (flat row-major, 9 elementi) da
    /// OpenCV a Unity applicando il flip dell'asse Y:
    ///
    ///   R_unity = F · R_opencv · F,   F = diag(1, −1, 1)
    ///
    /// Questo equivale a negare le righe e le colonne che coinvolgono Y.
    /// </summary>
    static Quaternion OpenCVRotationMatrixToUnity(float[] r)
    {
        // Indici row-major: r[row * 3 + col]
        //
        //  R_unity[i,j] = F[i] * R_opencv[i,j] * F[j]
        //  dove F = {+1, -1, +1}
        //
        //  Quindi le celle (0,1), (1,0), (1,2), (2,1) cambiano segno.

        Matrix4x4 m = Matrix4x4.identity;

        m.m00 =  r[0];  m.m01 = -r[1];  m.m02 =  r[2];
        m.m10 = -r[3];  m.m11 =  r[4];  m.m12 = -r[5];
        m.m20 =  r[6];  m.m21 = -r[7];  m.m22 =  r[8];

        return m.rotation;
    }

    // -----------------------------------------------------------------------
    void OnDestroy() => _running = false;

    /// <summary>
    /// Avvia o ferma il loop di cattura frame.
    /// Chiamato da AnatomyManager tramite il tasto UI.
    /// </summary>
    public bool IsTrackingEnabled => _running;
    public void SetTrackingEnabled(bool enabled)
    {
        if (enabled && !_running)
        {
            _running = true;
            StartCoroutine(CaptureLoop());
            Debug.Log("[PoseClient] Tracking avviato.");
        }
        else if (!enabled && _running)
        {
            _running = false;
            // Notifica i listener (AxisVisualizer, TrajectoryDeviationCalculator...)
            // così fanno il cleanup visivo senza aspettare il prossimo frame
            OnMarkerLost?.Invoke();
            Debug.Log("[PoseClient] Tracking fermato.");
        }
    }
}
