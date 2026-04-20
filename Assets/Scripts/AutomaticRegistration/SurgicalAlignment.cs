using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Meta.XR.MRUtilityKit;

public class SurgicalAlignment : MonoBehaviour
{
    public static SurgicalAlignment Instance { get; private set; } // Definisce un singleton per l'accesso globale

    public static bool IsSupported => MRUK.Instance != null; // Verifica se MRUK è presente, indicatore di supporto al tracking QR

    public static bool HasPermissions // In editor assumiamo sempre i permessi, su Android controlliamo effettivamente
#if UNITY_EDITOR
        => true;
#else
        => UnityEngine.Android.Permission.HasUserAuthorizedPermission(ScenePermission);
#endif

    public static bool TrackingEnabled // Controlla se il tracking QR è abilitato nelle impostazioni di MRUK, e permette di abilitarlo/disabilitarlo
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

    [SerializeField] private MRUK _mrukInstance; // Riferimento a MRUK, assegnabile in inspector o trovato automaticamente in Start()

    private GameObject _patientHologram; 
    private bool _alignmentDone = false; 

    private readonly Dictionary<string, MRUKTrackable> _detectedQRs = new(); // Dizionario dei QR rilevati, chiave: payload, valore: trackable. Non rimuoviamo i QR persi, manteniamo la posizione finché non troviamo 4 QR validi.
    private readonly Dictionary<string, GameObject>    _debugVisuals = new(); // Visual di debug associati a ciascun QR, per mostrare posizione e payload. Riferiti per payload, non rimossi quando un QR è perso, così da mantenere la visual finché non troviamo 4 QR validi.
    private readonly HashSet<MRUKTrackable> _knownTrackables = new(); // Cache dei trackable QR già visti, evita di contare lo stesso oggetto due volte e permette di fare recovery senza FindObjectsByType.

    private static readonly Color[] QR_COLORS =
        { Color.green, Color.cyan, Color.yellow, Color.magenta };

    // Coroutine di recovery: parte solo se mancano QR, si ferma quando li ha tutti
    private Coroutine _recoveryCoroutine;

    // ---------------------------------------------------------------
    // Si garantisce che ci sia una sola istanza di SurgicalAlignment, accessibile globalmente tramite Instance. Se ne esiste già una, quella nuova si distrugge da sola (Singleton).
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

// Se MRUK non è stato assegnato in inspector, prova a trovarlo automaticamente nella stessa scena. Questo permette di evitare l'assegnazione manuale, ma funziona solo se MRUK e SurgicalAlignment sono nella stessa scena.
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

    // Ferma la coroutine di recovery se è in esecuzione. Chiamata quando troviamo un nuovo QR o quando raggiungiamo 4 QR, così da non sprecare risorse.
    private void StopRecovery()
    {
        if (_recoveryCoroutine == null) return;
        StopCoroutine(_recoveryCoroutine);
        _recoveryCoroutine = null;
    }

    // Coroutine di recovery: nel caso in cui si perdono dei QR Code precedentemente rilevati, viene richiamata questa cororutine che riprocessa SOLO i trackable già noti.
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
    // Chiamato solo dagli eventi MRUK o dalla coroutine di recovery, quando troviamo un nuovo QR o quando un QR già noto rientra nel FOV. Aggiorna il dizionario dei QR rilevati, crea/aggiorna il visual di debug, aggiorna i contatori, e se ora abbiamo 4 QR tenta l'allineamento.
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

        // Riabilita il tracking per una nuova sessione
        TrackingEnabled = true;

