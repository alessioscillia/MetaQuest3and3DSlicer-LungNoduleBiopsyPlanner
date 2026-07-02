using System;
using System.Collections.Generic;
using UnityEngine;
using Meta.XR.MRUtilityKit;
using System.Linq;

public class SurgicalAlignment : MonoBehaviour
{
    public static SurgicalAlignment Instance { get; private set; }
    public static bool IsSupported => MRUK.Instance != null;
    private const string ScenePermission = "com.oculus.permission.USE_SCENE";

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
    private bool _isTracking = true;
    private bool _alignmentDone = false;

    private readonly Dictionary<string, MRUKTrackable> _detectedQRs = new();
    private readonly Dictionary<string, GameObject>    _debugVisuals = new();

    // ── CACHE: allocate once, reuse every frame ──
    private readonly Dictionary<string, float>   _qrQualityCache = new();
    private readonly Dictionary<string, Vector3> _qrPosCache     = new();

    private static readonly Color[] QR_COLORS =
        { Color.green, Color.cyan, Color.yellow, Color.magenta };

    [Header("Debug Visuals")]
    [SerializeField] private bool _showCenterDebug = true;
    private GameObject _alignmentCenterVisual;

    [Header("QR Layout - Payload e Misure Fisiche")]
    [SerializeField] private string _payloadTopLeft     = "Alto Sinistra";
    [SerializeField] private string _payloadTopRight    = "Alto Destra";
    [SerializeField] private string _payloadBottomLeft  = "Basso Sinistra";
    [SerializeField] private string _payloadBottomRight = "Basso Destra";
    [SerializeField] private float  _qrSize_m           = 0.073f;
    [SerializeField] private float  _qualityThreshold   = 0.80f;

    [Header("Tracking Speed")]
    [SerializeField] private float _positionSmoothSpeed = 25f; // alzato da 15
    [SerializeField] private float _rotationSmoothSpeed = 20f; // alzato da 10

    [Header("Model Orientation")]
    [SerializeField] private bool _alignModelToQrLayout = true;
    [SerializeField] private float _modelYawOffsetDegrees = 0f;

    // [NUOVO] Se true, la prima volta che aggancia fa snap istantaneo senza Lerp
    [SerializeField] private bool _snapOnFirstLock = true;

    private Quaternion _initialSheetRot = Quaternion.identity;
    private Vector3    _targetPosition;
    private Quaternion _targetRotation;
    private static readonly Vector3 HologramScale = new Vector3(0.001f, 0.001f, 0.001f);

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
        if (!_mrukInstance)   { Debug.LogError("[SA] MRUK non trovato.");           return; }
        if (!IsSupported)     { Debug.LogError("[SA] QR tracking non supportato."); return; }
        if (!HasPermissions)  { Debug.LogWarning("[SA] Permessi non concessi.");    return; }

