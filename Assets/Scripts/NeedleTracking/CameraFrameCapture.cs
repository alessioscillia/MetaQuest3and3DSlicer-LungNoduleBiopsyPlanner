using UnityEngine;

/// <summary>
/// Cattura frame dalla camera passthrough del Quest 3 tramite WebCamTexture
/// e li espone come array di byte JPEG pronti per essere inviati al server Python.
///
/// SETUP in Unity:
///   1. Aggiungi questo script a un GameObject vuoto nella scena.
///   2. Nel Meta XR Project Setup Tool, assicurati che il permesso CAMERA sia abilitato.
///   3. In Edit > Project Settings > XR Plug-in Management > Meta XR, abilita
///      "Passthrough" nelle funzionalità del visore.
///   4. Imposta cameraIndex = 0 (occhio sinistro) o 1 (occhio destro).
/// </summary>
public class CameraFrameCapture : MonoBehaviour
{
    [Header("Camera Settings")]
    [Tooltip("0 = camera sinistra, 1 = camera destra sul Quest 3")]
    public int cameraIndex = 0;

    [Tooltip("Risoluzione richiesta — il device usa la più vicina disponibile")]
    public int requestedWidth  = 1280;
    public int requestedHeight = 960;
    public int requestedFPS    = 30;

    [Header("Output")]
    [Range(1, 100)]
    [Tooltip("Qualità JPEG (1-100). 80-90 è un buon compromesso qualità/banda.")]
    public int jpegQuality = 85;

    // --- Stato interno ---
    private WebCamTexture _webcam;
    private Texture2D     _snapshot;

    /// <summary>True quando la camera è attiva e ha prodotto almeno un frame.</summary>
    public bool IsReady => _webcam != null
                        && _webcam.isPlaying
                        && _webcam.width > 16;   // evita frame "non inizializzati"

    /// <summary>Risoluzione effettiva dopo l'inizializzazione.</summary>
    public int ActualWidth  => _webcam != null ? _webcam.width  : 0;
    public int ActualHeight => _webcam != null ? _webcam.height : 0;

    /// <summary>
    /// Texture live della camera — assegnala a un RawImage per la preview in-headset.
    /// È null finché StartCamera() non viene chiamato.
    /// </summary>
    public WebCamTexture LiveTexture => _webcam;

    // -----------------------------------------------------------------------
    void Start()
    {
        // Verifica permesso camera (Android / Quest)
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                UnityEngine.Android.Permission.Camera))
        {
            UnityEngine.Android.Permission.RequestUserPermission(
                UnityEngine.Android.Permission.Camera);
        }
#endif
        StartCamera();
    }

    // -----------------------------------------------------------------------
    /// <summary>Inizializza e avvia la WebCamTexture.</summary>
    public void StartCamera()
    {
        WebCamDevice[] devices = WebCamTexture.devices;

        if (devices.Length == 0)
        {
            Debug.LogError("[CameraFrameCapture] Nessuna camera trovata nel sistema.");
            return;
        }

        // Log di tutte le camere disponibili (utile per trovare i nomi corretti)
        for (int i = 0; i < devices.Length; i++)
            Debug.Log($"[CameraFrameCapture] Camera {i}: \"{devices[i].name}\"");

        int idx = Mathf.Clamp(cameraIndex, 0, devices.Length - 1);
        string camName = devices[idx].name;

        _webcam  = new WebCamTexture(camName, requestedWidth, requestedHeight, requestedFPS);
        // La Texture2D verrà ridimensionata alla prima cattura
        _snapshot = new Texture2D(requestedWidth, requestedHeight, TextureFormat.RGB24, false);

        _webcam.Play();
        Debug.Log($"[CameraFrameCapture] Avviata \"{camName}\" | " +
                  $"richiesta: {requestedWidth}×{requestedHeight} @ {requestedFPS}fps");
    }

    // -----------------------------------------------------------------------
    /// <summary>
    /// Cattura il frame corrente e lo restituisce come byte array JPEG.
    /// Ritorna null se la camera non è pronta.
    /// Chiamare dall'esterno solo quando <see cref="IsReady"/> è true.
    /// </summary>
    public byte[] CaptureFrameAsJpeg()
    {
        if (_webcam == null || !_webcam.isPlaying)
            return null;

        int w = _webcam.width;
        int h = _webcam.height;

        // Ridimensiona la texture di snapshot se la risoluzione effettiva è diversa
        if (_snapshot.width != w || _snapshot.height != h)
        {
            _snapshot.Reinitialize(w, h, TextureFormat.RGB24, false);
            Debug.Log($"[CameraFrameCapture] Risoluzione effettiva: {w}×{h}");
        }

        // Copia pixel dalla WebCamTexture alla Texture2D e codifica in JPEG
        _snapshot.SetPixels(_webcam.GetPixels());
        _snapshot.Apply();

        return _snapshot.EncodeToJPG(jpegQuality);
    }

    // -----------------------------------------------------------------------
    void OnDestroy()
    {
        if (_webcam != null && _webcam.isPlaying)
        {
            _webcam.Stop();
            Debug.Log("[CameraFrameCapture] Camera fermata.");
        }
    }
}
