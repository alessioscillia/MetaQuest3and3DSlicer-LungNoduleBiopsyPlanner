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
    public GameObject reticlePrefab;
    public float reticleScale = 0.5f;
    public float reticleDistance = 0.8f;

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

        oculusGrabbable = GetComponent<Grabbable>();

        if (reticlePrefab != null)
        {
            reticleInstance = Instantiate(reticlePrefab);
            reticleInstance.transform.localScale = Vector3.one * reticleScale;
            reticleRenderer = reticleInstance.GetComponentInChildren<Renderer>();

            if (reticleRenderer != null)
            {
                Material reticleMat = reticleRenderer.material;
                reticleMat.renderQueue = 4000;
                reticleMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                reticleMat.SetInt("_ZWrite", 0); 
            }
        }
        
        if (distanceTextUI != null) distanceTextUI.text = "Skin: N/A\nLungs: N/A";
    }

    void Update()
    {
        Vector3 origin = transform.position;
        Vector3 direction = transform.forward;
        lineRenderer.SetPosition(0, origin);

        float currentHitDistance = maxDistance;
        bool hitNodule = false;

        // 1. Raycast principale (si ferma su Noduli o Ostacoli come ossa/vasi)
        if (Physics.Raycast(origin, direction, out RaycastHit hit, maxDistance, hittableLayers))
        {
            currentHitDistance = hit.distance;
            lineRenderer.SetPosition(1, hit.point);

            string hitName = hit.collider.gameObject.name.ToLowerInvariant();
            
            if (hitName.Contains("nodule"))
            {
                hitNodule = true;
                
                if (distanceTextUI != null)
                {
                    string finalDisplayText = "";

                    // --- CALCOLO PELLE ---
                    if (Physics.Raycast(origin, direction, out RaycastHit skinHit, maxDistance, skinLayer))
                    {
                        float distSkin = Vector3.Distance(skinHit.point, hit.point) * 20f; // dovrebbe essere 100, ma usiamo 20 per adattarci al modello 
                        finalDisplayText += $"Skin: {distSkin:F1} cm\n";
                    }
                    else
                    {
                        finalDisplayText += "Skin: N/A\n";
                    }

                    // --- CALCOLO PLEURA ---
                    if (Physics.Raycast(origin, direction, out RaycastHit pleuraHit, maxDistance, pleuraLayer))
                    {
                        float distPleura = Vector3.Distance(pleuraHit.point, hit.point) * 20f; // dovrebbe essere 100, ma usiamo 20 per adattarci al modello
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
            lineRenderer.SetPosition(1, origin + direction * maxDistance);
        }

        // Reset del testo se colpiamo il vuoto o un ostacolo
        if (!hitNodule && distanceTextUI != null)
        {
            distanceTextUI.text = "Skin: N/A\nLungs: N/A";
        }

        if (reticleInstance != null)
        {
            reticleInstance.SetActive(true);
            float actualDistance = Mathf.Min(reticleDistance, currentHitDistance);
            reticleInstance.transform.position = origin + (direction * actualDistance);
            reticleInstance.transform.rotation = Quaternion.LookRotation(direction);
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