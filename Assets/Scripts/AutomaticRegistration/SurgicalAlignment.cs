using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Meta.XR.MRUtilityKit;
using System.Linq; // per .Aggregate nei Debug.Log

public class SurgicalAlignment : MonoBehaviour
{
    public static SurgicalAlignment Instance { get; private set; }

    public static bool IsSupported => MRUK.Instance != null;

    public static bool HasPermissions
#if UNITY_EDITOR
        => true;
#else
        => UnityEngine.Android.Permission.HasUserAuthorizedPermission(ScenePermission);
#endif

    public static bool TrackingEnabled
    {
        get => Instance && Instance._mrukInstance &&
               Instance._mrukInstance.SceneSettings.TrackerConfiguration.QRCodeTrackingEnabled;
        set
        {
            if (!Instance || !Instance._mrukInstance) return;
            var config = Instance._mrukInstance.SceneSettings.TrackerConfiguration;
            config.QRCodeTrackingEnabled = value;
            Instance._mrukInstance.SceneSettings.TrackerConfiguration = config;
        }
    }

    [SerializeField] private MRUK _mrukInstance;

    private GameObject _patientHologram; 
    private bool _alignmentDone = false; 

    private readonly Dictionary<string, MRUKTrackable> _detectedQRs = new();
    private readonly Dictionary<string, GameObject>    _debugVisuals = new();
    private readonly HashSet<MRUKTrackable> _knownTrackables = new();

    private static readonly Color[] QR_COLORS =
        { Color.green, Color.cyan, Color.yellow, Color.magenta };

    private Coroutine _recoveryCoroutine;
    private Coroutine _delayedResetCoroutine;

    [Header("Debug Visuals")]
    [SerializeField] private bool _showCenterDebug = true; // Sferetta magenta al centro dei 4 QR

    private GameObject _alignmentCenterVisual; // Riferimento al marker centrale

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    void OnValidate()
    {
        if (!_mrukInstance &&
            FindAnyObjectByType<MRUK>() is { } mruk &&
            mruk.gameObject.scene == gameObject.scene)
            _mrukInstance = mruk;
    }

    void Start()
    {
        if (!_mrukInstance)   { Debug.LogError("[SA] MRUK not found.");           return; }
        if (!IsSupported)     { Debug.LogError("[SA] QR tracking not supported."); return; }
        if (!HasPermissions)  { Debug.LogWarning("[SA] Permission not granted.");  return; }
        if (!TrackingEnabled) { Debug.LogWarning("[SA] QR tracking not enabled."); return; }

        _mrukInstance.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
        _mrukInstance.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);

        ScanExistingTrackablesOnce();

