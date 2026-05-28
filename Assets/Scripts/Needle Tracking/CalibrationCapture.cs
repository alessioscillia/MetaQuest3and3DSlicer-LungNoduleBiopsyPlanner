using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Cattura frame per la calibrazione della camera passthrough del Quest 3.
///
/// FUNZIONALITÀ:
///   - Preview live della camera fisica su un pannello UI in-headset
///   - Premi A per inviare il frame corrente al server (nessun limite)
///   - Premi B per eliminare l'ultimo frame inviato (se viene mosso/sfuocato)
///
/// SETUP in Unity:
///   1. Crea un Canvas in modalità "World Space" nella scena.
///      Posizionalo davanti alla camera, es. position=(0, 0, 1.5), scala=0.001.
///   2. Aggiungi un RawImage come figlio del Canvas (dimensione es. 960×720).
///   3. Aggiungi questo script a un GameObject vuoto.
///   4. Nell'Inspector assegna:
///        CameraCapture → il GameObject con CameraFrameCapture
///        PreviewImage  → il RawImage del Canvas
/// </summary>
public class CalibrationCapture : MonoBehaviour
{
    // -----------------------------------------------------------------------
    // Inspector
    // -----------------------------------------------------------------------
    [Header("Server (stesso pose_server.py, porta 5000)")]
    public string serverIP   = "127.0.0.1";
    public int    serverPort = 5000;

    [Header("Riferimento camera")]
    public CameraFrameCapture cameraCapture;

    [Header("Preview live")]
    [Tooltip("RawImage su un Canvas World Space — mostra il feed della camera fisica")]
    public RawImage previewImage;

    [Tooltip("Aspect ratio del pannello preview. Quest 3 camera fisica ≈ 4/3")]
    public float previewAspect = 4f / 3f;

    // -----------------------------------------------------------------------
    // Stato
    // -----------------------------------------------------------------------
    private int    _framesSent = 0;
    private bool   _sending    = false;
    private bool   _deleting   = false;
    private int    _lastIdx    = -1;
    private bool   _serverOk   = false;
    private string _statusMsg  = "Connessione al server…";

    private string _uploadUrl;
    private string _deleteUrl;

    // -----------------------------------------------------------------------
    void Start()
    {
        _uploadUrl = $"http://{serverIP}:{serverPort}/save_frame";
        _deleteUrl = $"http://{serverIP}:{serverPort}/delete_last_frame";

        if (cameraCapture == null)
            cameraCapture = GetComponent<CameraFrameCapture>();

        StartCoroutine(CheckServer());
        StartCoroutine(WaitAndBindPreview());
    }

    // -----------------------------------------------------------------------
    IEnumerator CheckServer()
    {
        using (UnityWebRequest req = UnityWebRequest.Get(
                   $"http://{serverIP}:{serverPort}/health"))
        {
            req.timeout = 5;
            yield return req.SendWebRequest();
            _serverOk  = req.result == UnityWebRequest.Result.Success;
            _statusMsg = _serverOk
                ? "Server OK  —  A = cattura  |  B = annulla ultimo"
                : $"Server NON raggiungibile: {req.error}";
        }
    }

    // Aspetta che CameraFrameCapture abbia avviato la WebCamTexture,
    // poi la collega al RawImage
    IEnumerator WaitAndBindPreview()
    {
        if (previewImage == null) yield break;

        yield return new WaitUntil(() => cameraCapture != null
                                      && cameraCapture.LiveTexture != null
                                      && cameraCapture.IsReady);

        previewImage.texture = cameraCapture.LiveTexture;

        // Ridimensiona il pannello per rispettare l'aspect ratio della camera
        RectTransform rt = previewImage.GetComponent<RectTransform>();
        float h = rt.sizeDelta.y;
        rt.sizeDelta = new Vector2(h * previewAspect, h);

        Debug.Log("[CalibrationCapture] Preview live collegata alla camera.");
    }

    // -----------------------------------------------------------------------
    void Update()
    {
        if (!_serverOk) return;

        // A — cattura e invia
        if (!_sending && OVRInput.GetDown(OVRInput.Button.One))
        {
            if (!cameraCapture.IsReady)
            {
                _statusMsg = "Camera non pronta, riprova…";
                return;
            }
            byte[] jpeg = cameraCapture.CaptureFrameAsJpeg();
            if (jpeg != null)
                StartCoroutine(SendFrame(jpeg));
        }

        // B — elimina l'ultimo frame inviato
        if (!_deleting && _lastIdx >= 0 && OVRInput.GetDown(OVRInput.Button.Two))
            StartCoroutine(DeleteLastFrame());
    }

    // -----------------------------------------------------------------------
    IEnumerator SendFrame(byte[] jpeg)
    {
        _sending   = true;
        _statusMsg = "Invio frame…";

        using (UnityWebRequest req = new UnityWebRequest(_uploadUrl, "POST"))
        {
            req.uploadHandler   = new UploadHandlerRaw(jpeg);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "image/jpeg");
            req.timeout = 5;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                var resp = JsonUtility.FromJson<SaveFrameResponse>(
                               req.downloadHandler.text);
                _lastIdx = resp.index;
                _framesSent++;
                _statusMsg = $"✓ Frame_{resp.index:D3} salvato  |  Totale: {resp.total}  |  B = annulla";
            }
            else
            {
                _statusMsg = $"Errore invio: {req.error}  —  riprova";
            }
        }

        _sending = false;
    }

    // -----------------------------------------------------------------------
    IEnumerator DeleteLastFrame()
    {
        _deleting  = true;
        _statusMsg = $"Eliminazione frame_{_lastIdx:D3}…";

        using (UnityWebRequest req = UnityWebRequest.Delete(
                   $"{_deleteUrl}?index={_lastIdx}"))
        {
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = 5;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                _framesSent = Mathf.Max(0, _framesSent - 1);
                _statusMsg  = $"Frame_{_lastIdx:D3} eliminato  |  Rimasti: {_framesSent}";
                _lastIdx    = -1;
            }
            else
            {
                _statusMsg = $"Errore eliminazione: {req.error}";
            }
        }

        _deleting = false;
    }

    // -----------------------------------------------------------------------
    // HUD
    // -----------------------------------------------------------------------
    void OnGUI()
    {
        var s = new GUIStyle(GUI.skin.box)
        {
            fontSize  = 23,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            padding   = new RectOffset(14, 14, 8, 8)
        };

        // Riga 1: contatore + stato
        s.normal.textColor = _serverOk ? Color.white : new Color(1f, 0.35f, 0.35f);
        GUI.Box(new Rect(10, 10, 700, 44),
            $"  Frame inviati: {_framesSent}    {_statusMsg}", s);

        // Riga 2: istruzioni sempre visibili
        var si = new GUIStyle(s) { fontSize = 18, fontStyle = FontStyle.Normal };
        si.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
        GUI.Box(new Rect(10, 58, 700, 34),
            "  A = cattura    B = elimina ultimo    (nessun limite di frame)", si);
    }

    // -----------------------------------------------------------------------
    [System.Serializable]
    private class SaveFrameResponse
    {
        public string saved;
        public int    index;
        public int    total;
    }
}