        _mrukInstance.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
        _mrukInstance.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);

        TrackingEnabled = true;
        _isTracking = true;
        ScanExistingTrackablesOnce();
        Debug.Log("[SA] QR Code tracking Inizializzato in Real-Time.");
    }

    void Update()
    {
        if (!_isTracking || _patientHologram == null) return;
        if (_detectedQRs.Count == 0) return;

        // Riusa i dizionari cachati invece di allocarne di nuovi
        if (!TryComputeCenter(out Vector3 newCenter, out Quaternion newSheetRot, logDebug: false))
            return;

        if (!_alignmentDone)
        {
            // --- PRIMO AGGANCIO: snap istantaneo se abilitato ---
            _initialSheetRot = newSheetRot;
            _targetPosition  = newCenter;
            _targetRotation  = _alignModelToQrLayout
                ? ApplyModelYawOffset(newSheetRot)
                : Quaternion.identity;

            _patientHologram.transform.position   = _targetPosition;
            _patientHologram.transform.rotation   = _targetRotation;
            _patientHologram.transform.localScale = HologramScale; // ← set SOLO qui

            _alignmentDone = true;
            Debug.Log("[SA] Modello Agganciato! (Inseguimento Real-Time Attivo).");
        }
        else
        {
            _targetPosition = newCenter;
            _targetRotation = _alignModelToQrLayout
                ? ApplyModelYawOffset(newSheetRot)
                : newSheetRot * Quaternion.Inverse(_initialSheetRot);
        }

        Transform t = _patientHologram.transform;

        if (_snapOnFirstLock && !_alignmentDone)
        {
            // Già gestito sopra
        }
        else
        {
            // Lerp/Slerp con velocità aumentata = meno lag
            t.position = Vector3.Lerp(t.position, _targetPosition,
                                      Time.deltaTime * _positionSmoothSpeed);
            t.rotation = Quaternion.Slerp(t.rotation, _targetRotation,
                                          Time.deltaTime * _rotationSmoothSpeed);
        }

        // NON reimpostare localScale ogni frame
        // t.localScale = HologramScale; ← rimosso

        // Aggiorna solo la posizione del marker, senza distruggere/ricreare
        if (_showCenterDebug) MoveCenterDebugVisual(newCenter);
    }

    // ── FIX PRINCIPALE: sposta il marker invece di distruggerlo ogni frame ──
    private void MoveCenterDebugVisual(Vector3 centerPos)
    {
        if (_alignmentCenterVisual == null)
        {
            // Crea UNA SOLA VOLTA
            _alignmentCenterVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(_alignmentCenterVisual.GetComponent<Collider>());
            _alignmentCenterVisual.transform.localScale = Vector3.one * 0.03f;
            _alignmentCenterVisual.name = "Alignment_Center_Pivot_Marker";

            Material mat = new Material(Shader.Find("Standard"));
            mat.color = Color.magenta;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.magenta * 0.4f);
            _alignmentCenterVisual.GetComponent<Renderer>().material = mat;

            if (_patientHologram != null)
                _alignmentCenterVisual.transform.SetParent(_patientHologram.transform, true);
        }

        // Ogni frame: muovi soltanto, nessuna allocazione
        _alignmentCenterVisual.transform.position = centerPos;
    }

    public bool ToggleTracking()
    {
        _isTracking = !_isTracking;
        TrackingEnabled = _isTracking;

        if (_isTracking)
        {
            Debug.Log("[SA] Tracking: RIATTIVATO.");
            ClearQRMemory();
            _mrukInstance.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
            _mrukInstance.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);
        }
        else
        {
            Debug.Log("[SA] Tracking: IN PAUSA.");
            _mrukInstance.SceneSettings.TrackableAdded.RemoveListener(OnTrackableAdded);
            _mrukInstance.SceneSettings.TrackableRemoved.RemoveListener(OnTrackableRemoved);
            HideAllDebugVisuals();
        }

        return _isTracking;
    }

    private void HideAllDebugVisuals()
    {
        foreach (var v in _debugVisuals.Values)
            if (v != null) Destroy(v);
        _debugVisuals.Clear();

        if (_alignmentCenterVisual != null)
        {
            Destroy(_alignmentCenterVisual);
            _alignmentCenterVisual = null;
        }
    }

    private void ClearQRMemory()
    {
        MRUKTrackable[] existing = FindObjectsByType<MRUKTrackable>(FindObjectsSortMode.None);
        foreach (var t in existing)
            if (t != null && t.TrackableType == OVRAnchor.TrackableType.QRCode)
                Destroy(t.gameObject);

        HideAllDebugVisuals();
        _detectedQRs.Clear();
        _qrQualityCache.Clear();
        _qrPosCache.Clear();
        _alignmentDone = false;
    }

    public void SetHologram(GameObject loadedHologram)
    {
        _patientHologram = loadedHologram;
        Debug.Log("[SA] Hologram ricevuto. Pronto per l'allineamento.");
    }

    private void ScanExistingTrackablesOnce()
    {
        MRUKTrackable[] existing = FindObjectsByType<MRUKTrackable>(FindObjectsSortMode.None);
        foreach (var t in existing)
            if (t != null && t.TrackableType == OVRAnchor.TrackableType.QRCode)
                OnTrackableAdded(t);
    }

    private void OnTrackableAdded(MRUKTrackable trackable)
    {
        if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode) return;
        string payload = trackable.MarkerPayloadString;
        if (string.IsNullOrEmpty(payload)) return;

        if (!_detectedQRs.ContainsKey(payload))
        {
            int colorIndex = _detectedQRs.Count % QR_COLORS.Length;
            _detectedQRs.Add(payload, trackable);
            Debug.Log($"[SA] Nuovo QR: '{payload}' (#{_detectedQRs.Count}/4)");

            GameObject visual = CreateDebugVisual(trackable, payload, QR_COLORS[colorIndex]);
            _debugVisuals.Add(payload, visual);
            UpdateAllCounterLabels();
        }
    }

    private void OnTrackableRemoved(MRUKTrackable trackable)
    {
        if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode) return;
        string payload = trackable.MarkerPayloadString;

        if (_detectedQRs.ContainsKey(payload))
        {
            _detectedQRs.Remove(payload);
            _qrQualityCache.Remove(payload);
            _qrPosCache.Remove(payload);

            if (_debugVisuals.TryGetValue(payload, out GameObject visual))
            {
                Destroy(visual);
                _debugVisuals.Remove(payload);
            }

            UpdateAllCounterLabels();
            Debug.Log($"[SA] QR perso: '{payload}'. Rimasti: {_detectedQRs.Count}/4");
        }
    }

    void OnDestroy()
    {
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

        return container;
    }

    private Quaternion ApplyModelYawOffset(Quaternion qrLayoutRotation)
    {
        return qrLayoutRotation * Quaternion.Euler(0f, _modelYawOffsetDegrees, 0f);
    }

    // ── OTTIMIZZATA: zero allocazioni per frame, usa dizionari cachati ──
    private bool TryComputeCenter(out Vector3 center, out Quaternion sheetRotation, bool logDebug)
    {
        center        = Vector3.zero;
        sheetRotation = Quaternion.identity;
        if (_detectedQRs.Count == 0) return false;

        // Aggiorna le cache in-place (no new Dictionary)
        _qrQualityCache.Clear();
        _qrPosCache.Clear();

        foreach (var kv in _detectedQRs)
        {
            float quality = 1f;
            if (kv.Value.PlaneRect.HasValue)
            {
                float estimatedSize = (kv.Value.PlaneRect.Value.width + kv.Value.PlaneRect.Value.height) / 2f;
                float ratio = estimatedSize / _qrSize_m;
                quality = Mathf.Clamp(ratio * ratio, 0.05f, 1f);
            }
            _qrQualityCache[kv.Key] = quality;
            _qrPosCache[kv.Key]     = kv.Value.transform.position;
        }

        // Sostituisce LINQ con loop manuale: zero GC alloc
        string bestKey   = null;
        float  bestQual  = -1f;
        foreach (var kv in _qrQualityCache)
        {
            if (kv.Value > bestQual) { bestQual = kv.Value; bestKey = kv.Key; }
        }
        if (bestKey == null) return false;

        // Sostituisce .Min(p => p.y) con loop manuale
        float robustY = float.MaxValue;
        foreach (var p in _qrPosCache.Values)
            if (p.y < robustY) robustY = p.y;

        Vector3 Flat(Vector3 v) => new Vector3(v.x, robustY, v.z);

        Transform refT      = _detectedQRs[bestKey].transform;
        Vector3 ProjectOrFallback(Vector3 v, Vector3 fallback)
        {
            v = Vector3.ProjectOnPlane(v, Vector3.up);
            return v.sqrMagnitude > 0.0001f ? v.normalized : fallback.normalized;
        }

        Vector3 layoutRight = ProjectOrFallback(-refT.right, Vector3.right);
        Vector3 layoutDown  = ProjectOrFallback(-refT.up,    Vector3.back);
        float   half        = _qrSize_m / 2f;

        bool Good(string p) => _qrQualityCache.TryGetValue(p, out float q) && q >= _qualityThreshold;

        Vector3 AverageDirection(params Vector3[] directions)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (Vector3 dir in directions)
            {
                Vector3 flatDir = Vector3.ProjectOnPlane(dir, Vector3.up);
                if (flatDir.sqrMagnitude <= 0.0001f) continue;
                sum += flatDir.normalized;
                count++;
            }

            return count > 0 && sum.sqrMagnitude > 0.0001f
                ? sum.normalized
                : Vector3.zero;
        }

        Vector3 verticalUp = AverageDirection(
            Good(_payloadTopLeft) && Good(_payloadBottomLeft)
                ? Flat(_qrPosCache[_payloadTopLeft]) - Flat(_qrPosCache[_payloadBottomLeft])
                : Vector3.zero,
            Good(_payloadTopRight) && Good(_payloadBottomRight)
                ? Flat(_qrPosCache[_payloadTopRight]) - Flat(_qrPosCache[_payloadBottomRight])
                : Vector3.zero
        );

        Vector3 horizontalRight = AverageDirection(
            Good(_payloadTopRight) && Good(_payloadTopLeft)
                ? Flat(_qrPosCache[_payloadTopRight]) - Flat(_qrPosCache[_payloadTopLeft])
                : Vector3.zero,
            Good(_payloadBottomRight) && Good(_payloadBottomLeft)
                ? Flat(_qrPosCache[_payloadBottomRight]) - Flat(_qrPosCache[_payloadBottomLeft])
                : Vector3.zero
        );

        if (verticalUp != Vector3.zero)
        {
            layoutDown = -verticalUp;
            layoutRight = horizontalRight != Vector3.zero
                ? horizontalRight
                : Vector3.Cross(Vector3.up, verticalUp).normalized;
        }
        else if (horizontalRight != Vector3.zero)
        {
            layoutRight = horizontalRight;
            Vector3 inferredUp = Vector3.Cross(horizontalRight, Vector3.up).normalized;
            layoutDown = -inferredUp;
        }

        sheetRotation = Quaternion.LookRotation(-layoutDown, Vector3.up);

        if (Good(_payloadTopLeft) && Good(_payloadBottomRight))
        { center = (Flat(_qrPosCache[_payloadTopLeft]) + Flat(_qrPosCache[_payloadBottomRight])) / 2f; return true; }
        if (Good(_payloadTopRight) && Good(_payloadBottomLeft))
        { center = (Flat(_qrPosCache[_payloadTopRight]) + Flat(_qrPosCache[_payloadBottomLeft])) / 2f; return true; }
        if (Good(_payloadTopLeft) && Good(_payloadTopRight))
        { center = (Flat(_qrPosCache[_payloadTopLeft]) + Flat(_qrPosCache[_payloadTopRight])) / 2f + layoutDown * half; return true; }
        if (Good(_payloadBottomLeft) && Good(_payloadBottomRight))
        { center = (Flat(_qrPosCache[_payloadBottomLeft]) + Flat(_qrPosCache[_payloadBottomRight])) / 2f - layoutDown * half; return true; }
        if (Good(_payloadTopLeft) && Good(_payloadBottomLeft))
        { center = (Flat(_qrPosCache[_payloadTopLeft]) + Flat(_qrPosCache[_payloadBottomLeft])) / 2f + layoutRight * half; return true; }
        if (Good(_payloadTopRight) && Good(_payloadBottomRight))
        { center = (Flat(_qrPosCache[_payloadTopRight]) + Flat(_qrPosCache[_payloadBottomRight])) / 2f - layoutRight * half; return true; }

        if (Good(_payloadTopLeft))
        { center = Flat(_qrPosCache[_payloadTopLeft]) + layoutRight * half + layoutDown * half; return true; }
        if (Good(_payloadTopRight))
        { center = Flat(_qrPosCache[_payloadTopRight]) - layoutRight * half + layoutDown * half; return true; }
        if (Good(_payloadBottomLeft))
        { center = Flat(_qrPosCache[_payloadBottomLeft]) + layoutRight * half - layoutDown * half; return true; }
        if (Good(_payloadBottomRight))
        { center = Flat(_qrPosCache[_payloadBottomRight]) - layoutRight * half - layoutDown * half; return true; }

        float totalW = 0f, fx = 0f, fz = 0f;
        foreach (var kv in _qrPosCache)
        {
            float w = _qrQualityCache[kv.Key];
            fx += kv.Value.x * w; fz += kv.Value.z * w; totalW += w;
        }
        center = new Vector3(fx / totalW, robustY, fz / totalW);
        return true;
    }
}