        Debug.Log("[SA] QR Code tracking initialized.");
    }

    private void ScanExistingTrackablesOnce()
    {
        MRUKTrackable[] existing = FindObjectsByType<MRUKTrackable>(FindObjectsSortMode.None);
        foreach (var t in existing)
        {
            if (t.TrackableType == OVRAnchor.TrackableType.QRCode)
            {
                _knownTrackables.Add(t);
                RegisterOrUpdateTrackable(t);
            }
        }

        if (!_alignmentDone && _detectedQRs.Count < 4)
            StartRecovery();
    }

    private void StartRecovery()
    {
        if (_recoveryCoroutine != null) return;
        _recoveryCoroutine = StartCoroutine(RecoveryCoroutine());
    }

    private void StopRecovery()
    {
        if (_recoveryCoroutine == null) return;
        StopCoroutine(_recoveryCoroutine);
        _recoveryCoroutine = null;
    }

    private IEnumerator RecoveryCoroutine()
    {
        yield return new WaitForSeconds(1.0f);

        while (!_alignmentDone && _detectedQRs.Count < 4)
        {
            foreach (var t in _knownTrackables)
            {
                if (t != null && t.TrackableType == OVRAnchor.TrackableType.QRCode)
                    RegisterOrUpdateTrackable(t);
            }
            yield return new WaitForSeconds(2.0f);
        }

        _recoveryCoroutine = null;
        Debug.Log("[SA] Recovery fermato.");
    }

    private void OnTrackableAdded(MRUKTrackable trackable)
    {
        if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode) return;

        _knownTrackables.Add(trackable);
        RegisterOrUpdateTrackable(trackable);
    }

    private void OnTrackableRemoved(MRUKTrackable trackable)
    {
        if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode) return;

        if (!_alignmentDone && _detectedQRs.Count < 4)
            StartRecovery();

        Debug.Log($"[SA] QR perso temporaneamente: '{trackable.MarkerPayloadString}'. Salvati: {_detectedQRs.Count}/4");
    }

    private void RegisterOrUpdateTrackable(MRUKTrackable trackable)
    {
        if (_alignmentDone) return;

        string payload = trackable.MarkerPayloadString;
        if (string.IsNullOrEmpty(payload)) return;

        if (_detectedQRs.ContainsKey(payload))
        {
            _detectedQRs[payload] = trackable;
            return;
        }

        int colorIndex = _detectedQRs.Count % QR_COLORS.Length;
        _detectedQRs.Add(payload, trackable);
        Debug.Log($"[SA] Nuovo QR: '{payload}' (#{_detectedQRs.Count}/4)");

        GameObject visual = CreateDebugVisual(trackable, payload, QR_COLORS[colorIndex]);
        _debugVisuals.Add(payload, visual);

        UpdateAllCounterLabels();
        TryAlign();

        if (_detectedQRs.Count < 4)
            StartRecovery();
        else
            StopRecovery();
    }

    void OnDestroy()
    {
        StopRecovery();
        if (_mrukInstance != null)
        {
            _mrukInstance.SceneSettings.TrackableAdded.RemoveListener(OnTrackableAdded);
            _mrukInstance.SceneSettings.TrackableRemoved.RemoveListener(OnTrackableRemoved);
        }
        if (Instance == this) Instance = null;
    }

    public static void RequestRequiredPermissions(Action<bool> onRequestComplete)
    {
        if (!Instance) { onRequestComplete?.Invoke(false); return; }
#if UNITY_EDITOR
        onRequestComplete?.Invoke(HasPermissions);
#else
        var callbacks = new UnityEngine.Android.PermissionCallbacks();
        if (onRequestComplete is not null)
        {
            callbacks.PermissionGranted += _ => onRequestComplete(HasPermissions);
            callbacks.PermissionDenied  += _ => onRequestComplete(HasPermissions);
#if !UNITY_6000_0_OR_NEWER
            callbacks.PermissionDeniedAndDontAskAgain += _ => onRequestComplete(HasPermissions);
#endif
        }
        UnityEngine.Android.Permission.RequestUserPermission(ScenePermission, callbacks);
#endif
    }

    public void SetHologram(GameObject loadedHologram)
    {
        _patientHologram = loadedHologram;
        Debug.Log("[SA] Hologram ricevuto.");
        TryAlign();
    }

    // Questo è il metodo pubblico che verrà chiamato dall'esterno
    public void StartDelayedReset(float delayInSeconds = 5f)
    {
        if (_delayedResetCoroutine != null) StopCoroutine(_delayedResetCoroutine);
        _delayedResetCoroutine = StartCoroutine(DelayedResetRoutine(delayInSeconds));
    }

    // La Coroutine che gestisce l'attesa
    private IEnumerator DelayedResetRoutine(float delay)
    {
        Debug.Log($"[SA] Reset richiesto. Attesa di {delay} secondi per inquadrare i QR...");

        if (_patientHologram != null) _patientHologram.SetActive(false);

        // 1. Fermiamo le routine in corso e il tracking
        StopRecovery();
        _alignmentDone = false;
        TrackingEnabled = false;

        // Disconnettiamo gli eventi di MRUK
        if (_mrukInstance != null)
        {
            _mrukInstance.SceneSettings.TrackableAdded.RemoveListener(OnTrackableAdded);
            _mrukInstance.SceneSettings.TrackableRemoved.RemoveListener(OnTrackableRemoved);
        }

        // 2. FONDAMENTALE: Distruggiamo fisicamente i vecchi Trackable dalla scena di Unity!
        MRUKTrackable[] existing = FindObjectsByType<MRUKTrackable>(FindObjectsSortMode.None);
        foreach (var t in existing)
        {
            if (t != null && t.TrackableType == OVRAnchor.TrackableType.QRCode)
            {
                Destroy(t.gameObject);
            }
        }

        // Pulizia degli indicatori visivi e delle nostre liste interne
        foreach (var v in _debugVisuals.Values) if (v != null) Destroy(v);
        if (_alignmentCenterVisual != null) Destroy(_alignmentCenterVisual);
        
        _detectedQRs.Clear();
        _debugVisuals.Clear();
        _knownTrackables.Clear(); 

        // 3. Aspettiamo i secondi con la scena pulita
        yield return new WaitForSeconds(delay);

        Debug.Log("[SA] Attesa terminata. Riattivo il tracking QR per una scansione fresca...");

        // 4. Facciamo ripartire la scansione da zero
        TrackingEnabled = true;

        if (_mrukInstance != null)
        {
            _mrukInstance.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
            _mrukInstance.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);
        }

        // A questo punto la scena è vuota, quindi MRUK interrogherà fisicamente il visore
        ScanExistingTrackablesOnce();
    }

    private void TryAlign()
    {
        if (_alignmentDone || _detectedQRs.Count < 4) return;
        if (_patientHologram == null)
        {
            Debug.Log("[SA] 4 QR trovati! Attendo il modello...");
            return;
        }
        PerformAlignment();
    }

    // ── CONFIGURAZIONE LAYOUT ── (assegnabile dall'Inspector)
    [Header("QR Layout - Payload e Misure Fisiche")]
    [SerializeField] private string _payloadTopLeft     = "Alto Sinistra";
    [SerializeField] private string _payloadTopRight    = "Alto Destra";
    [SerializeField] private string _payloadBottomLeft  = "Basso Sinistra";
    [SerializeField] private string _payloadBottomRight = "Basso Destra";
    [SerializeField] private float  _qrSize_m           = 0.073f;  // lato QR in metri
    [SerializeField] private float  _qrSpacingH_m       = 0.088f;  // distanza orizzontale tra centri QR
    [SerializeField] private float  _qrSpacingV_m       = 0.088f;  // distanza verticale tra centri QR
    [SerializeField] private float  _qualityThreshold   = 0.80f;   // soglia per considerare un QR affidabile

    private void PerformAlignment()
    {
        var qrQuality = new Dictionary<string, float>();
        var qrPos     = new Dictionary<string, Vector3>();

        foreach (var kv in _detectedQRs)
        {
            string payload   = kv.Key;
            var    trackable = kv.Value;
            float  quality   = 1f;

            if (trackable.PlaneRect.HasValue)
            {
                float estimatedSize = (trackable.PlaneRect.Value.width + trackable.PlaneRect.Value.height) / 2f;
                float ratio = estimatedSize / _qrSize_m;
                quality = Mathf.Clamp(ratio * ratio, 0.05f, 1f);
            }

            qrQuality[payload] = quality;
            qrPos[payload]     = trackable.transform.position;
            Debug.Log($"[SA] QR '{payload}': qualità={quality:F3}, buono={quality >= _qualityThreshold}");
        }

        bool Good(string p) => qrQuality.ContainsKey(p) && qrQuality[p] >= _qualityThreshold;

        // Y robusta: il bias PnP spinge sempre verso il visore → il minimo Y è il più accurato
        float robustY = qrPos.Values.Min(p => p.y);
        Vector3 Flat(Vector3 v) => new Vector3(v.x, robustY, v.z);

        // ── Assi del layout derivati dal transform QR ──────────────────
        // Tutti i QR hanno lo stesso orientamento sul piano orizzontale:
        //   localRight ≈ -X world  →  LayoutRight = -localRight = +X world (TL→TR)
        //   localUp    ≈ +Z world  →  LayoutDown  = -localUp   = -Z world (TL→BL)
        // Usiamo il QR con qualità migliore come riferimento.
        var refT = _detectedQRs.Values
            .OrderByDescending(t => qrQuality.ContainsKey(t.MarkerPayloadString)
                                    ? qrQuality[t.MarkerPayloadString] : 0f)
            .First();
        Vector3 layoutRight = Vector3.ProjectOnPlane(-refT.transform.right, Vector3.up).normalized;
        Vector3 layoutDown  = Vector3.ProjectOnPlane(-refT.transform.up,    Vector3.up).normalized;
        float   half        = _qrSize_m / 2f;

        Debug.Log($"[SA] LayoutRight={layoutRight:F2}, LayoutDown={layoutDown:F2}");

        // ── Strategia A: diagonale — geometricamente perfetta ──────────
        // Il centro di un rettangolo = punto medio di qualsiasi diagonale.
        if (Good(_payloadTopLeft) && Good(_payloadBottomRight))
        {
            Debug.Log("[SA] Strategia: diagonale TL-BR");
            Finalize((Flat(qrPos[_payloadTopLeft]) + Flat(qrPos[_payloadBottomRight])) / 2f);
            return;
        }
        if (Good(_payloadTopRight) && Good(_payloadBottomLeft))
        {
            Debug.Log("[SA] Strategia: diagonale TR-BL");
            Finalize((Flat(qrPos[_payloadTopRight]) + Flat(qrPos[_payloadBottomLeft])) / 2f);
            return;
        }

        // ── Strategia B: coppia adiacente ──────────────────────────────
        // Centro = punto medio della coppia + mezzo-lato QR nella direzione inward.
        // Il "vertice interno" di un QR è a esattamente _qrSize_m/2 dal suo centro
        // nella direzione che punta verso il centro del layout.
        if (Good(_payloadTopLeft) && Good(_payloadTopRight))
        {
            Vector3 mid = (Flat(qrPos[_payloadTopLeft]) + Flat(qrPos[_payloadTopRight])) / 2f;
            Debug.Log("[SA] Strategia: coppia Top → vertici bassi (offset +LayoutDown)");
            Finalize(mid + layoutDown * half); return;
        }
        if (Good(_payloadBottomLeft) && Good(_payloadBottomRight))
        {
            Vector3 mid = (Flat(qrPos[_payloadBottomLeft]) + Flat(qrPos[_payloadBottomRight])) / 2f;
            Debug.Log("[SA] Strategia: coppia Bottom → vertici alti (offset -LayoutDown)");
            Finalize(mid - layoutDown * half); return;
        }
        if (Good(_payloadTopLeft) && Good(_payloadBottomLeft))
        {
            Vector3 mid = (Flat(qrPos[_payloadTopLeft]) + Flat(qrPos[_payloadBottomLeft])) / 2f;
            Debug.Log("[SA] Strategia: coppia Left → vertici destra (offset +LayoutRight)");
            Finalize(mid + layoutRight * half); return;
        }
        if (Good(_payloadTopRight) && Good(_payloadBottomRight))
        {
            Vector3 mid = (Flat(qrPos[_payloadTopRight]) + Flat(qrPos[_payloadBottomRight])) / 2f;
            Debug.Log("[SA] Strategia: coppia Right → vertici sinistra (offset -LayoutRight)");
            Finalize(mid - layoutRight * half); return;
        }

        // ── Strategia C: singolo QR → vertice interno diagonale ────────
        // Il centro del layout coincide con il vertice del QR che punta verso il centro.
        // TL → vertice basso-destra: +LayoutRight +LayoutDown
        // TR → vertice basso-sinistra: -LayoutRight +LayoutDown
        // BL → vertice alto-destra:  +LayoutRight -LayoutDown
        // BR → vertice alto-sinistra: -LayoutRight -LayoutDown
        if (Good(_payloadTopLeft))
        {
            Debug.Log("[SA] Strategia: singolo TL → vertice basso-destra");
            Finalize(Flat(qrPos[_payloadTopLeft]) + layoutRight * half + layoutDown * half);
            return;
        }
        if (Good(_payloadTopRight))
        {
            Debug.Log("[SA] Strategia: singolo TR → vertice basso-sinistra");
            Finalize(Flat(qrPos[_payloadTopRight]) - layoutRight * half + layoutDown * half);
            return;
        }
        if (Good(_payloadBottomLeft))
        {
            Debug.Log("[SA] Strategia: singolo BL → vertice alto-destra");
            Finalize(Flat(qrPos[_payloadBottomLeft]) + layoutRight * half - layoutDown * half);
            return;
        }
        if (Good(_payloadBottomRight))
        {
            Debug.Log("[SA] Strategia: singolo BR → vertice alto-sinistra");
            Finalize(Flat(qrPos[_payloadBottomRight]) - layoutRight * half - layoutDown * half);
            return;
        }

        // ── Fallback: nessun QR sopra soglia ───────────────────────────
        Debug.LogWarning("[SA] Nessun QR buono trovato — fallback a media pesata.");
        float totalW = 0f, fx = 0f, fz = 0f;
        foreach (var kv in qrPos)
        {
            float w = qrQuality[kv.Key];
            fx += kv.Value.x * w; fz += kv.Value.z * w; totalW += w;
        }
        Finalize(new Vector3(fx / totalW, robustY, fz / totalW));
    }

    private void Finalize(Vector3 finalCenter)
    {
        Debug.Log($"[SA] Centro finale: {finalCenter}");
        _patientHologram.transform.position      = finalCenter;
        _patientHologram.transform.localRotation = Quaternion.identity;
        _patientHologram.transform.localScale    = new Vector3(0.001f, 0.001f, 0.001f);

        if (_patientHologram != null) _patientHologram.SetActive(true);

        _alignmentDone = true;
        StopRecovery();
        TrackingEnabled = false;

        if (_mrukInstance != null)
        {
            _mrukInstance.SceneSettings.TrackableAdded.RemoveListener(OnTrackableAdded);
            _mrukInstance.SceneSettings.TrackableRemoved.RemoveListener(OnTrackableRemoved);
        }

        if (_showCenterDebug) CreateCenterDebugVisual(finalCenter);
    }

    // --- Crea un indicatore visibile per il centro ---
    private void CreateCenterDebugVisual(Vector3 centerPos)
    {
        if (_alignmentCenterVisual != null) Destroy(_alignmentCenterVisual);

        // Crea una sfera per identificare il centro esatto
        _alignmentCenterVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(_alignmentCenterVisual.GetComponent<Collider>()); // Rimuove il collider per non intralciare
        
        // La piazziamo nel centro esatto
        _alignmentCenterVisual.transform.position = centerPos;
        _alignmentCenterVisual.transform.localScale = Vector3.one * 0.03f; // 3 cm di diametro
        _alignmentCenterVisual.name = "Alignment_Center_Pivot_Marker";

        // Materiale Magenta acceso per essere distinto chiaramente dal rosso del nodulo
        Material mat = new Material(Shader.Find("Standard"));
        mat.color = Color.magenta;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", Color.magenta * 0.4f);
        _alignmentCenterVisual.GetComponent<Renderer>().material = mat;

        // Impostiamo la sfera come figlia dell'ologramma. 
        _alignmentCenterVisual.transform.SetParent(_patientHologram.transform, true);

        Debug.Log($"[SA] Creato marker Magenta al centro dei 4 QR (posizione: {centerPos}). Coincide col Pivot del modello.");
    }

    private void UpdateAllCounterLabels()
    {
        foreach (var visual in _debugVisuals.Values)
        {
            if (visual == null) continue;
            Transform ct = visual.transform.Find("Graphics/CounterLabel");
            if (ct == null) continue;
            TextMesh tm = ct.GetComponent<TextMesh>();
            if (tm != null) tm.text = $"{_detectedQRs.Count}/4";
        }
    }

    private GameObject CreateDebugVisual(MRUKTrackable trackable, string payload, Color color)
    {
        GameObject container = new GameObject($"Debug_QR_{payload}"); 
        container.transform.SetParent(trackable.transform, false); 

        GameObject graphicsRoot = new GameObject("Graphics"); 
        graphicsRoot.transform.SetParent(container.transform, false); 
        graphicsRoot.transform.localRotation = Quaternion.Euler(0, 180, 0); 

        float width  = (trackable.PlaneRect.HasValue ? trackable.PlaneRect.Value.width  : 0.15f) * 1.1f; 
        float height = (trackable.PlaneRect.HasValue ? trackable.PlaneRect.Value.height : 0.15f) * 1.1f;
        
        float w = width  / 2f;
        float h = height / 2f;

        GameObject outlineObj = new GameObject("Outline");
        outlineObj.transform.SetParent(graphicsRoot.transform, false); 
        LineRenderer lr = outlineObj.AddComponent<LineRenderer>(); 
        lr.useWorldSpace = false;
        lr.loop          = true;
        lr.positionCount = 4;
        lr.startWidth    = 0.006f;
        lr.endWidth      = 0.006f;
        lr.material      = new Material(Shader.Find("Sprites/Default"));
        lr.startColor    = color;
        lr.endColor      = color;
        lr.SetPosition(0, new Vector3(-w, -h, 0));
        lr.SetPosition(1, new Vector3( w, -h, 0));
        lr.SetPosition(2, new Vector3( w,  h, 0));
        lr.SetPosition(3, new Vector3(-w,  h, 0));

        GameObject textObj = new GameObject("TextInfo");
        textObj.transform.SetParent(graphicsRoot.transform, false); 
        textObj.transform.localPosition = new Vector3(0, h + 0.04f, 0);
        textObj.transform.localScale    = Vector3.one * 0.005f;
        TextMesh tm = textObj.AddComponent<TextMesh>();
        tm.text      = payload;
        tm.fontSize  = 20;
        tm.anchor    = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color     = color;

        GameObject counterObj = new GameObject("CounterLabel");
        counterObj.transform.SetParent(graphicsRoot.transform, false);
        counterObj.transform.localPosition = new Vector3(0, -(h + 0.02f), 0);
        counterObj.transform.localScale    = Vector3.one * 0.004f;
        TextMesh ctm = counterObj.AddComponent<TextMesh>();
        ctm.text      = $"{_detectedQRs.Count}/4";
        ctm.fontSize  = 20;
        ctm.anchor    = TextAnchor.MiddleCenter;
        ctm.alignment = TextAlignment.Center;
        ctm.color     = Color.white;

        return container;
    }
}