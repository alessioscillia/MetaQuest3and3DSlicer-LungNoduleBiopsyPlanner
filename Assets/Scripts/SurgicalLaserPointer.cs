using UnityEngine;
using Oculus.Interaction;

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
    public float reticleScale = 0.05f;
    [Tooltip("A che distanza dalla mano far comparire il mirino (es. 0.3 = 30 cm)")]
    public float reticleDistance = 0.3f;

    [Header("Filtro Hit (Ostacoli + Bersaglio)")]
    [Tooltip("Inserisci qui TUTTI i layer che il laser deve rilevare (Noduli, Ossa, Vasi, ecc.)")]
    public LayerMask hittableLayers;

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
        
        // Setup materiale Laser
        lineRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        lineRenderer.material.color = normalColor;
        
        // --- EFFETTO RAGGI X PER IL LASER ---
        lineRenderer.material.renderQueue = 4000;
        lineRenderer.material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);

        oculusGrabbable = GetComponent<Grabbable>();

        if (reticlePrefab != null)
        {
            reticleInstance = Instantiate(reticlePrefab);
            reticleInstance.transform.localScale = Vector3.one * reticleScale;
            reticleRenderer = reticleInstance.GetComponentInChildren<Renderer>();

            // --- EFFETTO RAGGI X PER IL MIRINO ---
            if (reticleRenderer != null)
            {
                Material reticleMat = reticleRenderer.material;
                reticleMat.renderQueue = 4000;
                reticleMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                reticleMat.SetInt("_ZWrite", 0); 
            }
        }
    }

    void Update()
    {
        Vector3 origin = transform.position;
        Vector3 direction = transform.forward;
        lineRenderer.SetPosition(0, origin);

        float currentHitDistance = maxDistance;
        bool hitNodule = false;

        // Il Raycast ora colpisce TUTTO ciò che è incluso nella maschera 'hittableLayers'
        if (Physics.Raycast(origin, direction, out RaycastHit hit, maxDistance, hittableLayers))
        {
            currentHitDistance = hit.distance;
            lineRenderer.SetPosition(1, hit.point);

            // Controlliamo il nome dell'oggetto colpito per capire se è il nodulo
            string hitName = hit.collider.gameObject.name.ToLowerInvariant();
            
            // Se colpisce il nodulo per primo, diventa verde. 
            // Se colpisce un osso o un vaso, si ferma lì e rimane rosso.
            if (hitName.Contains("nodule"))
            {
                hitNodule = true;
            }
        }
        else
        {
            lineRenderer.SetPosition(1, origin + direction * maxDistance);
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