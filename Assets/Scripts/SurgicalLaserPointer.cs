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

    [Header("Filtro Layer")]
    public LayerMask targetLayer;

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
        // renderQueue 4000 = Overlay (sopra a tutto)
        // _ZTest 8 = Always (ignora gli oggetti davanti)
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
                reticleMat.SetInt("_ZWrite", 0); // Non bloccare altri oggetti dietro di esso
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

        if (Physics.Raycast(origin, direction, out RaycastHit hit, maxDistance, targetLayer))
        {
            currentHitDistance = hit.distance;
            lineRenderer.SetPosition(1, hit.point);
            hitNodule = true;
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
        // 1. Nascondi la grafica della sfera (diventa invisibile)
        MeshRenderer mesh = GetComponent<MeshRenderer>();
        if (mesh != null) mesh.enabled = false;

        // 2. Disabilita la possibilità di afferrarla
        if (oculusGrabbable != null) oculusGrabbable.enabled = false;

        // 3. (Opzionale ma consigliato) Disabilita il collider per non sbatterci contro per sbaglio
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
    }

    public void ShowSphere()
    {
        // 1. Fai ricomparire la grafica della sfera
        MeshRenderer mesh = GetComponent<MeshRenderer>();
        if (mesh != null) mesh.enabled = true;

        // 2. Riabilita la presa
        if (oculusGrabbable != null) oculusGrabbable.enabled = true;

        // 3. Riabilita il collider
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = true;
    }
}