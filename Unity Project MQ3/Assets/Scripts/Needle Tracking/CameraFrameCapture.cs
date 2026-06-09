using UnityEngine;
using Unity.Collections;
using Meta.XR;

/// <summary>
/// Cattura frame dalla camera passthrough del Quest 3 usando PassthroughCameraAccess,
/// invece di WebCamTexture.
///
/// In questo modo immagine, intrinseche, risoluzione e posa fisica della camera
/// provengono tutte dalla stessa API Meta.
/// </summary>
public class CameraFrameCapture : MonoBehaviour
{
    [Header("Meta Passthrough API")]
    [Tooltip("Trascina qui il GameObject che contiene PassthroughCameraAccess")]
    public PassthroughCameraAccess metaCameraAccess;

    [Header("Output JPEG")]
    [Range(1, 100)]
    [Tooltip("Qualità JPEG. 80-90 è un buon compromesso qualità/banda.")]
    public int jpegQuality = 85;

    [Header("Debug")]
    public bool logDebugInfo = true;

    // -----------------------------------------------------------------------
    // Stato interno
    // -----------------------------------------------------------------------

    private Texture2D _snapshot;

    /// <summary>
    /// True quando la camera passthrough Meta è attiva e ha prodotto almeno un frame.
    /// </summary>
    public bool IsReady
    {
        get
        {
            return metaCameraAccess != null
                && metaCameraAccess.enabled
                && metaCameraAccess.IsPlaying
                && metaCameraAccess.CurrentResolution.x > 16
                && metaCameraAccess.CurrentResolution.y > 16
                && metaCameraAccess.GetTexture() != null;
        }
    }

    /// <summary>
    /// Larghezza effettiva del frame camera.
    /// </summary>
    public int ActualWidth
    {
        get
        {
            if (metaCameraAccess == null)
                return 0;

            return metaCameraAccess.CurrentResolution.x;
        }
    }

    /// <summary>
    /// Altezza effettiva del frame camera.
    /// </summary>
    public int ActualHeight
    {
        get
        {
            if (metaCameraAccess == null)
                return 0;

            return metaCameraAccess.CurrentResolution.y;
        }
    }

    /// <summary>
    /// Texture live della camera.
    /// Può essere assegnata a una RawImage per preview.
    /// </summary>
    public Texture LiveTexture
    {
        get
        {
            if (metaCameraAccess == null || !metaCameraAccess.IsPlaying)
                return null;

            return metaCameraAccess.GetTexture();
        }
    }

    // -----------------------------------------------------------------------
    // Unity lifecycle
    // -----------------------------------------------------------------------

    void Start()
    {
        if (metaCameraAccess == null)
            metaCameraAccess = GetComponent<PassthroughCameraAccess>();

        if (metaCameraAccess == null)
        {
            Debug.LogError(
                "[CameraFrameCapture] PassthroughCameraAccess non trovato. " +
                "Assegnalo da Inspector oppure mettilo sullo stesso GameObject."
            );
            return;
        }

        if (logDebugInfo)
        {
            Debug.Log(
                "[CameraFrameCapture] Uso PassthroughCameraAccess come sorgente frame. " +
                "Assicurati che CameraPosition sia impostata correttamente su Left o Right."
            );
        }
    }

    // -----------------------------------------------------------------------
    // Cattura JPEG
    // -----------------------------------------------------------------------

    /// <summary>
    /// Cattura il frame corrente e lo restituisce come byte array JPEG.
    /// Ritorna null se la camera non è pronta.
    /// </summary>
    public byte[] CaptureFrameAsJpeg()
    {
        if (!IsReady)
        {
            if (logDebugInfo)
                Debug.LogWarning("[CameraFrameCapture] Camera non pronta: impossibile catturare il frame.");

            return null;
        }

        int w = metaCameraAccess.CurrentResolution.x;
        int h = metaCameraAccess.CurrentResolution.y;
        int pixelCount = w * h;

        if (w <= 0 || h <= 0)
        {
            Debug.LogWarning($"[CameraFrameCapture] Risoluzione non valida: {w} x {h}");
            return null;
        }

        // Crea o ridimensiona la Texture2D locale usata per codificare il JPEG
        if (_snapshot == null || _snapshot.width != w || _snapshot.height != h)
        {
            if (_snapshot != null)
                Destroy(_snapshot);

            _snapshot = new Texture2D(w, h, TextureFormat.RGBA32, false);

            if (logDebugInfo)
            {
                var intrinsics = metaCameraAccess.Intrinsics;

                Debug.LogWarning(
                    "[CameraFrameCapture] Snapshot inizializzato\n" +
                    $"CurrentResolution = {w} x {h}\n" +
                    $"SensorResolution  = {intrinsics.SensorResolution.x} x {intrinsics.SensorResolution.y}\n" +
                    $"FocalLength       = ({intrinsics.FocalLength.x}, {intrinsics.FocalLength.y})\n" +
                    $"PrincipalPoint    = ({intrinsics.PrincipalPoint.x}, {intrinsics.PrincipalPoint.y})"
                );
            }
        }

        /*
         * GetColors() legge i pixel della camera tramite PassthroughCameraAccess.
         * È più costoso di usare direttamente una texture GPU, ma per inviare frame
         * a un server Python a intervalli tipo 0.1 s va bene per ora.
         */
        NativeArray<Color32> colors = metaCameraAccess.GetColors();

        if (!colors.IsCreated || colors.Length < pixelCount)
        {
            Debug.LogWarning(
                $"[CameraFrameCapture] Buffer colori non valido. " +
                $"Length={colors.Length}, atteso almeno={pixelCount}"
            );
            return null;
        }

        /*
         * In alcune versioni dell'API il buffer può essere più grande del numero
         * effettivo di pixel. Per sicurezza prendiamo solo i primi w*h pixel.
         */
        NativeArray<Color32> pixelData = colors.Length == pixelCount
            ? colors
            : colors.GetSubArray(0, pixelCount);

        _snapshot.SetPixelData(pixelData, 0);
        _snapshot.Apply(false);

        return _snapshot.EncodeToJPG(jpegQuality);
    }

    // -----------------------------------------------------------------------
    // Cleanup
    // -----------------------------------------------------------------------

    void OnDestroy()
    {
        if (_snapshot != null)
        {
            Destroy(_snapshot);
            _snapshot = null;
        }
    }
}