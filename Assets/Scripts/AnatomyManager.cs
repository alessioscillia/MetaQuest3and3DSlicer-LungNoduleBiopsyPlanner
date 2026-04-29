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
    [SerializeField] private Renderer arteriesRenderer;
    [SerializeField] private Renderer veinsRenderer;
    private System.Collections.Generic.List<Renderer> totalSegmentatorRenderers;
    
    [Header("Slice System")]
    [SerializeField] private GameObject sliceSystemInstance;
    
    [Header("Fixed Plane & UI Tracking")]
    [Tooltip("Inserisci qui il prefab del FixedImagePlane")]
    [SerializeField] private GameObject fixedImagePlanePrefab;
    [Tooltip("Il Canvas da seguire (es. quello dentro Menu_Biopsia_Container)")]
    [SerializeField] private Transform canvasTransform;
    [Tooltip("Distanza in metri a sinistra del Canvas")]
    [SerializeField] private float offsetLeft = 0.4f;
    private GameObject spawnedFixedPlane; // L'istanza creata a runtime

    // Variabili per il calcolo della scala proporzionale
    private Vector3 initialPlaneScale; 
    private float initialContainerScaleX = -1f; // Inizializzato a -1 per sicurezza

    [Header("Interaction")]
    [Tooltip("Inserisci qui il prefab ISDK_RayGrabInteraction da attaccare all'ago")]
    [SerializeField] private GameObject rayGrabInteractionPrefab;
    private GameObject activeRayGrabInteraction; 
    [Tooltip("Prefab ISDK_RayGrabInteraction da attaccare al modello importato (TotalSegmentatorModel)")]
    [SerializeField] private GameObject modelRayGrabInteractionPrefab;
    private GameObject importedModelRoot;
    private GameObject modelRayGrabInteractionInstance;
    
    [Header("Custom Settings")]
    [Tooltip("Trascina qui il Prefab del tuo GrabFreeTransformer configurato a mano (con i limiti min/max)")]
    [SerializeField] private GrabFreeTransformer customScaleTransformerPrefab;

    [Header("Toggle Texts")]
    [SerializeField] private Text skinToggleText;
    [SerializeField] private Text lungsToggleText;
    [SerializeField] private Text bonesToggleText;
    [SerializeField] private Text awVesselsToggleText;
    [SerializeField] private Text needlePathToggleText;
    [SerializeField] private Text tsToggleText;

    [Header("UI Sliders")]
    [SerializeField] private Slider lungsOpacitySlider;
    [SerializeField] private Slider bonesOpacitySlider;
    [SerializeField] private Slider awVesselsOpacitySlider;

    [Header("Button Visual States")]
    [Tooltip("Trascina qui i componenti Image dei rispettivi tasti")]
    [SerializeField] private Image modifyModelImage;
    [SerializeField] private Image fixModelImage;
    [SerializeField] private Image needleImage;
    
    [Tooltip("Immagine di sfondo del tasto di Tracking QR")]
    [SerializeField] private Image alignmentTrackingImage;

    [Tooltip("Colore dell'interno del tasto quando NON è selezionato")]
    [SerializeField] private Color normalButtonColor = new Color(0f, 0f, 0f, 0f); // Trasparente
    [Tooltip("Colore dell'interno del tasto quando E' selezionato")]
    [SerializeField] private Color selectedButtonColor = new Color(1f, 1f, 1f, 0.3f); // Bianco semitrasparente

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
        ToggleTool(false);
    }

    private void Start()
    {
        if (canvasTransform != null)
        {
            Transform menuContainer = canvasTransform.parent;
            initialContainerScaleX = menuContainer != null ? menuContainer.lossyScale.x : canvasTransform.lossyScale.x;
        }
        
        // All'avvio il tracking parte acceso, quindi accendiamo l'UI corrispondente
        UpdateTrackingButtonVisual(true);
    }

    private void Update()
    {
        if (spawnedFixedPlane != null && spawnedFixedPlane.activeInHierarchy && canvasTransform != null)
        {
            if (initialContainerScaleX < 0f)
            {
                Transform container = canvasTransform.parent;
                initialContainerScaleX = container != null ? container.lossyScale.x : canvasTransform.lossyScale.x;
            }

            Transform menuContainer = canvasTransform.parent;
            float currentContainerScaleX = menuContainer != null ? menuContainer.lossyScale.x : canvasTransform.lossyScale.x;
            
            float scaleRatio = (initialContainerScaleX > 0f) ? (currentContainerScaleX / initialContainerScaleX) : 1f;

            spawnedFixedPlane.transform.localScale = initialPlaneScale * scaleRatio;
            float currentOffsetLeft = offsetLeft * scaleRatio;
            spawnedFixedPlane.transform.position = canvasTransform.position - (canvasTransform.right * currentOffsetLeft);
            spawnedFixedPlane.transform.rotation = canvasTransform.rotation;
        }
    }

    public void RegisterOrganRenderer(string objName, Renderer rend)
    {
        string lowerName = objName.ToLower();
        
        if (lowerName.Contains("skin")) 
        {
            skinRenderer = rend;
            if (skinRenderer.gameObject.GetComponent<Collider>() == null)
            {
                MeshCollider mc = skinRenderer.gameObject.AddComponent<MeshCollider>();
                DisableFastMidphaseIfAvailable(mc);
                mc.convex = false; 
            }
            skinRenderer.gameObject.layer = LayerMask.NameToLayer("SkinLayer"); 
        }
        else if (lowerName.Contains("lung")) 
        {
            lungRenderer = rend;
            if (lungRenderer.gameObject.GetComponent<Collider>() == null)
            {
                MeshCollider mc = lungRenderer.gameObject.AddComponent<MeshCollider>();
                DisableFastMidphaseIfAvailable(mc);
                mc.convex = false; 
            }
            lungRenderer.gameObject.layer = LayerMask.NameToLayer("PleuraLayer"); 
        }
        else if (lowerName.Contains("bone") || lowerName.Contains("rib") || lowerName.Contains("vertebra")) 
        {
            bonesRenderer = rend;
            if (bonesRenderer.gameObject.GetComponent<Collider>() == null)
            {
                MeshCollider mc = bonesRenderer.gameObject.AddComponent<MeshCollider>();
                DisableFastMidphaseIfAvailable(mc);
                mc.convex = false; 
            }
            bonesRenderer.gameObject.layer = LayerMask.NameToLayer("Obstacle"); 
        }
        else if (lowerName.Contains("vessel")) 
        {
            vesselsRenderer = rend;
            if (vesselsRenderer.gameObject.GetComponent<Collider>() == null)
            {
                MeshCollider mc = vesselsRenderer.gameObject.AddComponent<MeshCollider>();
                DisableFastMidphaseIfAvailable(mc);
                mc.convex = false;
            }
            vesselsRenderer.gameObject.layer = LayerMask.NameToLayer("Obstacle");
        }
        else if (lowerName.Contains("pulmonaryarter")) 
        {
            arteriesRenderer = rend;
            if (arteriesRenderer.gameObject.GetComponent<Collider>() == null)
            {
                MeshCollider mc = arteriesRenderer.gameObject.AddComponent<MeshCollider>();
                DisableFastMidphaseIfAvailable(mc);
                mc.convex = false;
            }
            arteriesRenderer.gameObject.layer = LayerMask.NameToLayer("Obstacle");
        }
        else if (lowerName.Contains("pulmonaryvein")) 
        {
            veinsRenderer = rend;
            if (veinsRenderer.gameObject.GetComponent<Collider>() == null)
            {
                MeshCollider mc = veinsRenderer.gameObject.AddComponent<MeshCollider>();
                DisableFastMidphaseIfAvailable(mc);
                mc.convex = false;
            }
            veinsRenderer.gameObject.layer = LayerMask.NameToLayer("Obstacle");
        }
        else if (lowerName.Contains("airways") || lowerName.Contains("trachea")) 
        {
            airwaysRenderer = rend;
        }
        else if (lowerName.Contains("nodule"))
        {
            noduleRenderer = rend;
            if (noduleRenderer.gameObject.GetComponent<Collider>() == null)
            {
                MeshCollider mc = noduleRenderer.gameObject.AddComponent<MeshCollider>();
                DisableFastMidphaseIfAvailable(mc);
                mc.convex = false;
            }
            noduleRenderer.gameObject.layer = LayerMask.NameToLayer("Nodule");
        }
        else if (lowerName.Contains("tool"))
        {
            toolRenderer = rend;
            if (toolRenderer.gameObject.GetComponent<Collider>() == null)
            {
                MeshCollider mc = toolRenderer.gameObject.AddComponent<MeshCollider>();
                mc.convex = true; 
            }
            toolRenderer.enabled = false;
        }
        else
        {
            if (!totalSegmentatorRenderers.Contains(rend))
            {
                totalSegmentatorRenderers.Add(rend);
                rend.enabled = false; 
            }
        }
    }

    public void RegisterSliceSystem(GameObject sliceSystem)
    {
        sliceSystemInstance = sliceSystem;
    }

    private void DisableFastMidphaseIfAvailable(MeshCollider meshCollider)
    {
        if (meshCollider == null) return;
        var cookingOptionsProperty = typeof(MeshCollider).GetProperty("cookingOptions");
        if (cookingOptionsProperty == null || !cookingOptionsProperty.CanRead || !cookingOptionsProperty.CanWrite) return;
        try
        {
            object currentOptions = cookingOptionsProperty.GetValue(meshCollider, null);
            if (currentOptions == null) return;
            System.Type enumType = cookingOptionsProperty.PropertyType;
            if (!System.Enum.IsDefined(enumType, "UseFastMidphase")) return;
            object fastMidphaseValue = System.Enum.Parse(enumType, "UseFastMidphase");
            int currentFlags = System.Convert.ToInt32(currentOptions);
            int fastMidphaseFlag = System.Convert.ToInt32(fastMidphaseValue);
            object updatedFlags = System.Enum.ToObject(enumType, currentFlags & ~fastMidphaseFlag);
            cookingOptionsProperty.SetValue(meshCollider, updatedFlags, null);
        }
        catch { }
    }

    public void RegisterImportedModel(GameObject modelRoot)
    {
        importedModelRoot = modelRoot;
        EnsureModelRayGrabInteraction();
        FixModel();
    }
    
    // --- OPACITY SLIDERS ---
    private void SetOpacity(Renderer rend, float alphaVal)
    {
        if (rend != null && rend.material != null)
        {
            ConfigureMaterialSurfaceForAlpha(rend, alphaVal); 

            Color color = rend.material.color;
            color.a = alphaVal;
            rend.material.color = color;
            if (rend.material.HasProperty("_BaseColor")) rend.material.SetColor("_BaseColor", color);
            if (rend.material.HasProperty("_Color")) rend.material.SetColor("_Color", color);
        }
    }

    private void ConfigureMaterialSurfaceForAlpha(Renderer rend, float alphaVal)
    {
        Material mat = rend.material;
        if (mat == null) return;
        string lowerName = rend.gameObject.name.ToLower();
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1.0f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0.0f);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        if (lowerName.Contains("skin") || lowerName.Contains("body")) mat.renderQueue = 3000;
        else mat.renderQueue = 3001;
        mat.SetShaderPassEnabled("ShadowCaster", false);
    }
    
    public void UpdateSkinOpacity(float value) { skinOpacity = Mathf.Clamp01(value); SetOpacity(skinRenderer, skinOpacity); }
    public void UpdateLungOpacity(float value) { lungOpacity = Mathf.Clamp01(value); SetOpacity(lungRenderer, isTSVisible ? tsMasterOpacity : lungOpacity); }
    public void UpdateBonesOpacity(float value) { bonesOpacity = Mathf.Clamp01(value); SetOpacity(bonesRenderer, isTSVisible ? tsMasterOpacity : bonesOpacity); }
    public void UpdateVesselsOpacity(float value)
    {
        awVesselsOpacity = Mathf.Clamp01(value);
        float effectiveOpacity = isTSVisible ? tsMasterOpacity : awVesselsOpacity;
        SetOpacity(vesselsRenderer, effectiveOpacity);
        SetOpacity(arteriesRenderer, effectiveOpacity);
        SetOpacity(veinsRenderer, effectiveOpacity);    
        SetOpacity(airwaysRenderer, effectiveOpacity);
    }
    
    public void UpdateTSOpacity(float value)
    {
        tsMasterOpacity = Mathf.Clamp01(value);

        if (isTSVisible)
        {
            lungOpacity = tsMasterOpacity;
            bonesOpacity = tsMasterOpacity;
            awVesselsOpacity = tsMasterOpacity;

            if (lungsOpacitySlider != null) lungsOpacitySlider.value = lungOpacity;
            if (bonesOpacitySlider != null) bonesOpacitySlider.value = bonesOpacity;
            if (awVesselsOpacitySlider != null) awVesselsOpacitySlider.value = awVesselsOpacity;

            SetOpacity(lungRenderer, tsMasterOpacity);
            SetOpacity(bonesRenderer, tsMasterOpacity);
            SetOpacity(vesselsRenderer, tsMasterOpacity);
            SetOpacity(airwaysRenderer, tsMasterOpacity);
        }

        foreach (Renderer rend in totalSegmentatorRenderers) SetOpacity(rend, tsMasterOpacity);
    }
    
    public void ToggleTS(bool isVisible)
    {
        isTSVisible = isVisible;

        if (bonesRenderer) bonesRenderer.enabled = isVisible;
        if (lungRenderer) lungRenderer.enabled = isVisible;
        if (vesselsRenderer) vesselsRenderer.enabled = isVisible;
        if (arteriesRenderer) arteriesRenderer.enabled = isVisible; 
        if (veinsRenderer) veinsRenderer.enabled = isVisible;       
        if (airwaysRenderer) airwaysRenderer.enabled = isVisible;
        foreach (Renderer rend in totalSegmentatorRenderers) if (rend) rend.enabled = isVisible;

        if (isVisible) 
        {
            lungOpacity = tsMasterOpacity;
            bonesOpacity = tsMasterOpacity;
            awVesselsOpacity = tsMasterOpacity;

            if (lungsOpacitySlider != null) lungsOpacitySlider.value = lungOpacity;
            if (bonesOpacitySlider != null) bonesOpacitySlider.value = bonesOpacity;
            if (awVesselsOpacitySlider != null) awVesselsOpacitySlider.value = awVesselsOpacity;

            SetOpacity(lungRenderer, tsMasterOpacity);
            SetOpacity(bonesRenderer, tsMasterOpacity);
            SetOpacity(vesselsRenderer, tsMasterOpacity);
            SetOpacity(airwaysRenderer, tsMasterOpacity);
            SetOpacity(arteriesRenderer, tsMasterOpacity); 
            SetOpacity(veinsRenderer, tsMasterOpacity);    
            
            foreach (Renderer rend in totalSegmentatorRenderers) SetOpacity(rend, tsMasterOpacity);
        }

        if (tsToggleText) tsToggleText.text = isVisible ? "Total ON" : "Total OFF";
    }

    public void ToggleSkin(bool isVisible) { if (skinRenderer) skinRenderer.enabled = isVisible; if (skinToggleText) skinToggleText.text = isVisible ? "Skin ON" : "Skin OFF"; }
    public void ToggleLungs(bool isVisible) { if (lungRenderer) lungRenderer.enabled = isVisible; if (lungsToggleText) lungsToggleText.text = isVisible ? "Lungs ON" : "Lungs OFF"; }
    public void ToggleBones(bool isVisible) { if (bonesRenderer) bonesRenderer.enabled = isVisible; if (bonesToggleText) bonesToggleText.text = isVisible ? "Bones ON" : "Bones OFF"; }
    public void ToggleVessels(bool isVisible) 
    { 
        if (vesselsRenderer) vesselsRenderer.enabled = isVisible; 
        if (arteriesRenderer) arteriesRenderer.enabled = isVisible; 
        if (veinsRenderer) veinsRenderer.enabled = isVisible;       
        if (airwaysRenderer) airwaysRenderer.enabled = isVisible; 
        if (awVesselsToggleText) awVesselsToggleText.text = isVisible ? "AWVessels ON" : "AWVessels OFF"; 
    }
    
    public void ToggleNodule(bool isVisible) { if (noduleRenderer) noduleRenderer.enabled = isVisible; }
    
    public void ModifyModel() 
    { 
        if (EnsureModelRayGrabInteraction()) 
        { 
            modelRayGrabInteractionInstance.SetActive(true); 
            Debug.Log("[AnatomyManager] Modify Model: interazione attiva.");
            
            SetButtonVisualState(modifyModelImage, true);
            SetButtonVisualState(fixModelImage, false);
        }
    }
    public void FixModel() 
    { 
        if (modelRayGrabInteractionInstance != null) 
        { 
            modelRayGrabInteractionInstance.SetActive(false); 
        } 
        Debug.Log("[AnatomyManager] Fix Model: modello bloccato."); 
        
        SetButtonVisualState(modifyModelImage, false);
        SetButtonVisualState(fixModelImage, true);
    }
    
    public void ToggleTool(bool isVisible)
    {
        if (toolRenderer)
        {
            // Abilitiamo o disabilitiamo solo la visualizzazione del renderer
            toolRenderer.enabled = isVisible;
        }
        
        if (needlePathToggleText) 
            needlePathToggleText.text = isVisible ? "Needle Path ON" : "Needle Path OFF";
    }

    public void ToggleSliceSystem(bool isActive)
    {
        if (sliceSystemInstance != null)
        {
            sliceSystemInstance.SetActive(isActive);
        }
        if (isActive)
        {
            if (spawnedFixedPlane == null && fixedImagePlanePrefab != null)
            {
                spawnedFixedPlane = Instantiate(fixedImagePlanePrefab);
                
                initialPlaneScale = spawnedFixedPlane.transform.localScale;

                OpenIGTLinkConnect igtConnect = FindFirstObjectByType<OpenIGTLinkConnect>();
                if (igtConnect != null)
                {
                    Transform fixPlaneTransform = spawnedFixedPlane.transform.Find("FixPlane");
                    if (fixPlaneTransform != null)
                    {
                        igtConnect.fixPlane = fixPlaneTransform.gameObject;
                    }
                }
            }
            if (spawnedFixedPlane != null) spawnedFixedPlane.SetActive(true);
        }
        else
        {
            if (spawnedFixedPlane != null) spawnedFixedPlane.SetActive(false);
        }
    }

    private bool EnsureModelRayGrabInteraction()
    {
        if (importedModelRoot == null) return false;
        Collider modelCollider = EnsureModelCollider(importedModelRoot);
        Rigidbody modelRigidbody = EnsureModelRigidbody(importedModelRoot);
        Grabbable modelGrabbable = EnsureComponent<Grabbable>(importedModelRoot);
        
        GrabFreeTransformer activeTransformer;
        GrabFreeTransformer existingTransformer = importedModelRoot.GetComponentInChildren<GrabFreeTransformer>();
        
        if (existingTransformer != null && existingTransformer.gameObject != importedModelRoot)
        {
            activeTransformer = existingTransformer;
        }
        else if (customScaleTransformerPrefab != null)
        {
            activeTransformer = Instantiate(customScaleTransformerPrefab, importedModelRoot.transform);
        }
        else
        {
            activeTransformer = EnsureComponent<GrabFreeTransformer>(importedModelRoot);
        }
        
        ConfigureGrabbable(modelGrabbable, modelRigidbody, importedModelRoot.transform, activeTransformer, activeTransformer);
        
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
        if (modelRayGrabInteractionPrefab == null) return false;
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
        if (existing != null && box == null) return existing;
        if (box == null) box = modelRoot.AddComponent<BoxCollider>();
        Bounds bounds = CalculateHierarchyBounds(modelRoot);
        Vector3 localCenter = modelRoot.transform.InverseTransformPoint(bounds.center);
        Vector3 lossy = modelRoot.transform.lossyScale;
        Vector3 absLossy = new Vector3(Mathf.Max(Mathf.Abs(lossy.x), 0.0001f), Mathf.Max(Mathf.Abs(lossy.y), 0.0001f), Mathf.Max(Mathf.Abs(lossy.z), 0.0001f));
        Vector3 localSize = new Vector3(bounds.size.x / absLossy.x, bounds.size.y / absLossy.y, bounds.size.z / absLossy.z);
        box.center = localCenter;
        box.size = localSize;
        box.isTrigger = true;
        modelRoot.layer = LayerMask.NameToLayer("GrabbableModel");
        return box;
    }

    private Rigidbody EnsureModelRigidbody(GameObject modelRoot) { Rigidbody rb = modelRoot.GetComponent<Rigidbody>(); if (rb == null) rb = modelRoot.AddComponent<Rigidbody>(); rb.useGravity = false; rb.isKinematic = true; return rb; }
    private Bounds CalculateHierarchyBounds(GameObject root) { Renderer[] renderers = root.GetComponentsInChildren<Renderer>(); if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.one * 0.1f); Bounds bounds = renderers[0].bounds; for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds); return bounds; }
    private T EnsureComponent<T>(GameObject target) where T : Component { T comp = target.GetComponent<T>(); if (comp == null) comp = target.AddComponent<T>(); return comp; }
    
    private void WireRayGrabComponents(GameObject rayGrabRoot, Collider modelCollider, Grabbable modelGrabbable) { RayInteractable rayInteractable = EnsureComponent<RayInteractable>(rayGrabRoot); MoveFromTargetProvider moveFromTargetProvider = EnsureComponent<MoveFromTargetProvider>(rayGrabRoot); ColliderSurface colliderSurface = EnsureComponent<ColliderSurface>(rayGrabRoot); ConfigureColliderSurface(colliderSurface, modelCollider); ConfigureRayInteractable(rayInteractable, modelGrabbable, colliderSurface, moveFromTargetProvider); }
    
    private void ConfigureGrabbable(Grabbable grabbable, Rigidbody rb, Transform targetTransform, ITransformer oneGrab, ITransformer twoGrab) 
    { 
        if (grabbable == null) return; 
        grabbable.InjectOptionalRigidbody(rb); 
        grabbable.InjectOptionalTargetTransform(targetTransform); 
        
        if (oneGrab != null) grabbable.InjectOptionalOneGrabTransformer(oneGrab); 
        if (twoGrab != null) grabbable.InjectOptionalTwoGrabTransformer(twoGrab); 
    }
    
    private void ConfigureColliderSurface(ColliderSurface colliderSurface, Collider modelCollider) { if (colliderSurface == null) return; colliderSurface.InjectCollider(modelCollider); }
    private void ConfigureRayInteractable(RayInteractable rayInteractable, Grabbable pointableElement, ColliderSurface surface, MoveFromTargetProvider movementProvider) { if (rayInteractable == null) return; if (surface != null) rayInteractable.InjectSurface(surface); if (pointableElement != null) rayInteractable.InjectOptionalPointableElement(pointableElement); if (movementProvider != null) rayInteractable.InjectOptionalMovementProvider(movementProvider); }

    private void SetButtonVisualState(Image buttonImage, bool isActive)
    {
        if (buttonImage != null)
        {
            buttonImage.color = isActive ? selectedButtonColor : normalButtonColor;
        }
    }

    // --- SURGICAL ALIGNMENT CONTROLS ---
    
    /// <summary>
    /// Ora funziona come un Play/Pause. Chiama questo metodo da un bottone per 
    /// mettere in pausa o far ripartire il tracking in real-time.
    /// </summary>
    public void RestartAlignment()
    {
        if (SurgicalAlignment.Instance != null)
        {
            bool isNowTracking = SurgicalAlignment.Instance.ToggleTracking();
            UpdateTrackingButtonVisual(isNowTracking);
        }
        else
        {
            Debug.LogWarning("[AnatomyManager] SurgicalAlignment instance non trovata.");
        }
    }   

    // Aggiorna graficamente il colore del tasto
    public void UpdateTrackingButtonVisual(bool isTracking)
    {
        SetButtonVisualState(alignmentTrackingImage, isTracking);
    }
}