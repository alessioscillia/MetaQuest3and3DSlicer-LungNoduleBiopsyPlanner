using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Meta.XR.MRUtilityKit;

public class SurgicalAlignment : MonoBehaviour
{
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

    // Cache dei trackable noti a MRUK: aggiornata SOLO dagli eventi, mai da Find
    private readonly HashSet<MRUKTrackable> _knownTrackables = new();

    private static readonly Color[] QR_COLORS =
        { Color.green, Color.cyan, Color.yellow, Color.magenta };

    // Coroutine di recovery: parte solo se mancano QR, si ferma quando li ha tutti
    private Coroutine _recoveryCoroutine;

    // ---------------------------------------------------------------
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

        // Aggancia eventi MRUK — unica fonte di verità
        _mrukInstance.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
        _mrukInstance.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);

        // Scansione iniziale UNA SOLA VOLTA (sincrona, non in loop)
        // Necessaria solo per i trackable già presenti prima di Start()
        ScanExistingTrackablesOnce();

        Debug.Log("[SA] QR Code tracking initialized.");
    }

    // Eseguita una sola volta all'avvio — non in loop, non in Update
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

        // Se dopo la scansione iniziale mancano ancora QR, avvia il recovery
        if (!_alignmentDone && _detectedQRs.Count < 4)
            StartRecovery();
    }

    // ---------------------------------------------------------------
    // RECOVERY: usa i trackable già in cache (_knownTrackables),
    // NON chiama FindObjectsByType. Intervallo lungo (2s) perché
    // serve solo per QR che escono/rientrano nel FOV.
    // ---------------------------------------------------------------
    private void StartRecovery()
    {
        if (_recoveryCoroutine != null) return; // già in esecuzione
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
        // Aspetta un po' prima del primo tentativo (MRUK potrebbe essere ancora in init)
        yield return new WaitForSeconds(1.0f);

        while (!_alignmentDone && _detectedQRs.Count < 4)
        {
            // Riprocessa SOLO i trackable già noti — nessun Find, nessun overhead
            foreach (var t in _knownTrackables)
            {
                if (t != null && t.TrackableType == OVRAnchor.TrackableType.QRCode)
                    RegisterOrUpdateTrackable(t);
            }

            // Intervallo lungo: il recovery non è urgente, l'utente vedrà i QR
            yield return new WaitForSeconds(2.0f);
        }

        _recoveryCoroutine = null;
        Debug.Log("[SA] Recovery fermato.");
    }

    // ---------------------------------------------------------------
    // EVENTI MRUK — chiamati automaticamente, zero overhead
    // ---------------------------------------------------------------
    private void OnTrackableAdded(MRUKTrackable trackable)
    {
        if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode) return;

        _knownTrackables.Add(trackable); // aggiorna la cache
        RegisterOrUpdateTrackable(trackable);
    }

    private void OnTrackableRemoved(MRUKTrackable trackable)
    {
        if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode) return;

        // Non rimuovere dal dizionario principale — la posizione è ancora valida
        // Avvia recovery nel caso non avessimo ancora 4 QR
        if (!_alignmentDone && _detectedQRs.Count < 4)
            StartRecovery();

        Debug.Log($"[SA] QR perso temporaneamente: '{trackable.MarkerPayloadString}'. " +
                  $"Salvati: {_detectedQRs.Count}/4");
    }

    // ---------------------------------------------------------------
    // REGISTRAZIONE — nessun Find, nessun loop pesante
    // ---------------------------------------------------------------
    private void RegisterOrUpdateTrackable(MRUKTrackable trackable)
    {
        if (_alignmentDone) return;

        string payload = trackable.MarkerPayloadString;
        if (string.IsNullOrEmpty(payload)) return;

        if (_detectedQRs.ContainsKey(payload))
        {
            // Già noto: aggiorna solo il riferimento
            _detectedQRs[payload] = trackable;
            return;
        }

        // QR nuovo
        int colorIndex = _detectedQRs.Count % QR_COLORS.Length;
        _detectedQRs.Add(payload, trackable);
        Debug.Log($"[SA] Nuovo QR: '{payload}' (#{_detectedQRs.Count}/4)");

        GameObject visual = CreateDebugVisual(trackable, payload, QR_COLORS[colorIndex]);
        _debugVisuals.Add(payload, visual);

        UpdateAllCounterLabels();
        TryAlign();

        // Abbiamo trovato un nuovo QR: se ora siamo a 4, il recovery si fermerà
        // da solo al prossimo ciclo. Se no, assicuriamoci che giri.
        if (_detectedQRs.Count < 4)
            StartRecovery();
        else
            StopRecovery();
    }

    // ---------------------------------------------------------------
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

    // ---------------------------------------------------------------
    // API PUBBLICA
    // ---------------------------------------------------------------
    public void SetHologram(GameObject loadedHologram)
    {
        _patientHologram = loadedHologram;
        Debug.Log("[SA] Hologram ricevuto.");
        TryAlign();
    }

    public void ResetAlignment()
    {
        StopRecovery();
        _alignmentDone = false;

        foreach (var v in _debugVisuals.Values)
            if (v != null) Destroy(v);

        _detectedQRs.Clear();
        _debugVisuals.Clear();
        // NON svuotiamo _knownTrackables: li riprocessiamo subito
        ScanExistingTrackablesOnce();
        Debug.Log("[SA] Reset completato.");
    }

    // ---------------------------------------------------------------
    // ALLINEAMENTO
    // ---------------------------------------------------------------
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

    private void PerformAlignment()
    {
        Vector3 sum = Vector3.zero;
        foreach (var qr in _detectedQRs.Values)
            sum += qr.transform.position;

        _patientHologram.transform.position = sum / _detectedQRs.Count;
        _alignmentDone = true;
        StopRecovery();

        // Distruggi i visual: non servono più, liberano CPU e draw call
        foreach (var v in _debugVisuals.Values)
            if (v != null) Destroy(v);
        _debugVisuals.Clear();

        Debug.Log($"[SA] ALLINEAMENTO COMPLETATO! Isocentro: {_patientHologram.transform.position}");
    }

    // ---------------------------------------------------------------
    // HELPERS
    // ---------------------------------------------------------------
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

    // ---------------------------------------------------------------
    // VISUAL DI DEBUG
    // ---------------------------------------------------------------
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

        // Contorno
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

        // Label payload
        GameObject textObj = new GameObject("TextInfo");
        textObj.transform.SetParent(graphicsRoot.transform, false);
        textObj.transform.localPosition = new Vector3(0, h + 0.04f, 0);
        textObj.transform.localScale    = Vector3.one * 0.005f;
        TextMesh tm = textObj.AddComponent<TextMesh>();
        tm.text      = payload;
        tm.fontSize  = 100;
        tm.anchor    = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color     = color;

        // Contatore
        GameObject counterObj = new GameObject("CounterLabel");
        counterObj.transform.SetParent(graphicsRoot.transform, false);
        counterObj.transform.localPosition = new Vector3(0, -(h + 0.04f), 0);
        counterObj.transform.localScale    = Vector3.one * 0.004f;
        TextMesh ctm = counterObj.AddComponent<TextMesh>();
        ctm.text      = $"{_detectedQRs.Count}/4";
        ctm.fontSize  = 100;
        ctm.anchor    = TextAnchor.MiddleCenter;
        ctm.alignment = TextAlignment.Center;
        ctm.color     = Color.white;

        return container;
    }
}