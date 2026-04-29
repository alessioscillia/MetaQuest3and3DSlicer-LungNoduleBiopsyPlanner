using System;
using System.Collections.Generic;
using UnityEngine;
using Meta.XR.MRUtilityKit;
using System.Linq; 

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
    
    // Il tracking è SEMPRE ATTIVO di base finché non lo spegni tu
    private bool _isTracking = true;
    private bool _alignmentDone = false; 

    private readonly Dictionary<string, MRUKTrackable> _detectedQRs = new();
    private readonly Dictionary<string, GameObject>    _debugVisuals = new();

    private static readonly Color[] QR_COLORS =
        { Color.green, Color.cyan, Color.yellow, Color.magenta };

    [Header("Debug Visuals")]
    [SerializeField] private bool _showCenterDebug = true; // Sferetta magenta al centro dei 4 QR
    private GameObject _alignmentCenterVisual; // Riferimento al marker centrale

    // ── CONFIGURAZIONE LAYOUT ── (assegnabile dall'Inspector)
    [Header("QR Layout - Payload e Misure Fisiche")]
    [SerializeField] private string _payloadTopLeft     = "Alto Sinistra";
    [SerializeField] private string _payloadTopRight    = "Alto Destra";
    [SerializeField] private string _payloadBottomLeft  = "Basso Sinistra";
    [SerializeField] private string _payloadBottomRight = "Basso Destra";
    [SerializeField] private float  _qrSize_m           = 0.073f;  // lato QR in metri
    [SerializeField] private float  _qualityThreshold   = 0.80f;   // soglia per considerare un QR affidabile

    [Header("Continuous Tracking Settings")]
    [SerializeField] private float _positionSmoothSpeed = 15f;
    [SerializeField] private float _rotationSmoothSpeed = 10f; 

    private Quaternion _initialSheetRot   = Quaternion.identity; 
    private Vector3    _targetPosition;                          
    private Quaternion _targetRotation;                          

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
        if (!HasPermissions)  { Debug.LogWarning("[SA] Permessi non concessi.");  return; }
        
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

        if (_detectedQRs.Count >= 4)
        {
            if (TryComputeCenter(out Vector3 newCenter, out Quaternion newSheetRot, logDebug: false))
            {
                if (!_alignmentDone)
                {
                    // --- PRIMO AGGANCIO ---
                    _initialSheetRot = newSheetRot;
                    _targetPosition = newCenter;
                    _targetRotation = Quaternion.identity; 
                    
                    _patientHologram.transform.position = _targetPosition;
                    _patientHologram.transform.rotation = _targetRotation;
                    _patientHologram.transform.localScale = new Vector3(0.001f, 0.001f, 0.001f);
                    
                    _alignmentDone = true;
                    Debug.Log("[SA] Modello Agganciato! (Inseguimento Real-Time Attivo).");
                }
                else
                {
                    // --- AGGIORNAMENTO CONTINUO ---
                    _targetPosition = newCenter;
                    _targetRotation = newSheetRot * Quaternion.Inverse(_initialSheetRot); 
                }

                _patientHologram.transform.position = Vector3.Lerp(
                    _patientHologram.transform.position,
                    _targetPosition,
                    Time.deltaTime * _positionSmoothSpeed
                );

                _patientHologram.transform.rotation = Quaternion.Slerp(
                    _patientHologram.transform.rotation,
                    _targetRotation,
                    Time.deltaTime * _rotationSmoothSpeed
                );

                _patientHologram.transform.localScale = new Vector3(0.001f, 0.001f, 0.001f);

                if (_showCenterDebug) CreateCenterDebugVisual(newCenter);
            }
        }
    }

    // --- Play/Pause per il tracking real-time ---
    public bool ToggleTracking()
    {
        _isTracking = !_isTracking;
        TrackingEnabled = _isTracking; 

        if (_isTracking)
        {
            Debug.Log("[SA] Tracking Real-Time: RIATTIVATO. Pulisco la memoria per i nuovi dati...");
            ClearQRMemory();

            if (_mrukInstance != null)
            {
                _mrukInstance.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
                _mrukInstance.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);
            }
        }
        else
        {
            Debug.Log("[SA] Tracking Real-Time: IN PAUSA. Nascondo i traccianti.");
            if (_mrukInstance != null)
            {
                _mrukInstance.SceneSettings.TrackableAdded.RemoveListener(OnTrackableAdded);
                _mrukInstance.SceneSettings.TrackableRemoved.RemoveListener(OnTrackableRemoved);
            }
            
            // Nasconde i quadratini colorati e la pallina centrale!
            HideAllDebugVisuals();
        }

        return _isTracking;
    }

    // Distrugge gli elementi visivi (chiamato sia in pausa che durante la pulizia)
    private void HideAllDebugVisuals()
    {
        foreach (var v in _debugVisuals.Values) 
        {
            if (v != null) Destroy(v);
        }
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
        {
            if (t != null && t.TrackableType == OVRAnchor.TrackableType.QRCode)
            {
                Destroy(t.gameObject);
            }
        }

        HideAllDebugVisuals();
        _detectedQRs.Clear();
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
        {
            if (t != null && t.TrackableType == OVRAnchor.TrackableType.QRCode)
            {
                OnTrackableAdded(t);
            }
        }
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
            
            if (_debugVisuals.TryGetValue(payload, out GameObject visual))
            {
                Destroy(visual);
                _debugVisuals.Remove(payload);
            }
            
            UpdateAllCounterLabels();
            Debug.Log($"[SA] QR perso temporaneamente: '{payload}'. Salvati: {_detectedQRs.Count}/4");
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

    private void CreateCenterDebugVisual(Vector3 centerPos)
    {
        if (_alignmentCenterVisual != null) Destroy(_alignmentCenterVisual);

        _alignmentCenterVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(_alignmentCenterVisual.GetComponent<Collider>()); 
        
        _alignmentCenterVisual.transform.position = centerPos;
        _alignmentCenterVisual.transform.localScale = Vector3.one * 0.03f; 
        _alignmentCenterVisual.name = "Alignment_Center_Pivot_Marker";

        Material mat = new Material(Shader.Find("Standard"));
        mat.color = Color.magenta;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", Color.magenta * 0.4f);
        _alignmentCenterVisual.GetComponent<Renderer>().material = mat;

        _alignmentCenterVisual.transform.SetParent(_patientHologram.transform, true);
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

        // GameObject outlineObj = new GameObject("Outline");
        // outlineObj.transform.SetParent(graphicsRoot.transform, false); 
        // LineRenderer lr = outlineObj.AddComponent<LineRenderer>(); 
        // lr.useWorldSpace = false;
        // lr.loop          = true;
        // lr.positionCount = 4;
        // lr.startWidth    = 0.006f;
        // lr.endWidth      = 0.006f;
        // lr.material      = new Material(Shader.Find("Sprites/Default"));
        // lr.startColor    = color;
        // lr.endColor      = color;
        // lr.SetPosition(0, new Vector3(-w, -h, 0));
        // lr.SetPosition(1, new Vector3( w, -h, 0));
        // lr.SetPosition(2, new Vector3( w,  h, 0));
        // lr.SetPosition(3, new Vector3(-w,  h, 0));

        // Rimosso il blocco del Testo per nascondere i payload (es. "Alto Sinistra")

        // GameObject counterObj = new GameObject("CounterLabel");
        // counterObj.transform.SetParent(graphicsRoot.transform, false);
        // counterObj.transform.localPosition = new Vector3(0, -(h + 0.02f), 0);
        // counterObj.transform.localScale    = Vector3.one * 0.004f;
        // TextMesh ctm = counterObj.AddComponent<TextMesh>();
        // ctm.text      = $"{_detectedQRs.Count}/4";
        // ctm.fontSize  = 20;
        // ctm.anchor    = TextAnchor.MiddleCenter;
        // ctm.alignment = TextAlignment.Center;
        // ctm.color     = Color.white;

        return container;
    }
    
    private bool TryComputeCenter(out Vector3 center, out Quaternion sheetRotation, bool logDebug)
    {
        center        = Vector3.zero;
        sheetRotation = Quaternion.identity;
        if (_detectedQRs.Count == 0) return false;

        var qrQuality = new Dictionary<string, float>();
        var qrPos     = new Dictionary<string, Vector3>();

        foreach (var kv in _detectedQRs)
        {
            float quality = 1f;
            if (kv.Value.PlaneRect.HasValue)
            {
                float estimatedSize = (kv.Value.PlaneRect.Value.width + kv.Value.PlaneRect.Value.height) / 2f;
                float ratio = estimatedSize / _qrSize_m;
                quality = Mathf.Clamp(ratio * ratio, 0.05f, 1f);
            }
            qrQuality[kv.Key] = quality;
            qrPos[kv.Key]     = kv.Value.transform.position;
        }

        bool Good(string p) => qrQuality.ContainsKey(p) && qrQuality[p] >= _qualityThreshold;

        var bestKV = _detectedQRs
            .OrderByDescending(kv => qrQuality.ContainsKey(kv.Key) ? qrQuality[kv.Key] : 0f)
            .First();
        sheetRotation = bestKV.Value.transform.rotation;

        float robustY = qrPos.Values.Min(p => p.y);
        Vector3 Flat(Vector3 v) => new Vector3(v.x, robustY, v.z);

        Transform refT      = bestKV.Value.transform;
        Vector3 layoutRight = Vector3.ProjectOnPlane(-refT.right, Vector3.up).normalized;
        Vector3 layoutDown  = Vector3.ProjectOnPlane(-refT.up,    Vector3.up).normalized;
        float   half        = _qrSize_m / 2f;

        if (Good(_payloadTopLeft) && Good(_payloadBottomRight))
        {
            center = (Flat(qrPos[_payloadTopLeft]) + Flat(qrPos[_payloadBottomRight])) / 2f;
            return true;
        }
        if (Good(_payloadTopRight) && Good(_payloadBottomLeft))
        {
            center = (Flat(qrPos[_payloadTopRight]) + Flat(qrPos[_payloadBottomLeft])) / 2f;
            return true;
        }

        if (Good(_payloadTopLeft) && Good(_payloadTopRight))
        {
            center = (Flat(qrPos[_payloadTopLeft]) + Flat(qrPos[_payloadTopRight])) / 2f + layoutDown * half;
            return true;
        }
        if (Good(_payloadBottomLeft) && Good(_payloadBottomRight))
        {
            center = (Flat(qrPos[_payloadBottomLeft]) + Flat(qrPos[_payloadBottomRight])) / 2f - layoutDown * half;
            return true;
        }
        if (Good(_payloadTopLeft) && Good(_payloadBottomLeft))
        {
            center = (Flat(qrPos[_payloadTopLeft]) + Flat(qrPos[_payloadBottomLeft])) / 2f + layoutRight * half;
            return true;
        }
        if (Good(_payloadTopRight) && Good(_payloadBottomRight))
        {
            center = (Flat(qrPos[_payloadTopRight]) + Flat(qrPos[_payloadBottomRight])) / 2f - layoutRight * half;
            return true;
        }

        if (Good(_payloadTopLeft))
        {
            center = Flat(qrPos[_payloadTopLeft]) + layoutRight * half + layoutDown * half;
            return true;
        }
        if (Good(_payloadTopRight))
        {
            center = Flat(qrPos[_payloadTopRight]) - layoutRight * half + layoutDown * half;
            return true;
        }
        if (Good(_payloadBottomLeft))
        {
            center = Flat(qrPos[_payloadBottomLeft]) + layoutRight * half - layoutDown * half;
            return true;
        }
        if (Good(_payloadBottomRight))
        {
            center = Flat(qrPos[_payloadBottomRight]) - layoutRight * half - layoutDown * half;
            return true;
        }

        float totalW = 0f, fx = 0f, fz = 0f;
        foreach (var kv in qrPos)
        {
            float w = qrQuality[kv.Key];
            fx += kv.Value.x * w; fz += kv.Value.z * w; totalW += w;
        }
        center = new Vector3(fx / totalW, robustY, fz / totalW);
        return true;
    }
}