        // Ri-aggancia i listener (li avevamo rimossi dopo l'allineamento)
        if (_mrukInstance != null)
        {
            _mrukInstance.SceneSettings.TrackableAdded.RemoveListener(OnTrackableAdded);   // evita duplicati
            _mrukInstance.SceneSettings.TrackableRemoved.RemoveListener(OnTrackableRemoved);
            _mrukInstance.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
            _mrukInstance.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);
        }

        foreach (var v in _debugVisuals.Values)
            if (v != null) Destroy(v);

        _detectedQRs.Clear();
        _debugVisuals.Clear();
        ScanExistingTrackablesOnce();
        Debug.Log("[SA] Reset completato, QR tracking riabilitato.");
    }

    // ---------------------------------------------------------------
    // ALLINEAMENTO: permesso solo se abbiamo 4 QR rilevati e un modello da posizionare.
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

    // Allinea l'isocentro del paziente al centro dei QR rilevati. Chiamato quando abbiamo 4 QR e un modello da posizionare.
    private void PerformAlignment()
    {
        Vector3 sum = Vector3.zero;
        foreach (var qr in _detectedQRs.Values)
            sum += qr.transform.position;

        _patientHologram.transform.position = sum / _detectedQRs.Count; // posiziona l'isocentro al centro dei QR rilevati
        _alignmentDone = true;
        StopRecovery();

        // Spegne completamente il pipelineQR di MRUK - libera la CPU/GPU del visore
        TrackingEnabled = false;
        Debug.Log("[SA] QR tracking disabilitato dopo allineamento.");

        // Rimuovi anche i listener: non servono più
        if (_mrukInstance != null)
        {
            _mrukInstance.SceneSettings.TrackableAdded.RemoveListener(OnTrackableAdded);
            _mrukInstance.SceneSettings.TrackableRemoved.RemoveListener(OnTrackableRemoved);
        }


        // Distruggi i visual: (quadrati colorati intorno ai QR Code) non servono più, liberano CPU e draw call
        foreach (var v in _debugVisuals.Values)
            if (v != null) Destroy(v);
        _debugVisuals.Clear();

        Debug.Log($"[SA] ALLINEAMENTO COMPLETATO! Isocentro: {_patientHologram.transform.position}");
    }

    // ---------------------------------------------------------------
    // Aggiorna i contatori su tutti i visual di debug, mostrando quanti QR sono attualmente rilevati. Chiamato ogni volta che cambia il numero di QR rilevati.
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
        GameObject container = new GameObject($"Debug_QR_{payload}"); // contiene tutti i visual di questo QR, posizionato direttamente sul trackable
        container.transform.SetParent(trackable.transform, false); // parenta al trackable, così segue posizione e rotazione

        GameObject graphicsRoot = new GameObject("Graphics"); 
        graphicsRoot.transform.SetParent(container.transform, false); // child di container, così eredita la posizione del trackable ma può avere una rotazione fissa (per essere leggibile)
        graphicsRoot.transform.localRotation = Quaternion.Euler(0, 180, 0); // ruota di 180° per essere leggibile frontalmente (dipende da come MRUK posiziona i trackable, potrebbe essere necessario adattare)

        // Dimensioni del contorno: se MRUK fornisce le dimensioni fisiche del QR, usale (con un piccolo margine), altrimenti usa un default ragionevole
        float width  = (trackable.PlaneRect.HasValue ? trackable.PlaneRect.Value.width  : 0.15f) * 1.1f; 
        float height = (trackable.PlaneRect.HasValue ? trackable.PlaneRect.Value.height : 0.15f) * 1.1f;
        
        if (trackable.PlaneRect.HasValue)
        {
            Debug.Log($"[SA] QR '{payload}': Usate dimensioni REALI ({trackable.PlaneRect.Value.width:F2} x {trackable.PlaneRect.Value.height:F2} m)");
        }
        else
        {
            Debug.Log($"[SA] QR '{payload}': Dimensioni fisiche rimosse/non rilevate! Forzato DEFAULT 15 cm");
        }

        float w = width  / 2f;
        float h = height / 2f;

        // Contorno
        GameObject outlineObj = new GameObject("Outline");
        outlineObj.transform.SetParent(graphicsRoot.transform, false); // child di graphicsRoot, così eredita la posizione e la rotazione del container
        LineRenderer lr = outlineObj.AddComponent<LineRenderer>(); //
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
        textObj.transform.SetParent(graphicsRoot.transform, false); // child di graphicsRoot, così eredita la posizione e la rotazione del container
        textObj.transform.localPosition = new Vector3(0, h + 0.04f, 0);
        textObj.transform.localScale    = Vector3.one * 0.005f;
        TextMesh tm = textObj.AddComponent<TextMesh>();
        tm.text      = payload;
        tm.fontSize  = 20;
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
        ctm.fontSize  = 20;
        ctm.anchor    = TextAnchor.MiddleCenter;
        ctm.alignment = TextAlignment.Center;
        ctm.color     = Color.white;

        return container;
    }
}