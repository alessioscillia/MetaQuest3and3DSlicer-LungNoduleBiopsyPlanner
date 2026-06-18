using UnityEngine;
using UnityEngine.UI;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;

public class AnatomyManager : MonoBehaviour
{
    // SINGLETON
    public static AnatomyManager Instance;
    
    [Header("Debug Info")]
    [SerializeField] private Renderer skinRenderer;
    [SerializeField] private Renderer lungRenderer;
    [SerializeField] private Renderer bonesRenderer;
    private System.Collections.Generic.List<Renderer> boneRenderers =
    new System.Collections.Generic.List<Renderer>();
    [SerializeField] private Renderer vesselsRenderer;
    private System.Collections.Generic.List<Renderer> airwayRenderers =
    new System.Collections.Generic.List<Renderer>();
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
    [SerializeField] private Slider skinOpacitySlider;
    [SerializeField] private Slider lungsOpacitySlider;
    [SerializeField] private Slider bonesOpacitySlider;
    [SerializeField] private Slider awVesselsOpacitySlider;

    [Header("Button Visual States")]
    [Tooltip("Trascina qui i componenti Image dei rispettivi tasti")]
    [SerializeField] private Image modifyModelImage;
    [SerializeField] private Image fixModelImage;
    [SerializeField] private Image needleImage;
    [SerializeField] private Image sphereImage;
    
    [Tooltip("Immagine di sfondo del tasto di Tracking QR")]
    [SerializeField] private Image alignmentTrackingImage;

    [Tooltip("Colore dell'interno del tasto quando NON è selezionato")]
    [SerializeField] private Color normalButtonColor = new Color(0f, 0f, 0f, 0f); // Trasparente
    [Tooltip("Colore dell'interno del tasto quando E' selezionato")]
    [SerializeField] private Color selectedButtonColor = new Color(1f, 1f, 1f, 0.3f); // Bianco semitrasparente

    [Header("Opacity State")]
    [Range(0f, 1f)] [SerializeField] private float skinOpacity = 0.2f;
    [Range(0f, 1f)] [SerializeField] private float lungOpacity = 1f;
    [Range(0f, 1f)] [SerializeField] private float bonesOpacity = 1f;
    [Range(0f, 1f)] [SerializeField] private float awVesselsOpacity = 1f;
    [Range(0f, 1f)] [SerializeField] private float tsMasterOpacity = 1f;

    [Header("Trajectory Confirmation")]
    [SerializeField] private Toggle trajectoryConfirmedToggle;

    private bool _suppressTrajectoryConfirmedToggleCallback = false;

    private bool isTSVisible;

    [Header("Laser Pointer System")]
    [SerializeField] private SurgicalLaserPointer laserPointer;
    [Tooltip("La telecamera principale (l'utente) per fargli spawnare il laser davanti")]
    [SerializeField] private Transform playerCamera;
    [SerializeField] private float spawnDistanceInFront = 0.5f;
    private bool isSphereHidden = false;

    [Header("Deviation Tracking")]
    [SerializeField] private PoseClient poseClient;
    [SerializeField] private TrajectoryDeviationCalculator deviationCalculator; // solo per UI text opz.
    [SerializeField] private Image deviationTrackingImage;
    private bool _isDeviationTrackingActive = false;
    
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
        
        UpdateTrackingButtonVisual(true);

        SetTrajectoryConfirmedToggleWithoutNotify(false);

        InitializeOpacitySliders();

        if (laserPointer != null)
        {
            laserPointer.HideSphere();
            laserPointer.gameObject.SetActive(false);
            isSphereHidden = true;
        }

        SetButtonVisualState(needleImage, false);
        SetButtonVisualState(sphereImage, false);
        SetButtonVisualState(deviationTrackingImage, false);
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
    private bool IsBoneName(string lowerName)
    {
        return lowerName.Contains("bone")
            || lowerName.Contains("bones")
            || lowerName.Contains("rib")
            || lowerName.Contains("ribs")
            || lowerName.Contains("vertebra")
            || lowerName.Contains("spine")
            || lowerName.Contains("sternum")
            || lowerName.Contains("clavicula")
            || lowerName.Contains("clavicle")
            || lowerName.Contains("scapula")
            || lowerName.Contains("shoulder");
    }

