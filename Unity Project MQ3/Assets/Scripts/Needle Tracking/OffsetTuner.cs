using UnityEngine;
using TMPro;

/// <summary>
/// Permette di tarare l'offset fisico della camera in tempo reale.
/// - Levetta Sinistra (Verticale) -> Regola Y (Altezza)
/// - Levetta Destra (Verticale)   -> Regola Z (Profondità)
/// - Asse X rimane FISSO.
/// </summary>
public class OffsetTuner : MonoBehaviour
{
    public PoseClient poseClient;
    
    [Header("Riferimento UI")]
    public TextMeshProUGUI debugText;

    [Header("Impostazioni Sensibilità")]
    public float baseSpeed = 0.005f; 
    public float deadzone = 0.15f;
    public float precisionMultiplier = 0.1f;

    void Start()
    {
        if (poseClient == null)
            poseClient = GetComponent<PoseClient>();

        if (debugText == null)
            Debug.LogError("[OffsetTuner] Manca il riferimento a debugText nell'Inspector!");
    }

    void Update()
    {
        if (poseClient == null) return;

        // Lettura input dalle levette (ci interessano solo gli assi verticali .y)
        Vector2 rightStick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
        Vector2 leftStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);

        // Applica la Deadzone solo sull'asse verticale di ciascuna levetta
        float deltaZ = (Mathf.Abs(rightStick.y) > deadzone) ? rightStick.y : 0f;
        float deltaY = (Mathf.Abs(leftStick.y) > deadzone) ? leftStick.y : 0f;

        // Verifica se c'è movimento attivo
        bool isMoving = (deltaZ != 0f || deltaY != 0f);

        // Modalità Precisione (Grilletti)
        float currentSpeed = baseSpeed;
        bool isPrecisionMode = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger) || OVRInput.Get(OVRInput.Button.SecondaryIndexTrigger);
        if (isPrecisionMode)
        {
            currentSpeed *= precisionMultiplier;
        }

        // Applica gli spostamenti: Y a sinistra, Z a destra. X RESTA INVARIATA.
        poseClient.cameraPhysicalOffset.y += deltaY * currentSpeed * Time.deltaTime;
        poseClient.cameraPhysicalOffset.z += deltaZ * currentSpeed * Time.deltaTime;

        // Aggiorna l'interfaccia sul Canvas VR
        if (debugText != null)
        {
            string colorHex = isPrecisionMode ? "#00FFFF" : "#FFFF00";
            
            debugText.text = $"<color={colorHex}><b>[TARATURA OFFSET]</b></color>\n\n" +
                             $"<b>X (Orizzontale):</b> {poseClient.cameraPhysicalOffset.x * 1000:F1} mm <color=#888888>(BLOCCATO)</color>\n" +
                             $"<b>Y (Altezza):</b> {poseClient.cameraPhysicalOffset.y * 1000:F1} mm <color=#66FF66>(Stick L)</color>\n" +
                             $"<b>Z (Profondità):</b> {poseClient.cameraPhysicalOffset.z * 1000:F1} mm <color=#6666FF>(Stick R)</color>\n\n" +
                             $"<size=80%>{(isPrecisionMode ? "Modalità Precisione ATTIVA" : "Premi il grilletto per rallentare")}</size>";
        }
    }
}