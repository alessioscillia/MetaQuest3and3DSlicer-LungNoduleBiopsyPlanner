using UnityEngine;

public class LaserUIManager : MonoBehaviour
{
    [Header("Riferimento al Puntatore")]
    public SurgicalLaserPointer laserPointer;

    [Header("Impostazioni Spawn")]
    [Tooltip("La telecamera principale (l'utente) per fargli spawnare il laser davanti")]
    public Transform playerCamera;
    public float spawnDistanceInFront = 0.5f;

    // Aggiungiamo una variabile per ricordare se la sfera è attualmente nascosta
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
}