    private bool HasValidMesh(Renderer rend)
    {
        if (rend == null) return false;

        MeshFilter mf = rend.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return false;

        Mesh mesh = mf.sharedMesh;
        Vector3[] vertices = mesh.vertices;

        if (vertices == null || vertices.Length == 0) return false;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = vertices[i];

            if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z))
            {
                Debug.LogError(
                    $"[AnatomyManager] Mesh non valida su '{rend.gameObject.name}', " +
                    $"mesh='{mesh.name}', vertex index={i}, value={v}. Collider non creato."
                );
                return false;
            }
        }

        Bounds b = mesh.bounds;

        if (float.IsNaN(b.center.x) || float.IsNaN(b.center.y) || float.IsNaN(b.center.z) ||
            float.IsNaN(b.size.x) || float.IsNaN(b.size.y) || float.IsNaN(b.size.z))
        {
            Debug.LogError(
                $"[AnatomyManager] Bounds non validi su '{rend.gameObject.name}', mesh='{mesh.name}'."
            );
            return false;
        }

        return true;
    }

    private MeshCollider TryAddMeshCollider(Renderer rend, bool convex, string layerName)
    {
        if (rend == null) return null;

        if (!HasValidMesh(rend))
            return null;

        MeshFilter meshFilter = rend.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
            return null;

        string colliderName = rend.gameObject.name + "_Collider";

        Transform existingChild = rend.transform.Find(colliderName);
        if (existingChild != null)
        {
            MeshCollider existingCollider = existingChild.GetComponent<MeshCollider>();
            if (existingCollider != null)
                return existingCollider;
        }

        GameObject colliderObject = new GameObject(colliderName);
        colliderObject.transform.SetParent(rend.transform, false);
        colliderObject.transform.localPosition = Vector3.zero;
        colliderObject.transform.localRotation = Quaternion.identity;
        colliderObject.transform.localScale = Vector3.one;

        int layer = LayerMask.NameToLayer(layerName);
        if (layer >= 0)
            colliderObject.layer = layer;

        MeshCollider meshCollider = colliderObject.AddComponent<MeshCollider>();

        // Disattiva Fast Midphase prima di assegnare la mesh.
        meshCollider.cookingOptions =
            meshCollider.cookingOptions & ~MeshColliderCookingOptions.UseFastMidphase;

        meshCollider.sharedMesh = meshFilter.sharedMesh;
        meshCollider.convex = convex;

        return meshCollider;
    }

    public void RegisterOrganRenderer(string objName, Renderer rend)
    {
        string lowerName = objName.ToLower();
        
        if (lowerName.Contains("skin")) 
        {
            skinRenderer = rend;

            if (skinRenderer.gameObject.GetComponent<Collider>() == null)
            {
                TryAddMeshCollider(rend, false, "SkinLayer");
            }

            skinRenderer.gameObject.layer = LayerMask.NameToLayer("SkinLayer");

            /*
            * Applica subito l'opacità iniziale della pelle.
            * Di default skinOpacity = 0.2f, quindi non cambia l'aspetto iniziale.
            */
            SetOpacity(skinRenderer, skinOpacity);

            if (skinOpacitySlider != null)
                skinOpacitySlider.SetValueWithoutNotify(skinOpacity);
        }
        else if (lowerName.Contains("lung")) 
        {
            lungRenderer = rend;
            if (lungRenderer.gameObject.GetComponent<Collider>() == null)
            {
                TryAddMeshCollider(rend, false, "PleuraLayer");
            }
            lungRenderer.gameObject.layer = LayerMask.NameToLayer("PleuraLayer"); 
        }
        else if (IsBoneName(lowerName)) 
        {
            // Manteniamo bonesRenderer per compatibilità con il vecchio codice.
            if (bonesRenderer == null)
                bonesRenderer = rend;

            // Nuova gestione: più renderer ossei separati.
            if (!boneRenderers.Contains(rend))
                boneRenderers.Add(rend);

            if (rend.gameObject.GetComponent<Collider>() == null)
            {
                TryAddMeshCollider(rend, false, "Obstacle");
            }

            rend.gameObject.layer = LayerMask.NameToLayer("Obstacle"); 
        }
        else if (lowerName.Contains("vessel")) 
        {
            vesselsRenderer = rend;
            if (vesselsRenderer.gameObject.GetComponent<Collider>() == null)
            {
                TryAddMeshCollider(rend, false, "Obstacle");
            }
            vesselsRenderer.gameObject.layer = LayerMask.NameToLayer("Obstacle");
        }
        else if (lowerName.Contains("pulmonaryarter")) 
        {
            arteriesRenderer = rend;
            if (arteriesRenderer.gameObject.GetComponent<Collider>() == null)
            {
                TryAddMeshCollider(rend, false, "Obstacle");
            }
            arteriesRenderer.gameObject.layer = LayerMask.NameToLayer("Obstacle");
        }
        else if (lowerName.Contains("pulmonaryvein")) 
        {
            veinsRenderer = rend;
            if (veinsRenderer.gameObject.GetComponent<Collider>() == null)
            {
                TryAddMeshCollider(rend, false, "Obstacle");
                
            }
            veinsRenderer.gameObject.layer = LayerMask.NameToLayer("Obstacle");
        }
        else if (
            lowerName.Contains("airways") ||
            lowerName.Contains("airway") ||
            lowerName.Contains("trachea") ||
            lowerName.Contains("bronch"))
        {
            if (airwaysRenderer == null)
                airwaysRenderer = rend;

            if (!airwayRenderers.Contains(rend))
                airwayRenderers.Add(rend);

            if (rend.gameObject.GetComponent<Collider>() == null)
            {
                TryAddMeshCollider(rend, false, "Obstacle");
            }

            rend.gameObject.layer = LayerMask.NameToLayer("Obstacle");
        }
        else if (lowerName.Contains("nodule"))
        {
            noduleRenderer = rend;
            if (noduleRenderer.gameObject.GetComponent<Collider>() == null)
            {
                TryAddMeshCollider(rend, false, "Nodule");
            }
            noduleRenderer.gameObject.layer = LayerMask.NameToLayer("Nodule");
        }
        else if (lowerName.Contains("tool"))
        {
            toolRenderer = rend;
            if (toolRenderer.gameObject.GetComponent<Collider>() == null)
            {
                TryAddMeshCollider(rend, true, "ToolLayer");
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
    private void InitializeOpacitySliders()
    {
        /*
        * Usiamo SetValueWithoutNotify per evitare che Unity chiami subito
        * gli eventi OnValueChanged prima che il modello sia stato importato.
        *
        * La pelle parte da skinOpacity = 0.2f, quindi l'aspetto iniziale
        * rimane uguale a quello attuale.
        */

        if (skinOpacitySlider != null)
            skinOpacitySlider.SetValueWithoutNotify(skinOpacity);

        if (lungsOpacitySlider != null)
            lungsOpacitySlider.SetValueWithoutNotify(lungOpacity);

        if (bonesOpacitySlider != null)
            bonesOpacitySlider.SetValueWithoutNotify(bonesOpacity);

        if (awVesselsOpacitySlider != null)
            awVesselsOpacitySlider.SetValueWithoutNotify(awVesselsOpacity);
    }
    private void SetOpacity(Renderer rend, float alphaVal)
    {
        if (rend != null && rend.material != null)
        {
            Material mat = rend.material;

            alphaVal = Mathf.Clamp01(alphaVal);

            // Se alpha ~ 1, renderizza come opaco.
            // Se alpha < 1, renderizza come trasparente.
            bool renderedOpaque = ConfigureMaterialSurfaceForAlpha(rend, alphaVal);

            string colorProp = "";
            if (mat.HasProperty("baseColorFactor")) colorProp = "baseColorFactor";
            else if (mat.HasProperty("_BaseColor")) colorProp = "_BaseColor";
            else if (mat.HasProperty("_Color")) colorProp = "_Color";

            if (colorProp != "")
            {
                Color currentColor = mat.GetColor(colorProp);

                // Se il materiale è opaco, forziamo alpha a 1.
                // Se è trasparente, usiamo il valore dello slider.
                currentColor.a = renderedOpaque ? 1f : alphaVal;

                mat.SetColor(colorProp, currentColor);
            }
        }
    }

    private bool ConfigureMaterialSurfaceForAlpha(Renderer rend, float alphaVal)
    {
        Material mat = rend.material;
        if (mat == null) return false;

        alphaVal = Mathf.Clamp01(alphaVal);

        /*
        * Quando alpha è praticamente 1, conviene usare rendering opaco.
        * Questo evita problemi di sovrapposizione/disegno errato,
        * soprattutto per ossa, coste, sterno e colonna.
        */
        bool useOpaqueRendering = alphaVal >= 0.98f;

        if (useOpaqueRendering)
        {
            ConfigureMaterialOpaque(mat);
            return true;
        }
        else
        {
            ConfigureMaterialTransparent(mat, rend);
            return false;
        }
    }
    private void ConfigureMaterialOpaque(Material mat)
    {
        if (mat == null) return;

        if (mat.HasProperty("_Surface"))
            mat.SetFloat("_Surface", 0.0f);

        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        mat.SetInt("_ZWrite", 1);

        mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");

        mat.SetOverrideTag("RenderType", "Opaque");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;

        mat.SetShaderPassEnabled("ShadowCaster", true);
    }

    private void ConfigureMaterialTransparent(Material mat, Renderer rend)
    {
        if (mat == null) return;

        string lowerName = rend.gameObject.name.ToLower();

        if (mat.HasProperty("_Surface"))
            mat.SetFloat("_Surface", 1.0f);

        if (mat.HasProperty("_Blend"))
            mat.SetFloat("_Blend", 0.0f);

        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);

        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");

        mat.SetOverrideTag("RenderType", "Transparent");

        if (lowerName.Contains("skin") || lowerName.Contains("body"))
            mat.renderQueue = 3000;
        else
            mat.renderQueue = 3001;

        mat.SetShaderPassEnabled("ShadowCaster", false);
    }

    private void SetBoneRenderersEnabled(bool isVisible)
    {
        bool any = false;

        foreach (Renderer rend in boneRenderers)
        {
            if (rend == null) continue;

            rend.enabled = isVisible;
            any = true;
        }

        // Fallback per vecchi modelli con un solo renderer Bones.
        if (!any && bonesRenderer != null)
            bonesRenderer.enabled = isVisible;
    }

    private void SetBoneRenderersOpacity(float opacity)
    {
        bool any = false;

        foreach (Renderer rend in boneRenderers)
        {
            if (rend == null) continue;

            SetOpacity(rend, opacity);
            any = true;
        }

        // Fallback per vecchi modelli con un solo renderer Bones.
        if (!any && bonesRenderer != null)
            SetOpacity(bonesRenderer, opacity);
    }
    private void SetAirwayRenderersEnabled(bool isVisible)
    {
        bool any = false;

        foreach (Renderer rend in airwayRenderers)
        {
            if (rend == null) continue;
            rend.enabled = isVisible;
            any = true;
        }

        if (!any && airwaysRenderer != null)
            airwaysRenderer.enabled = isVisible;
    }

    private void SetAirwayRenderersOpacity(float opacity)
    {
        bool any = false;

        foreach (Renderer rend in airwayRenderers)
        {
            if (rend == null) continue;
            SetOpacity(rend, opacity);
            any = true;
        }

        if (!any && airwaysRenderer != null)
            SetOpacity(airwaysRenderer, opacity);
    }
    
    public void UpdateSkinOpacity(float value)
    {
        skinOpacity = Mathf.Clamp01(value);

        if (skinRenderer != null)
            SetOpacity(skinRenderer, skinOpacity);
    }
    public void UpdateLungOpacity(float value) { lungOpacity = Mathf.Clamp01(value); SetOpacity(lungRenderer, isTSVisible ? tsMasterOpacity : lungOpacity); }
    public void UpdateBonesOpacity(float value) 
    { 
        bonesOpacity = Mathf.Clamp01(value);

        float effectiveOpacity = isTSVisible ? tsMasterOpacity : bonesOpacity;

        SetBoneRenderersOpacity(effectiveOpacity);
    }
    public void UpdateVesselsOpacity(float value)
    {
        awVesselsOpacity = Mathf.Clamp01(value);
        float effectiveOpacity = isTSVisible ? tsMasterOpacity : awVesselsOpacity;
        SetOpacity(vesselsRenderer, effectiveOpacity);
        SetOpacity(arteriesRenderer, effectiveOpacity);
        SetOpacity(veinsRenderer, effectiveOpacity);    
        SetAirwayRenderersOpacity(effectiveOpacity);
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
            SetBoneRenderersOpacity(tsMasterOpacity);
            SetOpacity(vesselsRenderer, tsMasterOpacity);
            SetAirwayRenderersOpacity(tsMasterOpacity);
            SetOpacity(arteriesRenderer, tsMasterOpacity);
            SetOpacity(veinsRenderer, tsMasterOpacity);
        }

        foreach (Renderer rend in totalSegmentatorRenderers) SetOpacity(rend, tsMasterOpacity);
    }
    
    public void ToggleTS(bool isVisible)
    {
        isTSVisible = isVisible;

        SetBoneRenderersEnabled(isVisible);
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
            SetBoneRenderersOpacity(tsMasterOpacity);
            SetOpacity(vesselsRenderer, tsMasterOpacity);
            SetAirwayRenderersOpacity(tsMasterOpacity);
            SetOpacity(arteriesRenderer, tsMasterOpacity); 
            SetOpacity(veinsRenderer, tsMasterOpacity);    
            
            foreach (Renderer rend in totalSegmentatorRenderers) SetOpacity(rend, tsMasterOpacity);
        }

        if (tsToggleText) tsToggleText.text = isVisible ? "Total ON" : "Total OFF";
    }

    public void ToggleSkin(bool isVisible) 
    { 
        if (skinRenderer != null)
        {
            skinRenderer.enabled = isVisible;

            if (isVisible)
                SetOpacity(skinRenderer, skinOpacity);
        }

        if (skinToggleText != null)
            skinToggleText.text = isVisible ? "Skin ON" : "Skin OFF"; 
    }
    
    public void ToggleLungs(bool isVisible) 
    { 
        if (lungRenderer) lungRenderer.enabled = isVisible; 
        if (isVisible) SetOpacity(lungRenderer, isTSVisible ? tsMasterOpacity : lungOpacity);
        if (lungsToggleText) lungsToggleText.text = isVisible ? "Lungs ON" : "Lungs OFF"; 
    }
    
    public void ToggleBones(bool isVisible) 
    { 
        SetBoneRenderersEnabled(isVisible);

        if (isVisible)
        {
            float effectiveOpacity = isTSVisible ? tsMasterOpacity : bonesOpacity;
            SetBoneRenderersOpacity(effectiveOpacity);
        }

        if (bonesToggleText)
            bonesToggleText.text = isVisible ? "Bones ON" : "Bones OFF"; 
    }
    
    public void ToggleVessels(bool isVisible) 
    { 
        if (vesselsRenderer) vesselsRenderer.enabled = isVisible; 
        if (arteriesRenderer) arteriesRenderer.enabled = isVisible; 
        if (veinsRenderer) veinsRenderer.enabled = isVisible;       
        SetAirwayRenderersEnabled(isVisible); 
        
        // AGGIUNTO: Quando accendi i vasi, applica subito l'opacità e la configurazione corretta
        if (isVisible)
        {
            float effectiveOpacity = isTSVisible ? tsMasterOpacity : awVesselsOpacity;
            SetOpacity(vesselsRenderer, effectiveOpacity);
            SetOpacity(arteriesRenderer, effectiveOpacity);
            SetOpacity(veinsRenderer, effectiveOpacity);
            SetAirwayRenderersOpacity(effectiveOpacity);
        }

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
            toolRenderer.enabled = isVisible;
        }
        if (needlePathToggleText) needlePathToggleText.text = isVisible ? "Needle Path ON" : "Needle Path OFF";
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
    private const float MaxReasonableModelSizeMeters = 10f;
    private const float MaxReasonableDistanceFromOriginMeters = 100f;

    private Bounds CalculateHierarchyBounds(GameObject root)
    {
        /*
        * Questo bounds serve solo per il BoxCollider globale del modello,
        * usato per Modify/Fix Model.
        *
        * Per questo motivo è molto meglio usare la skin, se disponibile,
        * invece di includere tutti i renderer interni come Airways, vasi, ecc.
        */

        if (skinRenderer != null)
        {
            MeshFilter skinMeshFilter = skinRenderer.GetComponent<MeshFilter>();

            if (TryGetWorldBoundsFromMeshFilter(
                    skinMeshFilter,
                    out Bounds skinBounds,
                    "skinRenderer"))
            {
                return skinBounds;
            }
        }

        /*
        * Fallback robusto:
        * calcola i bounds dai MeshFilter, non dai Renderer.bounds,
        * così evitiamo l'errore Invalid worldAABB.
        */

        MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);

        bool hasValidBounds = false;
        Bounds combinedBounds = new Bounds(root.transform.position, Vector3.zero);

        foreach (MeshFilter meshFilter in meshFilters)
        {
            if (!TryGetWorldBoundsFromMeshFilter(
                    meshFilter,
                    out Bounds currentBounds,
                    meshFilter != null ? meshFilter.gameObject.name : "NULL"))
            {
                continue;
            }

            if (!hasValidBounds)
            {
                combinedBounds = currentBounds;
                hasValidBounds = true;
            }
            else
            {
                combinedBounds.Encapsulate(currentBounds);
            }
        }

        if (!hasValidBounds)
        {
            Debug.LogWarning("[AnatomyManager] Nessun bounds valido trovato. Uso bounds di fallback.");
            return new Bounds(root.transform.position, Vector3.one * 0.2f);
        }

        return combinedBounds;
    }

    private bool TryGetWorldBoundsFromMeshFilter(
        MeshFilter meshFilter,
        out Bounds worldBounds,
        string debugName)
    {
        worldBounds = default;

        if (meshFilter == null || meshFilter.sharedMesh == null)
            return false;

        Mesh mesh = meshFilter.sharedMesh;

        if (mesh.vertexCount == 0)
            return false;

        Bounds localBounds = mesh.bounds;

        if (!IsFiniteVector(localBounds.center) || !IsFiniteVector(localBounds.size))
        {
            Debug.LogWarning($"[AnatomyManager] Bounds locali non validi su '{debugName}'. Mesh ignorata.");
            return false;
        }

        Vector3 min = localBounds.min;
        Vector3 max = localBounds.max;

        Vector3[] corners = new Vector3[8]
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(min.x, max.y, min.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(min.x, min.y, max.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(min.x, max.y, max.z),
            new Vector3(max.x, max.y, max.z)
        };

        bool initialized = false;

        foreach (Vector3 corner in corners)
        {
            Vector3 worldCorner = meshFilter.transform.TransformPoint(corner);

            if (!IsFiniteVector(worldCorner))
            {
                Debug.LogWarning($"[AnatomyManager] Vertice bounds non valido su '{debugName}'. Mesh ignorata.");
                return false;
            }

            if (!initialized)
            {
                worldBounds = new Bounds(worldCorner, Vector3.zero);
                initialized = true;
            }
            else
            {
                worldBounds.Encapsulate(worldCorner);
            }
        }

        if (!IsReasonableWorldBounds(worldBounds))
        {
            Debug.LogWarning(
                $"[AnatomyManager] Bounds sospetti su '{debugName}'. " +
                $"Center={worldBounds.center}, Size={worldBounds.size}. Mesh ignorata dal collider globale."
            );

            return false;
        }

        return true;
    }

    private bool IsFiniteVector(Vector3 v)
    {
        return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    }

    private bool IsReasonableWorldBounds(Bounds bounds)
    {
        if (!IsFiniteVector(bounds.center) || !IsFiniteVector(bounds.size))
            return false;

        if (bounds.size.x > MaxReasonableModelSizeMeters ||
            bounds.size.y > MaxReasonableModelSizeMeters ||
            bounds.size.z > MaxReasonableModelSizeMeters)
            return false;

        if (Mathf.Abs(bounds.center.x) > MaxReasonableDistanceFromOriginMeters ||
            Mathf.Abs(bounds.center.y) > MaxReasonableDistanceFromOriginMeters ||
            Mathf.Abs(bounds.center.z) > MaxReasonableDistanceFromOriginMeters)
            return false;

        return true;
    }
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

    public void UpdateTrackingButtonVisual(bool isTracking)
    {
        SetButtonVisualState(alignmentTrackingImage, isTracking);
    }

    // --- LASER POINTER CONTROLS ---

    private void SpawnLaserInFrontOfUser()
    {
        if (laserPointer == null || playerCamera == null) return;

        laserPointer.transform.position =
            playerCamera.position + (playerCamera.forward * spawnDistanceInFront);

        laserPointer.transform.rotation =
            Quaternion.LookRotation(playerCamera.forward);
    }

    private void SetSphereVisible(bool visible)
    {
        if (laserPointer == null) return;

        if (visible)
        {
            laserPointer.ShowSphere();
            isSphereHidden = false;
        }
        else
        {
            laserPointer.HideSphere();
            isSphereHidden = true;
        }

        SetButtonVisualState(sphereImage, visible);
    }

    public void OnSpawnLaserClicked()
    {
        if (laserPointer == null || playerCamera == null) return;

        laserPointer.gameObject.SetActive(true);
        SpawnLaserInFrontOfUser();

        // Quando accendo il laser, la sfera deve comparire.
        SetSphereVisible(true);

        SetButtonVisualState(needleImage, true);
    }

    public void OnToggleNeedleClicked()
    {
        if (laserPointer == null || playerCamera == null) return;

        if (!laserPointer.gameObject.activeSelf)
        {
            laserPointer.ClearConfirmedTrajectory();
            SetTrajectoryConfirmedToggleWithoutNotify(false);

            laserPointer.gameObject.SetActive(true);
            SpawnLaserInFrontOfUser();

            SetSphereVisible(true);

            SetButtonVisualState(needleImage, true);
        }
        else
        {
            laserPointer.ClearConfirmedTrajectory();
            SetTrajectoryConfirmedToggleWithoutNotify(false);

            SetSphereVisible(false);

            laserPointer.gameObject.SetActive(false);

            SetButtonVisualState(needleImage, false);
        }
    }

    public void OnToggleSphereClicked()
    {
        if (laserPointer == null) return;

        // Se il laser è spento, Sphere non fa nulla.
        if (!laserPointer.gameObject.activeSelf)
            return;

        if (isSphereHidden)
        {
            SetSphereVisible(true);
        }
        else
        {
            SetSphereVisible(false);
        }
    }
    private void SetTrajectoryConfirmedToggleWithoutNotify(bool isOn)
    {
        _suppressTrajectoryConfirmedToggleCallback = true;

        if (trajectoryConfirmedToggle != null)
            trajectoryConfirmedToggle.SetIsOnWithoutNotify(isOn);

        _suppressTrajectoryConfirmedToggleCallback = false;
    }

    public void OnTrajectoryConfirmedToggleChanged(bool isOn)
    {
        if (_suppressTrajectoryConfirmedToggleCallback)
            return;

        if (laserPointer == null)
        {
            SetTrajectoryConfirmedToggleWithoutNotify(false);
            return;
        }

        if (isOn)
        {
            /*
            * L'utente sta spuntando "Trajectory Confirmed".
            * Confermiamo solo se la traiettoria corrente è valida.
            */
            bool confirmed = laserPointer.ConfirmCurrentTrajectory();

            if (!confirmed)
            {
                /*
                * Se il laser non sta colpendo correttamente il nodulo,
                * il toggle torna automaticamente OFF.
                */
                SetTrajectoryConfirmedToggleWithoutNotify(false);
            }
        }
        else
        {
            /*
            * L'utente ha tolto la spunta.
            * Rimuoviamo il marker SkinEntryPoint e facciamo tornare il mirino.
            */
            laserPointer.ClearConfirmedTrajectory();
        }
    }

    public void OnToggleDeviationTrackingClicked()
    {
        _isDeviationTrackingActive = !_isDeviationTrackingActive;

        if (poseClient != null)
            poseClient.SetTrackingEnabled(_isDeviationTrackingActive);

        SetButtonVisualState(deviationTrackingImage, _isDeviationTrackingActive);
    }
}