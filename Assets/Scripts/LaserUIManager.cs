using UnityEngine;

public class LaserUIManager : MonoBehaviour
{
    [Header("Riferimento al Puntatore")]
    public SurgicalLaserPointer laserPointer;

    [Header("Impostazioni Spawn")]
    [Tooltip("La telecamera principale (l'utente) per fargli spawnare il laser davanti")]
    public Transform playerCamera;
    public float spawnDistanceInFront = 0.5f;

    // Variabile per ricordare se la sfera è attualmente nascosta
    private bool isSphereHidden = false; 

    void Start()
    {
        if (laserPointer != null)
        {
            laserPointer.gameObject.SetActive(false);
        }
    }

    public void OnSpawnLaserClicked()
    {
        if (laserPointer == null || playerCamera == null) return;

        laserPointer.gameObject.SetActive(true);
        laserPointer.ShowSphere(); 
        
        // Resettiamo lo stato: se spawniamo una nuova sfera, ovviamente non è nascosta
        isSphereHidden = false; 

        laserPointer.transform.position = playerCamera.position + (playerCamera.forward * spawnDistanceInFront);
        laserPointer.transform.rotation = Quaternion.LookRotation(playerCamera.forward);
    }

    // Collega questo metodo all'evento OnClick del tuo bottone "Fix Laser"
    public void OnToggleFixLaserClicked()
    {
        if (laserPointer == null) return;

        // Controlliamo lo stato attuale e facciamo l'opposto
        if (isSphereHidden)
        {
            // Se era nascosta, la mostriamo di nuovo per farla riafferrare
            laserPointer.ShowSphere();
            isSphereHidden = false; // Aggiorniamo la memoria
        }
        else
        {
            // Se era visibile, la nascondiamo e la blocchiamo
            laserPointer.HideSphere();
            isSphereHidden = true; // Aggiorniamo la memoria
        }
    }

    // Collega questo metodo all'evento OnClick del tuo bottone dell'ago ("Needle")
    public void OnToggleNeedleClicked()
    {
        if (laserPointer == null || playerCamera == null) return;

        // Se il sistema è attualmente SPENTO, lo accendiamo e lo posizioniamo
        if (!laserPointer.gameObject.activeSelf)
        {
            // 1. Attiviamo l'oggetto
            laserPointer.gameObject.SetActive(true);
            
            // 2. Riposizioniamo il sistema davanti agli occhi (stessa logica dello spawn)
            laserPointer.transform.position = playerCamera.position + (playerCamera.forward * spawnDistanceInFront);
            laserPointer.transform.rotation = Quaternion.LookRotation(playerCamera.forward);
            
            // 3. Ci assicuriamo che la sfera sia visibile e lo stato resettato
            laserPointer.ShowSphere();
            isSphereHidden = false;
        }
        else
        {
            // Se il sistema è attualmente ACCESO, lo spegniamo semplicemente
            laserPointer.gameObject.SetActive(false);
        }
    }
}