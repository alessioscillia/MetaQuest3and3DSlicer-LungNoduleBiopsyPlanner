using UnityEngine;
using UnityEngine.UI;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
// using TMPro; // Rimuovi i commenti se stai usando TextMeshPro
public class AnatomyManager : MonoBehaviour
{
    // SINGLETON
    public static AnatomyManager Instance;
    [Header("Debug Info")]
    [SerializeField] private Renderer skinRenderer;
    [SerializeField] private Renderer lungRenderer;
    [SerializeField] private Renderer bonesRenderer;
    [SerializeField] private Renderer vesselsRenderer;
    [SerializeField] private Renderer airwaysRenderer;
    [SerializeField] private Renderer noduleRenderer;
    [SerializeField] private Renderer toolRenderer;
    private System.Collections.Generic.List<Renderer> totalSegmentatorRenderers;
    [Header("Slice System")]
    [SerializeField] private GameObject sliceSystemInstance;
    [Header("Interaction")]
    [Tooltip("Inserisci qui il prefab ISDK_RayGrabInteraction da attaccare all'ago")]
    [SerializeField] private GameObject rayGrabInteractionPrefab;
    private GameObject activeRayGrabInteraction; // Tiene traccia dell'oggetto istanziato
    [Tooltip("Prefab ISDK_RayGrabInteraction da attaccare al modello importato (TotalSegmentatorModel)")]
    [SerializeField] private GameObject modelRayGrabInteractionPrefab;
    private GameObject importedModelRoot;
    private GameObject modelRayGrabInteractionInstance;
    [Header("Toggle Texts")]
    [SerializeField] private Text skinToggleText;
    [SerializeField] private Text lungsToggleText;
    [SerializeField] private Text bonesToggleText;
    [SerializeField] private Text awVesselsToggleText;
    [SerializeField] private Text needlePathToggleText;
    [SerializeField] private Text tsToggleText;

    [Header("Opacity State")]
    [Range(0f, 1f)] [SerializeField] private float skinOpacity = 1f;
    [Range(0f, 1f)] [SerializeField] private float lungOpacity = 1f;
    [Range(0f, 1f)] [SerializeField] private float bonesOpacity = 1f;
    [Range(0f, 1f)] [SerializeField] private float awVesselsOpacity = 1f;
    [Range(0f, 1f)] [SerializeField] private float tsMasterOpacity = 1f;

