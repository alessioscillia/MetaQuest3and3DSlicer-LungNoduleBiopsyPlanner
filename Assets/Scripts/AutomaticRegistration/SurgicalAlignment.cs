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

    [Header("Debug Nodule")]
    [SerializeField] private bool _showNoduleDebug = true; // Attiva/Disattiva la sferetta rossa

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
    // Scansiona l'intera scena per trovare i trackable QR già presenti e li registra. Questo permette di gestire i QR che MRUK rileva subito all'avvio. Dopo questa scansione iniziale, ci affidiamo solo agli eventi MRUK per aggiornamenti, e alla coroutine di recovery se perdiamo dei QR.
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
    // Quando MRUK rileva un nuovo trackable QR, lo registra o aggiorna la sua posizione se già noto. Aggiorna la cache dei trackable conosciuti, così da poter fare recovery senza FindObjectsByType.
    private void OnTrackableAdded(MRUKTrackable trackable)
    {
        if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode) return;

        _knownTrackables.Add(trackable); // aggiorna la cache
        RegisterOrUpdateTrackable(trackable);
    }

    // Quando MRUK perde un trackable QR, non lo rimuove dal dizionario dei QR rilevati, così da mantenere la posizione finché non troviamo 4 QR validi. Avvia il recovery se non abbiamo ancora 4 QR, così da tentare di recuperare i QR persi senza aspettare nuovi eventi MRUK.
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
    // PULIZIA ALLA CHIUSURA — rimuove i listener per evitare errori, ferma la coroutine di recovery se è in esecuzione, e resetta il singleton.
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

    // Permette di assegnare il modello del paziente da allineare.
    public void SetHologram(GameObject loadedHologram)
    {
        _patientHologram = loadedHologram;
        Debug.Log("[SA] Hologram ricevuto.");
        TryAlign();
    }

    // Permette di resettare l'allineamento, tornando allo stato iniziale.
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

        CalculateNoduleDebug();
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


    private void CalculateNoduleDebug()
    {
        if (_patientHologram == null) return;

        // 1. RICERCA AUTOMATICA DEL NODULO E CALCOLO INVERSO
        Transform noduleTransform = null;
        
        // Cerca tra tutti i renderer figli del modello importato
        MeshRenderer[] renderers = _patientHologram.GetComponentsInChildren<MeshRenderer>();
        foreach (MeshRenderer rend in renderers)
        {
            if (rend.gameObject.name.ToLowerInvariant().Contains("nodule"))
            {
                noduleTransform = rend.transform;
                break; // Trovato, fermiamo la ricerca
            }
        }

        if (noduleTransform != null)
        {
            // Prendi la posizione globale
            Vector3 noduleWorldPos = noduleTransform.position;
            
            // Se l'oggetto nodulo ha un MeshFilter, prendiamo il centro esatto della geometria (centroide)
            MeshFilter mf = noduleTransform.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) 
            {
                noduleWorldPos = noduleTransform.TransformPoint(mf.sharedMesh.bounds.center);
            }

            // Calcola la distanza fisica globale in metri (ignorando le scale dei Transform padri)
            Vector3 worldOffset = noduleWorldPos - _patientHologram.transform.position;

            // Riconverti in coordinate Slicer (millimetri e sistema RAS)
            // X = Right, Y = Superior, Z = Anterior
            float slicerR = worldOffset.x * 1000f;
            float slicerS = worldOffset.y * 1000f;
            float slicerA = worldOffset.z * 1000f;

            Debug.Log($"[DEBUG NODULO] Trovato automaticamente: '{noduleTransform.name}'");
            Debug.Log($"[DEBUG NODULO] Offset globale in Unity (m) -> X: {worldOffset.x:F4}, Y: {worldOffset.y:F4}, Z: {worldOffset.z:F4}");
            Debug.Log($"[DEBUG NODULO] Coordinate stimate Slicer (mm) -> R: {slicerR:F3}, A: {slicerA:F3}, S: {slicerS:F3}");
        }
        else
        {
            Debug.LogWarning("[DEBUG NODULO] Nessun sub-modello contenente la parola 'nodule' trovato.");
        }

        // 2. VISUAL DEBUG: Sferetta rossa target basata su 3D Slicer
        if (_showNoduleDebug)
        {
            // Le tue coordinate di Slicer in millimetri
            float targetR = -58.972f;
            float targetA = -86.941f;
            float targetS = -120.000f;

            // Mappatura da Slicer a Unity Offset (in metri):
            // R = X, S = Y, A = Z
            Vector3 theoreticalWorldOffset = new Vector3(targetR / 1000f, targetS / 1000f, targetA / 1000f);
            
            // Aggiungiamo l'offset calcolato alla posizione globale dell'isocentro
            Vector3 theoreticalWorldPos = _patientHologram.transform.position + theoreticalWorldOffset;

            // Genera l'indicatore visivo (Sferetta)
            GameObject debugIndicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(debugIndicator.GetComponent<Collider>()); // Rimuovi collider
            debugIndicator.transform.position = theoreticalWorldPos;
            debugIndicator.transform.localScale = Vector3.one * 0.015f; // Sferetta da 1.5 cm
            debugIndicator.name = "Isocenter_Theoretical_Target";

            Material mat = new Material(Shader.Find("Standard"));
            mat.color = Color.red;
            debugIndicator.GetComponent<Renderer>().material = mat;

            Debug.Log($"[DEBUG NODULO] Sfera rossa generata alla posizione globale target: {theoreticalWorldPos}");
        }
    }
}