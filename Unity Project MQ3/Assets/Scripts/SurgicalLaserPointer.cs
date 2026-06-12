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
    
    [Tooltip("Crea un materiale URP/Unlit nel progetto e trascinalo qui! Non lasciarlo vuoto.")]
    public Material baseLaserMaterial; 

    [Header("Impostazioni Mirino")]
    public GameObject reticlePrefab; 
    public float reticleScale = 0.5f;

    [Header("Filtri Hit (Livelli Laser)")]
    public LayerMask hittableLayers;
    
    [Header("Calcolo Distanze")]
    public LayerMask skinLayer;
    public LayerMask pleuraLayer;
    public TextMeshProUGUI distanceTextUI;
    
    [Header("Trajectory Confirmation")]
    public float skinEntryMarkerDiameter = 0.01f;
    public float skinEntryLabelFontSize = 0.035f;
    public float skinEntryLabelOffsetRight = 0.04f;
    public float skinEntryLabelOffsetUp = 0.025f;
    public Color skinEntryColor = Color.red;

    private GameObject skinEntryMarkerInstance;
    private TextMeshPro skinEntryLabelInstance;
    private bool trajectoryConfirmed = false;

    public bool TrajectoryConfirmed => trajectoryConfirmed;
    public Vector3 ConfirmedSkinEntryPoint { get; private set; }

    private LineRenderer lineRenderer;
    private Grabbable oculusGrabbable;
    private GameObject reticleInstance;
    private Renderer reticleRenderer;
    public Vector3 SkinHitPoint      { get; private set; }
    public Vector3 NoduleHitPoint    { get; private set; }
    public bool    TrajectoryDefined { get; private set; }

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        oculusGrabbable = GetComponent<Grabbable>();

        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.positionCount = 2;
        
        // --- FIX SHADER STRIPPING ---
        if (baseLaserMaterial != null)
        {
            // Creiamo un'istanza del materiale assegnato da Inspector, così non modifichiamo l'originale
            lineRenderer.material = new Material(baseLaserMaterial);
            lineRenderer.material.color = normalColor;
            lineRenderer.material.renderQueue = 4000;
            lineRenderer.material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }
        else
        {
            Debug.LogError("[SurgicalLaserPointer] MANCA IL MATERIALE BASE! Assegnalo nell'Inspector.");
        }
        // -----------------------------

        // Setup Mirino Principale
        if (reticlePrefab != null)
        {
            reticleInstance = Instantiate(reticlePrefab, transform);
            reticleInstance.transform.localScale = Vector3.one * reticleScale;
            reticleRenderer = reticleInstance.GetComponentInChildren<Renderer>();

            if (reticleRenderer != null && reticleRenderer.material != null)
            {
                reticleRenderer.material = new Material(reticleRenderer.material);
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

        // La traiettoria deve essere rivalutata a ogni frame.
        // Evita che rimanga true da un frame precedente.
        TrajectoryDefined = false;

        bool skinHitDetected = false;
        Vector3 skinHitPoint = origin;
        Vector3 skinHitNormal = -direction; 

        if (Physics.Raycast(origin, direction, out RaycastHit skinHit, maxDistance, skinLayer))
        {
            skinHitDetected = true;
            skinHitPoint = skinHit.point;
            skinHitNormal = skinHit.normal;
        }

        bool hitNodule = false;
        if (skinHitDetected) SkinHitPoint = skinHitPoint;

        if (Physics.Raycast(origin, direction, out RaycastHit hit, maxDistance, hittableLayers))
        {
            lineRenderer.SetPosition(1, hit.point);
            string hitName = hit.collider.gameObject.name.ToLowerInvariant();

            if (hitName.Contains("nodule"))
            {
                hitNodule          = true;
                NoduleHitPoint     = hit.point;   
                // Traiettoria valida solo se colpisce un nodulo e abbiamo un punto di ingresso sulla pelle      
                TrajectoryDefined  = skinHitDetected;
                
                if (distanceTextUI != null)
                {
                    string finalDisplayText = "";

                    if (skinHitDetected)
                    {
                        float distSkin = Vector3.Distance(skinHitPoint, hit.point) * 100f; 
                        finalDisplayText += $"Skin: {distSkin:F1} cm\n";
                    }
                    else
                    {
                        finalDisplayText += "Skin: N/A\n";
                    }

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
            lineRenderer.SetPosition(1, origin + direction * maxDistance);
            TrajectoryDefined = false;
        }

        if (!hitNodule && distanceTextUI != null)
        {
            distanceTextUI.text = "Skin: N/A\nLungs: N/A";
        }

        if (reticleInstance != null)
        {
            // Dopo la conferma della traiettoria, il mirino deve sparire.
            reticleInstance.SetActive(!trajectoryConfirmed);

            if (!trajectoryConfirmed)
            {
                if (skinHitDetected)
                {
                    float offset = 0.005f; 
                    reticleInstance.transform.position = skinHitPoint + (skinHitNormal * offset);
                    reticleInstance.transform.rotation = Quaternion.LookRotation(-skinHitNormal);
                }
                else
                {
                    reticleInstance.transform.position = origin + (direction * maxDistance);
                    reticleInstance.transform.rotation = Quaternion.LookRotation(-direction); 
                }
            }
        }

        UpdateSkinEntryLabelBillboard();

        if (hitNodule) SetColor(targetHitColor);
        else SetColor(normalColor);
    }

    private void SetColor(Color color)
    {
        // Colore ai vertici della linea
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
        
        // --- FIX URP COLOR ---
        // Cambiamo dinamicamente il colore del materiale del raggio laser
        if (lineRenderer.material != null)
        {
            if (lineRenderer.material.HasProperty("_BaseColor"))
                lineRenderer.material.SetColor("_BaseColor", color);
            else if (lineRenderer.material.HasProperty("_Color"))
                lineRenderer.material.SetColor("_Color", color);
            else
                lineRenderer.material.color = color; // Fallback
        }

        // Cambiamo dinamicamente il colore del materiale del mirino
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
    public bool ConfirmCurrentTrajectory()
    {
        if (!TrajectoryDefined)
        {
            Debug.LogWarning("[SurgicalLaserPointer] Impossibile confermare: la traiettoria corrente non è valida.");
            return false;
        }

        trajectoryConfirmed = true;
        ConfirmedSkinEntryPoint = SkinHitPoint;

        CreateOrUpdateSkinEntryMarker(ConfirmedSkinEntryPoint);

        if (reticleInstance != null)
            reticleInstance.SetActive(false);

        Debug.Log($"[SurgicalLaserPointer] Traiettoria confermata. SkinEntryPoint = {ConfirmedSkinEntryPoint}");

        return true;
    }

    public void ClearConfirmedTrajectory()
    {
        trajectoryConfirmed = false;

        if (skinEntryMarkerInstance != null)
        {
            Destroy(skinEntryMarkerInstance);
            skinEntryMarkerInstance = null;
        }

        if (skinEntryLabelInstance != null)
        {
            Destroy(skinEntryLabelInstance.gameObject);
            skinEntryLabelInstance = null;
        }

        if (reticleInstance != null)
            reticleInstance.SetActive(true);
    }

    private void CreateOrUpdateSkinEntryMarker(Vector3 worldPosition)
    {
        if (skinEntryMarkerInstance == null)
        {
            skinEntryMarkerInstance = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            skinEntryMarkerInstance.name = "SkinEntryPoint_Marker";

            Collider markerCollider = skinEntryMarkerInstance.GetComponent<Collider>();
            if (markerCollider != null)
                Destroy(markerCollider);

            Renderer markerRenderer = skinEntryMarkerInstance.GetComponent<Renderer>();
            if (markerRenderer != null)
            {
                Material markerMat = CreateVisibleRedMaterial();
                markerRenderer.material = markerMat;
            }
        }

        skinEntryMarkerInstance.transform.position = worldPosition;
        skinEntryMarkerInstance.transform.localScale = Vector3.one * skinEntryMarkerDiameter;

        if (skinEntryLabelInstance == null)
        {
            GameObject labelObject = new GameObject("SkinEntryPoint_Label");
            skinEntryLabelInstance = labelObject.AddComponent<TextMeshPro>();

            skinEntryLabelInstance.text = "SkinEntryPoint";
            skinEntryLabelInstance.color = skinEntryColor;
            skinEntryLabelInstance.fontSize = skinEntryLabelFontSize;
            skinEntryLabelInstance.alignment = TextAlignmentOptions.Left;
            skinEntryLabelInstance.enableWordWrapping = false;

            Renderer labelRenderer = skinEntryLabelInstance.GetComponent<Renderer>();
            if (labelRenderer != null && labelRenderer.material != null)
            {
                labelRenderer.material.renderQueue = 4001;
            }
        }

        UpdateSkinEntryLabelBillboard();
    }

    private Material CreateVisibleRedMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        if (shader == null)
            shader = Shader.Find("Standard");

        Material mat = new Material(shader);
        mat.renderQueue = 4000;

        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", skinEntryColor);
        else if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", skinEntryColor);
        else
            mat.color = skinEntryColor;

        // Proviamo a renderizzarla sempre visibile sopra il modello.
        if (mat.HasProperty("_ZTest"))
            mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);

        if (mat.HasProperty("_ZWrite"))
            mat.SetInt("_ZWrite", 0);

        return mat;
    }

    private void UpdateSkinEntryLabelBillboard()
    {
        if (!trajectoryConfirmed || skinEntryLabelInstance == null)
            return;

        Camera cam = Camera.main;

        Vector3 labelPosition = ConfirmedSkinEntryPoint;

        if (cam != null)
        {
            labelPosition += cam.transform.right * skinEntryLabelOffsetRight;
            labelPosition += cam.transform.up * skinEntryLabelOffsetUp;

            skinEntryLabelInstance.transform.position = labelPosition;

            Vector3 directionToCamera = skinEntryLabelInstance.transform.position - cam.transform.position;
            if (directionToCamera.sqrMagnitude > 0.0001f)
            {
                skinEntryLabelInstance.transform.rotation = Quaternion.LookRotation(directionToCamera);
            }
        }
        else
        {
            skinEntryLabelInstance.transform.position =
                ConfirmedSkinEntryPoint + new Vector3(0.04f, 0.025f, 0f);
        }
    }
}