    private bool isTSVisible;
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        totalSegmentatorRenderers = new System.Collections.Generic.List<Renderer>();
        // Il Tool deve essere inizialmente spento.
        ToggleTool(false);
    }
    // --- REGISTRAZIONE AUTOMATICA ---
    public void RegisterOrganRenderer(string objName, Renderer rend)
    {
        string lowerName = objName.ToLower();
        
        if (lowerName.Contains("skin")) 
        {
            skinRenderer = rend;
        }
        else if (lowerName.Contains("lung")) 
        {
            lungRenderer = rend;
        }
        else if (lowerName.Contains("bone") || lowerName.Contains("rib") || lowerName.Contains("vertebra")) 
        {
            bonesRenderer = rend;
            
            // Aggiungiamo il Collider alle ossa per bloccare il laser
            if (bonesRenderer.gameObject.GetComponent<Collider>() == null)
            {
                MeshCollider mc = bonesRenderer.gameObject.AddComponent<MeshCollider>();
                mc.convex = false; // False va bene per mesh complesse se usate solo per Raycast
            }
            // Assegniamo le ossa a un layer specifico (es. "Obstacle" o lo stesso dei vasi)
            bonesRenderer.gameObject.layer = LayerMask.NameToLayer("Obstacle"); 
        }
        else if (lowerName.Contains("vessel")) 
        {
            vesselsRenderer = rend;
            
            // Aggiungiamo il Collider ai vasi per bloccare il laser
            if (vesselsRenderer.gameObject.GetComponent<Collider>() == null)
            {
                MeshCollider mc = vesselsRenderer.gameObject.AddComponent<MeshCollider>();
                mc.convex = false;
            }
            // Assegniamo i vasi a un layer specifico (es. "Obstacle")
            vesselsRenderer.gameObject.layer = LayerMask.NameToLayer("Obstacle");
        }
        else if (lowerName.Contains("airways") || lowerName.Contains("trachea")) 
        {
            airwaysRenderer = rend;
            
            // Opzionale: se vuoi che anche le vie aeree blocchino il laser, scommenta qui sotto
            /*
            if (airwaysRenderer.gameObject.GetComponent<Collider>() == null)
            {
                MeshCollider mc = airwaysRenderer.gameObject.AddComponent<MeshCollider>();
                mc.convex = false;
            }
            airwaysRenderer.gameObject.layer = LayerMask.NameToLayer("Obstacle");
            */
        }
        else if (lowerName.Contains("nodule"))
        {
            noduleRenderer = rend;
            
            // 1. Assicuriamoci che il nodulo abbia un Collider per essere colpito dal laser
            if (noduleRenderer.gameObject.GetComponent<Collider>() == null)
            {
                MeshCollider mc = noduleRenderer.gameObject.AddComponent<MeshCollider>();
                mc.convex = false;
            }
            // 2. Assegniamo il layer "Nodule" all'oggetto
            noduleRenderer.gameObject.layer = LayerMask.NameToLayer("Nodule");
        }
        else if (lowerName.Contains("tool"))
        {
            toolRenderer = rend;
            
            // Per permettere a ISDK di afferrare l'oggetto, questo DEVE avere un Collider.
            if (toolRenderer.gameObject.GetComponent<Collider>() == null)
            {
                MeshCollider mc = toolRenderer.gameObject.AddComponent<MeshCollider>();
                mc.convex = true; // Convex è spesso richiesto per le interazioni fisiche
            }
            toolRenderer.enabled = false;
        }
        else
        {
            // Tutti i segmenti non categorizzati vanno in totalSegmentatorRenderers
            if (!totalSegmentatorRenderers.Contains(rend))
            {
                totalSegmentatorRenderers.Add(rend);
                rend.enabled = false; // Parte spento
            }
        }
    }
    public void RegisterSliceSystem(GameObject sliceSystem)
    {
        sliceSystemInstance = sliceSystem;
        Debug.Log("[AnatomyManager] Sistema di slicing registrato.");
    }
    public void RegisterImportedModel(GameObject modelRoot)
    {
        importedModelRoot = modelRoot;
        EnsureModelRayGrabInteraction();
        FixModel();
        Debug.Log("[AnatomyManager] Modello importato registrato.");
    }
    // --- OPACITY SLIDERS ---
    private void SetOpacity(Renderer rend, float alphaVal)
    {
        if (rend != null && rend.material != null)
        {
            ConfigureMaterialSurfaceForAlpha(rend.material, alphaVal);

            Color color = rend.material.color;
            color.a = alphaVal;
            rend.material.color = color;
            if (rend.material.HasProperty("_BaseColor")) rend.material.SetColor("_BaseColor", color);
            if (rend.material.HasProperty("_Color")) rend.material.SetColor("_Color", color);
        }
    }

    private void ConfigureMaterialSurfaceForAlpha(Material mat, float alphaVal)
    {
        if (mat == null)
        {
            return;
        }

        bool shouldBeTransparent = alphaVal < 0.999f;

        if (shouldBeTransparent)
        {
            // URP Lit/Unlit style properties
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1.0f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0.0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.SetShaderPassEnabled("ShadowCaster", false);
        }
        else
        {
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0.0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            mat.SetInt("_ZWrite", 1);
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            mat.SetShaderPassEnabled("ShadowCaster", true);
        }
    }
    public void UpdateSkinOpacity(float value)
    {
        skinOpacity = Mathf.Clamp01(value);
        SetOpacity(skinRenderer, skinOpacity);
    }
    public void UpdateLungOpacity(float value)
    {
        lungOpacity = Mathf.Clamp01(value);
        SetOpacity(lungRenderer, GetTSAdjustedOpacity(lungOpacity));
    }
    public void UpdateBonesOpacity(float value)
    {
        bonesOpacity = Mathf.Clamp01(value);
        SetOpacity(bonesRenderer, GetTSAdjustedOpacity(bonesOpacity));
    }
    public void UpdateVesselsOpacity(float value)
    {
        awVesselsOpacity = Mathf.Clamp01(value);
        float effectiveOpacity = GetTSAdjustedOpacity(awVesselsOpacity);
        SetOpacity(vesselsRenderer, effectiveOpacity);
        SetOpacity(airwaysRenderer, effectiveOpacity);
    }
    public void UpdateTSOpacity(float value)
    {
        tsMasterOpacity = Mathf.Clamp01(value);

        if (isTSVisible)
        {
            // Quando TS e' ON, lo slider TS agisce da master su tutti i distretti visibili.
            SetOpacity(lungRenderer, GetTSAdjustedOpacity(lungOpacity));
            SetOpacity(bonesRenderer, GetTSAdjustedOpacity(bonesOpacity));
            float awVesselsEffectiveOpacity = GetTSAdjustedOpacity(awVesselsOpacity);
            SetOpacity(vesselsRenderer, awVesselsEffectiveOpacity);
            SetOpacity(airwaysRenderer, awVesselsEffectiveOpacity);
        }

        foreach (Renderer rend in totalSegmentatorRenderers)
        {
            SetOpacity(rend, tsMasterOpacity);
        }
    }

    private float GetTSAdjustedOpacity(float baseOpacity)
    {
        return isTSVisible ? baseOpacity * tsMasterOpacity : baseOpacity;
    }
    // --- NUOVI TOGGLE (ON/OFF) ---
    public void ToggleSkin(bool isVisible)
    {
        if (skinRenderer) skinRenderer.enabled = isVisible;
        if (skinToggleText) skinToggleText.text = isVisible ? "Skin ON" : "Skin OFF";
    }
    public void ToggleLungs(bool isVisible)
    {
        if (lungRenderer) lungRenderer.enabled = isVisible;
        if (lungsToggleText) lungsToggleText.text = isVisible ? "Lungs ON" : "Lungs OFF";
    }
    public void ToggleBones(bool isVisible)
    {
        if (bonesRenderer) bonesRenderer.enabled = isVisible;
        if (bonesToggleText) bonesToggleText.text = isVisible ? "Bones ON" : "Bones OFF";
    }
    public void ToggleVessels(bool isVisible)
    {
        if (vesselsRenderer) vesselsRenderer.enabled = isVisible;
        if (airwaysRenderer) airwaysRenderer.enabled = isVisible;
        if (awVesselsToggleText) awVesselsToggleText.text = isVisible ? "AWVessels ON" : "AWVessels OFF";
    }
    public void ToggleTS(bool isVisible)
    {
        isTSVisible = isVisible;

        // Attiva/disattiva ossa, polmoni, vasi, vie aeree insieme ai segments non categorizzati
        if (bonesRenderer) bonesRenderer.enabled = isVisible;
        if (lungRenderer) lungRenderer.enabled = isVisible;
        if (vesselsRenderer) vesselsRenderer.enabled = isVisible;
        if (airwaysRenderer) airwaysRenderer.enabled = isVisible;
        foreach (Renderer rend in totalSegmentatorRenderers)
        {
            if (rend) rend.enabled = isVisible;
        }

        // Sincronizza subito le opacita' quando TS viene acceso/spento.
        SetOpacity(lungRenderer, GetTSAdjustedOpacity(lungOpacity));
        SetOpacity(bonesRenderer, GetTSAdjustedOpacity(bonesOpacity));
        float awVesselsEffectiveOpacity = GetTSAdjustedOpacity(awVesselsOpacity);
        SetOpacity(vesselsRenderer, awVesselsEffectiveOpacity);
        SetOpacity(airwaysRenderer, awVesselsEffectiveOpacity);

        if (isVisible)
        {
            foreach (Renderer rend in totalSegmentatorRenderers)
            {
                SetOpacity(rend, tsMasterOpacity);
            }
        }

        if (tsToggleText) tsToggleText.text = isVisible ? "TS ON" : "TS OFF";
    }
    public void ToggleNodule(bool isVisible)
    {
        if (noduleRenderer) noduleRenderer.enabled = isVisible;
    }
    public void ModifyModel()
    {
        if (EnsureModelRayGrabInteraction())
        {
            modelRayGrabInteractionInstance.SetActive(true);
            Debug.Log("[AnatomyManager] Modify Model: interazione attiva.");
        }
    }
    public void FixModel()
    {
        if (modelRayGrabInteractionInstance != null)
        {
            modelRayGrabInteractionInstance.SetActive(false);
        }
        Debug.Log("[AnatomyManager] Fix Model: modello bloccato.");
    }
    // --- MODIFICATO: GESTIONE DEL TOOL E DELLA SUA INTERAZIONE ---
    public void ToggleTool(bool isVisible)
    {
        if (toolRenderer)
        {
            toolRenderer.enabled = isVisible;
            // Gestione del sistema di presa (Grab)
            if (isVisible)
            {
                // Se non esiste ancora, lo istanziamo come figlio dell'ago
                if (activeRayGrabInteraction == null && rayGrabInteractionPrefab != null)
                {
                    activeRayGrabInteraction = Instantiate(rayGrabInteractionPrefab, toolRenderer.transform);
                    // Resettiamo posizione e rotazione per allinearlo perfettamente all'ago
                    activeRayGrabInteraction.transform.localPosition = Vector3.zero;
                    activeRayGrabInteraction.transform.localRotation = Quaternion.identity;
                }
                else if (activeRayGrabInteraction != null)
                {
                    // Se esiste giÃ , lo riattiviamo
                    activeRayGrabInteraction.SetActive(true);
                }
            }
            else
            {
                // Se spegniamo l'ago, disattiviamo anche il sistema di presa
                if (activeRayGrabInteraction != null)
                {
                    activeRayGrabInteraction.SetActive(false);
                }
            }
        }
        if (needlePathToggleText) needlePathToggleText.text = isVisible ? "Needle Path ON" : "Needle Path OFF";
    }
    // --- TOGGLE SISTEMA DI SLICING ---
    public void ToggleSliceSystem(bool isActive)
    {
        if (sliceSystemInstance != null)
        {
            sliceSystemInstance.SetActive(isActive);
            Debug.Log($"[AnatomyManager] Sistema di slicing: {(isActive ? "ATTIVO" : "DISATTIVO")}");
        }
        else
        {
            Debug.LogWarning("[AnatomyManager] Sistema di slicing non ancora inizializzato.");
        }
    }
    private bool EnsureModelRayGrabInteraction()
    {
        if (importedModelRoot == null)
        {
            Debug.LogWarning("[AnatomyManager] Modello non registrato: impossibile abilitare Modify Model.");
            return false;
        }
        // Assicura i requisiti minimi sul modello per il grabbing (wizard-like setup).
        Collider modelCollider = EnsureModelCollider(importedModelRoot);
        Rigidbody modelRigidbody = EnsureModelRigidbody(importedModelRoot);
        Grabbable modelGrabbable = EnsureComponent<Grabbable>(importedModelRoot);
        OneGrabTranslateTransformer oneGrabTranslate = EnsureComponent<OneGrabTranslateTransformer>(importedModelRoot);
        GrabFreeTransformer grabFreeTransformer = EnsureComponent<GrabFreeTransformer>(importedModelRoot);
        ConfigureGrabbable(modelGrabbable, modelRigidbody, importedModelRoot.transform, oneGrabTranslate, grabFreeTransformer);
        if (modelRayGrabInteractionInstance != null)
        {
            WireRayGrabComponents(modelRayGrabInteractionInstance, modelCollider, modelGrabbable);
            return true;
        }
        Transform existing = importedModelRoot.transform.Find("ISDK_RayGrabInteraction");
        if (existing != null)
        {
            modelRayGrabInteractionInstance = existing.gameObject;
            WireRayGrabComponents(modelRayGrabInteractionInstance, modelCollider, modelGrabbable);
            return true;
        }
        if (modelRayGrabInteractionPrefab == null)
        {
            Debug.LogWarning("[AnatomyManager] Assegna modelRayGrabInteractionPrefab per usare Modify/Fix Model.");
            return false;
        }
        modelRayGrabInteractionInstance = Instantiate(modelRayGrabInteractionPrefab, importedModelRoot.transform);
        modelRayGrabInteractionInstance.name = "ISDK_RayGrabInteraction";
        modelRayGrabInteractionInstance.transform.localPosition = Vector3.zero;
        modelRayGrabInteractionInstance.transform.localRotation = Quaternion.identity;
        modelRayGrabInteractionInstance.transform.localScale = Vector3.one;
        WireRayGrabComponents(modelRayGrabInteractionInstance, modelCollider, modelGrabbable);
        return true;
    }
    private Collider EnsureModelCollider(GameObject modelRoot)
    {
        Collider existing = modelRoot.GetComponent<Collider>();
        BoxCollider box = existing as BoxCollider;
        // Se c'e' un collider non-box gia' presente, lo riusiamo senza sovrascriverlo.
        if (existing != null && box == null)
        {
            return existing;
        }
        // Crea o aggiorna un BoxCollider basato sui renderer figli per rendere il root raycastabile.
        if (box == null)
        {
            box = modelRoot.AddComponent<BoxCollider>();
        }
        Bounds bounds = CalculateHierarchyBounds(modelRoot);
        Vector3 localCenter = modelRoot.transform.InverseTransformPoint(bounds.center);
        Vector3 lossy = modelRoot.transform.lossyScale;
        Vector3 absLossy = new Vector3(
            Mathf.Max(Mathf.Abs(lossy.x), 0.0001f),
            Mathf.Max(Mathf.Abs(lossy.y), 0.0001f),
            Mathf.Max(Mathf.Abs(lossy.z), 0.0001f)
        );
        // bounds.size e' in world-space, BoxCollider.size e' in local-space.
        Vector3 localSize = new Vector3(
            bounds.size.x / absLossy.x,
            bounds.size.y / absLossy.y,
            bounds.size.z / absLossy.z
        );
        box.center = localCenter;
        box.size = localSize;
        box.isTrigger = true;
        // Assegna il layer specifico al root del modello in modo che il laser possa ignorarlo
        modelRoot.layer = LayerMask.NameToLayer("GrabbableModel");
        return box;
    }
    private Rigidbody EnsureModelRigidbody(GameObject modelRoot)
    {
        Rigidbody rb = modelRoot.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = modelRoot.AddComponent<Rigidbody>();
        }
        rb.useGravity = false;
        rb.isKinematic = true;
        return rb;
    }
    private Bounds CalculateHierarchyBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return new Bounds(root.transform.position, Vector3.one * 0.1f);
        }
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        return bounds;
    }
    private T EnsureComponent<T>(GameObject target) where T : Component
    {
        T comp = target.GetComponent<T>();
        if (comp == null)
        {
            comp = target.AddComponent<T>();
        }
        return comp;
    }
    private void WireRayGrabComponents(GameObject rayGrabRoot, Collider modelCollider, Grabbable modelGrabbable)
    {
        RayInteractable rayInteractable = EnsureComponent<RayInteractable>(rayGrabRoot);
        MoveFromTargetProvider moveFromTargetProvider = EnsureComponent<MoveFromTargetProvider>(rayGrabRoot);
        ColliderSurface colliderSurface = EnsureComponent<ColliderSurface>(rayGrabRoot);
        ConfigureColliderSurface(colliderSurface, modelCollider);
        ConfigureRayInteractable(rayInteractable, modelGrabbable, colliderSurface, moveFromTargetProvider);
    }
    private void ConfigureGrabbable(Grabbable grabbable, Rigidbody rb, Transform targetTransform, OneGrabTranslateTransformer oneGrabTranslate, GrabFreeTransformer grabFreeTransformer)
    {
        if (grabbable == null)
        {
            return;
        }
        grabbable.InjectOptionalRigidbody(rb);
        grabbable.InjectOptionalTargetTransform(targetTransform);
        ITransformer oneGrab = oneGrabTranslate != null ? oneGrabTranslate : grabFreeTransformer;
        ITransformer twoGrab = grabFreeTransformer != null ? grabFreeTransformer : oneGrab;
        if (oneGrab != null)
        {
            grabbable.InjectOptionalOneGrabTransformer(oneGrab);
        }
        if (twoGrab != null)
        {
            grabbable.InjectOptionalTwoGrabTransformer(twoGrab);
        }
    }
    private void ConfigureColliderSurface(ColliderSurface colliderSurface, Collider modelCollider)
    {
        if (colliderSurface == null)
        {
            return;
        }
        colliderSurface.InjectCollider(modelCollider);
    }
    private void ConfigureRayInteractable(RayInteractable rayInteractable, Grabbable pointableElement, ColliderSurface surface, MoveFromTargetProvider movementProvider)
    {
        if (rayInteractable == null)
        {
            return;
        }
        if (surface != null)
        {
            rayInteractable.InjectSurface(surface);
        }
        if (pointableElement != null)
        {
            rayInteractable.InjectOptionalPointableElement(pointableElement);
        }
        if (movementProvider != null)
        {
            rayInteractable.InjectOptionalMovementProvider(movementProvider);
        }
    }
}