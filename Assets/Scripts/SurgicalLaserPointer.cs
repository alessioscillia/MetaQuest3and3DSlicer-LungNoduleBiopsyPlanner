using UnityEngine;
using Oculus.Interaction;
using TMPro;

[RequireComponent(typeof(LineRenderer))]
public class SurgicalLaserPointer : MonoBehaviour
{
    [Header("Impostazioni Raggio")]
    public float maxDistance = 2.0f;
    public float lineWidth = 0.002f;
    public Color normalColor = Color.red;
    public Color targetHitColor = Color.green;

    [Header("Impostazioni Mirino")]
    public GameObject reticlePrefab; // Il tuo Quad con il materiale mostrato
    public float reticleScale = 0.5f;

    [Header("Filtri Hit (Livelli Laser)")]
    [Tooltip("Ostacoli e Noduli (es. Obstacle, Nodule)")]
    public LayerMask hittableLayers;
    
    [Header("Calcolo Distanze")]
    [Tooltip("Inserisci qui SOLO il layer della pelle")]
    public LayerMask skinLayer;
    [Tooltip("Inserisci qui SOLO il layer dei polmoni/pleura")]
    public LayerMask pleuraLayer;
    [Tooltip("Il testo nel Canvas dove verrà mostrata la distanza")]
    public TextMeshProUGUI distanceTextUI; 

    private LineRenderer lineRenderer;
    private Grabbable oculusGrabbable;
    private GameObject reticleInstance;
    private Renderer reticleRenderer;

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.positionCount = 2;
        
        lineRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        lineRenderer.material.color = normalColor;
        lineRenderer.material.renderQueue = 4000;
        lineRenderer.material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);

        // Setup Mirino Principale
        if (reticlePrefab != null)
        {
            reticleInstance = Instantiate(reticlePrefab, transform);
            reticleInstance.transform.localScale = Vector3.one * reticleScale;
            reticleRenderer = reticleInstance.GetComponentInChildren<Renderer>();

            if (reticleRenderer != null)
            {
                // Crea un'istanza unica del materiale per non alterare il prefab
                reticleRenderer.material = new Material(reticleRenderer.material);
                Material reticleMat = reticleRenderer.material;
                
                // --- TRUCCO PER RENDERE IL MIRINO ADERENTE E VISIBILE ---
                // Il tuo materiale è già Transparent. Dobbiamo solo forzarlo sopra tutto.
                reticleMat.renderQueue = 4000; // Imposta la coda di rendering su Overlay
                reticleMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always); // Disattiva il depth test
                reticleMat.SetInt("_ZWrite", 0); // Non scrive nel depth buffer
            }
        }
        
        if (distanceTextUI != null) distanceTextUI.text = "Skin: N/A\nLungs: N/A";
    }

    void Update()
    {
        Vector3 origin = transform.position;
        Vector3 direction = transform.forward;
        lineRenderer.SetPosition(0, origin);

        // --- NUOVA LOGICA: Il mirino si posiziona sempre sulla pelle se rilevata ---
        bool skinHitDetected = false;
        Vector3 skinHitPoint = origin;
        Vector3 skinHitNormal = -direction; // Default, se non colpisce

        if (Physics.Raycast(origin, direction, out RaycastHit skinHit, maxDistance, skinLayer))
        {
            skinHitDetected = true;
            skinHitPoint = skinHit.point;
            skinHitNormal = skinHit.normal;
        }

        // Raycast principale (per rilevare il nodulo e le distanze)
        bool hitNodule = false;
        if (Physics.Raycast(origin, direction, out RaycastHit hit, maxDistance, hittableLayers))
        {
            lineRenderer.SetPosition(1, hit.point);

            string hitName = hit.collider.gameObject.name.ToLowerInvariant();
            
            if (hitName.Contains("nodule"))
            {
                hitNodule = true;
                
                if (distanceTextUI != null)
                {
                    string finalDisplayText = "";

                    // Calcolo della distanza Skin-Nodule (usando il skinHitDetected calcolato sopra)
                    if (skinHitDetected)
                    {
                        float distSkin = Vector3.Distance(skinHitPoint, hit.point) * 100f; // dovrebbe essere 100, ma usiamo 20 per adattarci al modello 
                        finalDisplayText += $"Skin: {distSkin:F1} cm\n";
                    }
                    else
                    {
                        finalDisplayText += "Skin: N/A\n";
                    }

                    // --- CALCOLO PLEURA ---
                    if (Physics.Raycast(origin, direction, out RaycastHit pleuraHit, maxDistance, pleuraLayer))
                    {
                        float distPleura = Vector3.Distance(pleuraHit.point, hit.point) * 100f;
                        finalDisplayText += $"Lungs: {distPleura:F1} cm";
                    }
                    else
                    {
                        finalDisplayText += "Lungs: N/A";
                    }

                    distanceTextUI.text = finalDisplayText;
                }
            }
        }
        else
        {
            // Se non colpisce noduli o ostacoli, il raggio va alla massima distanza
            lineRenderer.SetPosition(1, origin + direction * maxDistance);
        }

        // Reset del testo se non colpiamo il nodulo
        if (!hitNodule && distanceTextUI != null)
        {
            distanceTextUI.text = "Skin: N/A\nLungs: N/A";
        }

        // --- POSIZIONAMENTO DEL MIRINO ADERENTE ALLA PELLE ---
        if (reticleInstance != null)
        {
            reticleInstance.SetActive(true);
            
            if (skinHitDetected)
            {
                // 1. OFFSET: Spingiamo il mirino in fuori di 5 millimetri lungo la normale della pelle
                // Questo impedisce fisicamente alla pelle di coprirlo.
                float offset = 0.005f; 
                reticleInstance.transform.position = skinHitPoint + (skinHitNormal * offset);

                // 2. ROTAZIONE: I Quad di Unity hanno la faccia visibile rivolta verso -Z.
                // Per farlo "guardare verso l'esterno", allineiamo la sua Z verso l'interno della pelle (-skinHitNormal)
                reticleInstance.transform.rotation = Quaternion.LookRotation(-skinHitNormal);
            }
            else
            {
                // Se la pelle non è colpita, posiziona il mirino alla massima distanza
                reticleInstance.transform.position = origin + (direction * maxDistance);
                // Qui guarda verso chi tiene il laser
                reticleInstance.transform.rotation = Quaternion.LookRotation(-direction); 
            }
        }

        if (hitNodule) SetColor(targetHitColor);
        else SetColor(normalColor);
    }

    private void SetColor(Color color)
    {
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
        lineRenderer.material.color = color;

        if (reticleRenderer != null && reticleRenderer.material != null)
        {
            if (reticleRenderer.material.HasProperty("_BaseColor"))
                reticleRenderer.material.SetColor("_BaseColor", color);
            else if (reticleRenderer.material.HasProperty("_Color"))
                reticleRenderer.material.SetColor("_Color", color);
        }
    }

    public void HideSphere()
    {
        MeshRenderer mesh = GetComponent<MeshRenderer>();
        if (mesh != null) mesh.enabled = false;
        if (oculusGrabbable != null) oculusGrabbable.enabled = false;
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
    }

    public void ShowSphere()
    {
        MeshRenderer mesh = GetComponent<MeshRenderer>();
        if (mesh != null) mesh.enabled = true;
        if (oculusGrabbable != null) oculusGrabbable.enabled = true;
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = true;
    }
}