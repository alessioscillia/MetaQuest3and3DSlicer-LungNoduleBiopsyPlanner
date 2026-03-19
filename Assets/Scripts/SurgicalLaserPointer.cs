using UnityEngine;
using UnityEngine.UI; // Necessario per la UI standard
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

    [Header("Filtro Hit (Ostacoli + Bersaglio)")]
    public LayerMask hittableLayers;

    [Header("Calcolo Distanza (Pelle-Nodulo)")]
    [Tooltip("Inserisci qui SOLO il layer della pelle")]
    public LayerMask skinLayer;
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
        
        // Imposta il testo di default all'avvio
        if (distanceTextUI != null) distanceTextUI.text = "Skin: N/A";
    }

    void Update()
    {
        Vector3 origin = transform.position;
        Vector3 direction = transform.forward;
        lineRenderer.SetPosition(0, origin);

        float currentHitDistance = maxDistance;
        bool hitNodule = false;

        // 1. Raycast principale per Ostacoli e Noduli
        if (Physics.Raycast(origin, direction, out RaycastHit hit, maxDistance, hittableLayers))
        {
            currentHitDistance = hit.distance;
            lineRenderer.SetPosition(1, hit.point);

            string hitName = hit.collider.gameObject.name.ToLowerInvariant();
            
            if (hitName.Contains("nodule"))
            {
                hitNodule = true;
                
                // --- CALCOLO DELLA DISTANZA ---
                if (distanceTextUI != null)
                {
                    // Secondo raycast esclusivo per trovare l'intersezione con la pelle
                    if (Physics.Raycast(origin, direction, out RaycastHit skinHit, maxDistance, skinLayer))
                    {
                        // LA TUA IDEA: Calcoliamo la distanza tra il punto sulla pelle (skinHit.point) 
                        // e il punto esatto in cui il laser tocca il nodulo (hit.point)
                        float distanceInMeters = Vector3.Distance(skinHit.point, hit.point);
                        float distanceInCm = distanceInMeters * 20f; // dovrebbe essere 100, ma usiamo 20 per adattarsi alla scala del modello 
                        
                        distanceTextUI.text = $"Skin: {distanceInCm:F1} cm";
                    }
                    else
                    {
                        distanceTextUI.text = "Skin: N/A";
                    }
                }
            }
        }
        else
        {
            lineRenderer.SetPosition(1, origin + direction * maxDistance);
        }

        // Se NON stiamo colpendo un nodulo puliamo il testo sulla UI
        if (!hitNodule && distanceTextUI != null)
        {
            distanceTextUI.text = "Skin: N/A";
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