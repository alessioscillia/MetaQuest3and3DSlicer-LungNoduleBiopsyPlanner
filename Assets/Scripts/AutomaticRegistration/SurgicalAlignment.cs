using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Meta.XR.MRUtilityKit;

public class SurgicalAlignment : MonoBehaviour
{
    // --- SINGLETON ---
    public static SurgicalAlignment Instance { get; private set; }

    public const string ScenePermission = OVRPermissionsRequester.ScenePermission;
    public static bool IsSupported => MRUK.Instance != null;

    public static bool HasPermissions
#if UNITY_EDITOR
        => true;
#else
        => UnityEngine.Android.Permission.HasUserAuthorizedPermission(ScenePermission);
#endif

    public static bool TrackingEnabled
    {
        get => Instance && Instance._mrukInstance && Instance._mrukInstance.SceneSettings.TrackerConfiguration.QRCodeTrackingEnabled;
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

    // --- LA VERA NOVITÀ ---
    // Usiamo la STRINGA (il nome del QR) come chiave, e salviamo le POSIZIONI pure, ignorando gli oggetti di Meta.
    private Dictionary<string, Vector3> detectedQRsPos = new Dictionary<string, Vector3>();
    private Dictionary<string, GameObject> debugVisuals = new Dictionary<string, GameObject>();

    private static readonly Color[] QR_COLORS = { Color.green, Color.cyan, Color.yellow, Color.magenta };

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void OnValidate()
    {
        if (!_mrukInstance && FindAnyObjectByType<MRUK>() is { } mruk && mruk.gameObject.scene == gameObject.scene)
            _mrukInstance = mruk;
    }

    void Start()
    {
        if (!_mrukInstance)       { Debug.LogError("ERROR: MRUK not found."); return; }
        if (!IsSupported)         { Debug.LogError("ERROR: QR Code tracking not supported."); return; }
        if (!HasPermissions)      { Debug.LogWarning("Scene permission not granted."); return; }
        if (!TrackingEnabled)     { Debug.LogWarning("QR Code tracking not enabled."); return; }

        ScanExistingTrackables();
        _mrukInstance.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
        _mrukInstance.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);

        Debug.Log("SurgicalAlignment: QR Code tracking initialized.");
        StartCoroutine(ScanExistingTrackablesDelayed());
    }

    private IEnumerator ScanExistingTrackablesDelayed()
    {
        yield return new WaitForSeconds(0.5f);
        MRUKTrackable[] existing = FindObjectsByType<MRUKTrackable>(FindObjectsSortMode.None);
        foreach (var trackable in existing)
        {
            if (trackable.TrackableType == OVRAnchor.TrackableType.QRCode)
                OnTrackableAdded(trackable);
        }
    }

    private void ScanExistingTrackables()
    {
        MRUKTrackable[] existing = FindObjectsByType<MRUKTrackable>(FindObjectsSortMode.None);
        foreach (var trackable in existing)
        {
            if (trackable.TrackableType == OVRAnchor.TrackableType.QRCode)
                OnTrackableAdded(trackable);
        }
    }

    void Update()
    {
        if (Camera.main == null) return;

        foreach (var visual in debugVisuals.Values)
        {
            if (visual == null) continue;

            Vector3 screenPoint = Camera.main.WorldToViewportPoint(visual.transform.position);
            bool isVisible = screenPoint.z > 0
                          && screenPoint.x >= 0 && screenPoint.x <= 1
                          && screenPoint.y >= 0 && screenPoint.y <= 1;

            if (visual.transform.childCount > 0)
            {
                GameObject graphicsRoot = visual.transform.GetChild(0).gameObject;
                if (graphicsRoot.activeSelf != isVisible)
                    graphicsRoot.SetActive(isVisible);
            }
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

    public void SetHologram(GameObject loadedHologram)
    {
        _patientHologram = loadedHologram;
        VerifyAndAlign();
    }

    // ---------------------------------------------------------------
    // TRACKABLE ADDED: Lavoriamo con le Stringhe e le Posizioni pure!
    // ---------------------------------------------------------------
    private void OnTrackableAdded(MRUKTrackable trackable)
    {
        if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode) return;

        string payload = trackable.MarkerPayloadString;
        if (string.IsNullOrEmpty(payload)) return;

        Vector3 currentPos = trackable.transform.position;
        Quaternion currentRot = trackable.transform.rotation;

        // Se l'abbiamo già visto, aggiorniamo solo la sua posizione nello spazio
        if (detectedQRsPos.ContainsKey(payload))
        {
            detectedQRsPos[payload] = currentPos;
            if (debugVisuals.ContainsKey(payload))
            {
                debugVisuals[payload].transform.position = currentPos;
                debugVisuals[payload].transform.rotation = currentRot;
            }
            Debug.Log($"[QR] Aggiornato Posizione: '{payload}'. Totale: {detectedQRsPos.Count}/4");
            VerifyAndAlign();
            return;
        }

        // È un QR nuovo (o almeno una stringa nuova)
        detectedQRsPos.Add(payload, currentPos);
        int colorIndex = (detectedQRsPos.Count - 1) % QR_COLORS.Length;
        Debug.Log($"[QR] Nuovo: '{payload}' (#{detectedQRsPos.Count}/4)");

        // Creiamo la grafica SGANCIATA dall'oggetto di Meta
        GameObject visual = CreateIndependentVisual(trackable, payload, currentPos, currentRot, QR_COLORS[colorIndex]);
        debugVisuals.Add(payload, visual);

        UpdateAllCounterLabels();
        VerifyAndAlign();
    }

    // ---------------------------------------------------------------
    // TRACKABLE REMOVED
    // ---------------------------------------------------------------
    private void OnTrackableRemoved(MRUKTrackable trackable)
    {
        if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode) return;
        Debug.Log($"[QR] Anchor di Meta riciclato o perso di vista. La nostra posizione salvata è al sicuro.");
    }

