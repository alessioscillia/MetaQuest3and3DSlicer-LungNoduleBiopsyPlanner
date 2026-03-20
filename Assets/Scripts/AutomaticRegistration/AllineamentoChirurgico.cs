using System;
using System.Collections.Generic;
using UnityEngine;
using Meta.XR.MRUtilityKit; // Usiamo SOLO la libreria ufficiale base

public class AllineamentoChirurgico : MonoBehaviour
{
    // --- SINGLETON ---
    public static AllineamentoChirurgico Instance { get; private set; }

    // L'ologramma verrà impostato automaticamente dallo script AnatomyImporter.
    private GameObject _ologrammaPaziente;

    // Dizionario per tenere traccia dei QR in modo sicuro
    private Dictionary<Guid, MRUKTrackable> qrRilevati = new Dictionary<Guid, MRUKTrackable>();

    private void Awake()
    {
        // Setup del Singleton
        if (Instance == null) { Instance = this; }
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
        if (MRUK.Instance == null)
        {
            Debug.LogError("ERRORE: MRUK non trovato. Assicurati di aver aggiunto il MRUK component alla scena.");
            return;
        }

        // Iscrizione agli eventi di tracciamento
        MRUK.Instance.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
        MRUK.Instance.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);
    }

    void OnDestroy()
    {
        if (MRUK.Instance != null)
        {
            MRUK.Instance.SceneSettings.TrackableAdded.RemoveListener(OnTrackableAdded);
            MRUK.Instance.SceneSettings.TrackableRemoved.RemoveListener(OnTrackableRemoved);
        }
        if (Instance == this) { Instance = null; }
    }

    // --- METODO: Inserimento automatico dall'Importer ---
    public void ImpostaOlogramma(GameObject ologrammaCaricato)
    {
        _ologrammaPaziente = ologrammaCaricato;
        Debug.Log("AllineamentoChirurgico: Ologramma ricevuto e pronto per l'allineamento.");

        // Se abbiamo gi� trovato i 4 QR prima che il download finisse, allineiamo subito
        VerificaEAllinea();
    }

    private void OnTrackableAdded(MRUKTrackable trackable)
    {
        // Controlliamo che sia effettivamente un QRCode
        if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode) return;

        Guid idQr = trackable.Anchor.Uuid;

        if (!qrRilevati.ContainsKey(idQr))
        {
            qrRilevati.Add(idQr, trackable);

            // Usiamo MarkerPayloadString per leggere il contenuto, come indicato dalla documentazione
            Debug.Log($"QR Rilevato! Payload: {trackable.MarkerPayloadString}. Trovati: ({qrRilevati.Count}/4)");

            VerificaEAllinea();
        }
    }

    private void OnTrackableRemoved(MRUKTrackable trackable)
    {
        if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode) return;

        Guid idQr = trackable.Anchor.Uuid;
        if (qrRilevati.ContainsKey(idQr))
        {
            qrRilevati.Remove(idQr);
            Debug.Log("Attenzione: Un QR Code � stato perso dalla vista.");
        }
    }

    private void VerificaEAllinea()
    {
        // Serve aver trovato 4 QR e aver caricato il modello
        if (qrRilevati.Count < 4) return;

        if (_ologrammaPaziente == null)
        {
            Debug.Log("4 QR trovati, ma sto aspettando che AnatomyImporter mi consegni il modello...");
            return;
        }

        // 1. CALCOLO DELL'ISOCENTRO
        Vector3 sommaPosizioni = Vector3.zero;
        foreach (var qr in qrRilevati.Values)
        {
            sommaPosizioni += qr.transform.position;
        }

        Vector3 isocentro = sommaPosizioni / 4f;

        // 2. SPOSTAMENTO DELL'OLOGRAMMA
        _ologrammaPaziente.transform.position = isocentro;

        Debug.Log($"ALLINEAMENTO ESEGUITO! Ologramma posizionato a: {isocentro}");
    }
}