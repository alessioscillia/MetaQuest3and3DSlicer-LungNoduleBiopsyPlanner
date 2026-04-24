using UnityEngine;
using UnityEngine.UI; // IMPORTANTE: Aggiunto per poter usare le Image

public class LaserUIManager : MonoBehaviour
{
    [Header("Riferimento al Puntatore")]
    public SurgicalLaserPointer laserPointer;

    [Header("Impostazioni Spawn")]
    [Tooltip("La telecamera principale (l'utente) per fargli spawnare il laser davanti")]
    public Transform playerCamera;
    public float spawnDistanceInFront = 0.5f;

    [Header("Button Visual States")]
    [Tooltip("Trascina qui l'Image del Box_Needle")]
    public Image needleImage;
    [Tooltip("Trascina qui l'Image del Box_FixLaser (se vuoi l'effetto anche per lui)")]
    public Image fixLaserImage;

    [Tooltip("Colore dell'interno del tasto quando NON è selezionato")]
    public Color normalButtonColor = new Color(0f, 0f, 0f, 0f); // Trasparente
    [Tooltip("Colore dell'interno del tasto quando E' selezionato")]
    public Color selectedButtonColor = new Color(1f, 1f, 1f, 0.3f); // Bianco semitrasparente

    // Variabile per ricordare se la sfera è attualmente nascosta
    private bool isSphereHidden = false; 

    void Start()
    {
        if (laserPointer != null)
        {
            laserPointer.gameObject.SetActive(false);
        }
        
        // Assicuriamoci che i tasti siano spenti all'avvio
        SetButtonVisualState(needleImage, false);
        SetButtonVisualState(fixLaserImage, false);
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

        // Aggiorniamo l'UI: L'ago è attivo, il fix è disattivo
        SetButtonVisualState(needleImage, true);
        SetButtonVisualState(fixLaserImage, false);
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
            
            // UI: Il blocco è disattivato
            SetButtonVisualState(fixLaserImage, false);
        }
        else
        {
            // Se era visibile, la nascondiamo e la blocchiamo
            laserPointer.HideSphere();
            isSphereHidden = true; // Aggiorniamo la memoria
            
            // UI: Il blocco è attivato
            SetButtonVisualState(fixLaserImage, true);
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

            // UI: Ago acceso, fix resettato
            SetButtonVisualState(needleImage, true);
            SetButtonVisualState(fixLaserImage, false);
        }
        else
        {
            // Se il sistema è attualmente ACCESO, lo spegniamo semplicemente
            laserPointer.gameObject.SetActive(false);

            // UI: Tutto spento
            SetButtonVisualState(needleImage, false);
            SetButtonVisualState(fixLaserImage, false);
        }
    }

    private void SetButtonVisualState(Image buttonImage, bool isActive)
    {
        if (buttonImage != null)
        {
            buttonImage.color = isActive ? selectedButtonColor : normalButtonColor;
        }
    }
}