    public void ResetAlignment()
    {
        _alignmentDone = false;
        foreach (var visual in debugVisuals.Values)
            if (visual != null) Destroy(visual);

        detectedQRsPos.Clear();
        debugVisuals.Clear();
        Debug.Log("[QR] Reset completato. Puoi scansionare di nuovo.");
    }

    private void VerifyAndAlign()
    {
        if (_alignmentDone || detectedQRsPos.Count < 4) return;

        if (_patientHologram == null)
        {
            Debug.Log("[QR] 4 QR trovati! In attesa del modello da AnatomyImporter...");
            return;
        }

        PerformAlignment();
    }

    private void PerformAlignment()
    {
        Vector3 sumPositions = Vector3.zero;
        foreach (var pos in detectedQRsPos.Values)
        {
            sumPositions += pos;
        }

        Vector3 isocenter = sumPositions / detectedQRsPos.Count;
        _patientHologram.transform.position = isocenter;
        _alignmentDone = true;

        foreach (var visual in debugVisuals.Values)
            SetVisualColor(visual, Color.white);

        Debug.Log($"[QR] ALLINEAMENTO COMPLETATO! Isocentro: {isocenter}");
    }

    private void UpdateAllCounterLabels()
    {
        foreach (var pair in debugVisuals)
        {
            if (pair.Value == null) continue;
            Transform graphicsRoot = pair.Value.transform.Find("Graphics");
            if (graphicsRoot == null) continue;
            Transform counterTransform = graphicsRoot.Find("CounterLabel");
            if (counterTransform == null) continue;

            TextMesh tm = counterTransform.GetComponent<TextMesh>();
            if (tm != null) tm.text = $"{detectedQRsPos.Count}/4";
        }
    }

    private void SetVisualColor(GameObject visual, Color color)
    {
        if (visual == null) return;
        LineRenderer lr = visual.GetComponentInChildren<LineRenderer>();
        if (lr != null) { lr.startColor = color; lr.endColor = color; }
    }

    // ===============================================================
    // GENERAZIONE GRAFICA (Sganciata e ingrandita)
    // ===============================================================
    private GameObject CreateIndependentVisual(MRUKTrackable trackable, string payload, Vector3 pos, Quaternion rot, Color color)
    {
        // NON lo imparentiamo al trackable! Lo posizioniamo liberamente nel mondo.
        GameObject container = new GameObject($"Debug_QR_{payload}");
        container.transform.position = pos;
        container.transform.rotation = rot;

        GameObject graphicsRoot = new GameObject("Graphics");
        graphicsRoot.transform.SetParent(container.transform, false);
        graphicsRoot.transform.localRotation = Quaternion.Euler(0, 180, 0); 

        // Ingrandiamo leggermente del 10% (1.1f) per coprire la "Quiet Zone" bianca
        float width  = (trackable.PlaneRect.HasValue ? trackable.PlaneRect.Value.width  : 0.15f) * 1.1f;
        float height = (trackable.PlaneRect.HasValue ? trackable.PlaneRect.Value.height : 0.15f) * 1.1f;
        float w = width / 2f;
        float h = height / 2f;

        GameObject outlineObj = new GameObject("Outline");
        outlineObj.transform.SetParent(graphicsRoot.transform, false);

        LineRenderer lr = outlineObj.AddComponent<LineRenderer>();
        lr.useWorldSpace  = false;
        lr.loop           = true;
        lr.positionCount  = 4;
        lr.startWidth     = 0.006f;
        lr.endWidth       = 0.006f;
        lr.material       = new Material(Shader.Find("Sprites/Default"));
        lr.startColor     = color;
        lr.endColor       = color;
        lr.SetPosition(0, new Vector3(-w, -h, 0));
        lr.SetPosition(1, new Vector3( w, -h, 0));
        lr.SetPosition(2, new Vector3( w,  h, 0));
        lr.SetPosition(3, new Vector3(-w,  h, 0));

        GameObject textObj = new GameObject("TextInfo");
        textObj.transform.SetParent(graphicsRoot.transform, false);
        textObj.transform.localPosition = new Vector3(0, h + 0.04f, 0);
        textObj.transform.localScale    = new Vector3(0.005f, 0.005f, 0.005f);

        TextMesh tm = textObj.AddComponent<TextMesh>();
        tm.text      = payload;
        tm.fontSize  = 100;
        tm.anchor    = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color     = color; 

        GameObject counterObj = new GameObject("CounterLabel");
        counterObj.transform.SetParent(graphicsRoot.transform, false);
        counterObj.transform.localPosition = new Vector3(0, -(h + 0.04f), 0);
        counterObj.transform.localScale    = new Vector3(0.004f, 0.004f, 0.004f);

        TextMesh counterTm = counterObj.AddComponent<TextMesh>();
        counterTm.text      = $"{detectedQRsPos.Count}/4";
        counterTm.fontSize  = 100;
        counterTm.anchor    = TextAnchor.MiddleCenter;
        counterTm.alignment = TextAlignment.Center;
        counterTm.color     = Color.white;

        return container;
    }
}