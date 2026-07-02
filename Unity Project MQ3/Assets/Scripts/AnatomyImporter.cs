using UnityEngine;
using GLTFast;
using System;
using System.Threading.Tasks;

public class AnatomyImporter : MonoBehaviour
{
    [Header("Model URLs")]
    [Tooltip("Modello completo usato in Unity Editor / PC.")]
    [SerializeField] private string editorModelUrl = "http://127.0.0.1:8080/model.glb";

    [Tooltip("Modello alleggerito usato su Meta Quest / Android.")]
    [SerializeField] private string questModelUrl = "http://127.0.0.1:8080//model_quest.glb";

    [Header("Loading Settings")]
    public int maxLoadAttempts = 5;
    public float retryDelaySeconds = 0.75f;

    [Header("Slicer / Slice System")]
    [SerializeField] private GameObject slicerPrefab;

    private Renderer skinRenderer;
    private GameObject lungObject;
    private bool isLoadingModel;

    private string RuntimeModelUrl
    {
        get
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return questModelUrl;
#else
            return editorModelUrl;
#endif
        }
    }

    async void Start()
    {
        InitializeCamera();

        string selectedModelUrl = RuntimeModelUrl;

#if UNITY_ANDROID && !UNITY_EDITOR
        Debug.Log($"[AnatomyImporter] Running on Android/Quest. Loading QUEST model: {selectedModelUrl}");
#else
        Debug.Log($"[AnatomyImporter] Running in Unity Editor/PC. Loading EDITOR model: {selectedModelUrl}");
#endif

        await LoadGltfFromUrl(selectedModelUrl);
    }
    
    void InitializeCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            // Set a transparent background for PassThrough mode
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = new Color(0, 0, 0, 0);
        }
        else
        {
            Debug.LogWarning("No MainCamera found. Check that the Camera Rig is active.");
        }
    }
    
    async Task LoadGltfFromUrl(string url)
    {
        if (isLoadingModel)
        {
            Debug.LogWarning("[AnatomyImporter] Loading already in progress, request ignored.");
            return;
        }

        isLoadingModel = true;
        // --- ANTI-CACHING MODIFICATION ---
        string noCacheUrl = url + "?t=" + System.DateTime.Now.Ticks;
        // ---------------------------
        try
        {
            GameObject modelContainer = new GameObject("TotalSegmentatorModel");
            modelContainer.transform.SetParent(null); // Assicura che sia nella root della scena

            GltfImport gltf = null;
            bool success = false;

            for (int attempt = 1; attempt <= Mathf.Max(1, maxLoadAttempts); attempt++)
            {
                try
                {
                    gltf = new GltfImport();
                    success = await gltf.Load(noCacheUrl);
                    if (success)
                    {
                        break;
                    }

                    Debug.LogWarning($"[AnatomyImporter] Tentativo {attempt}/{maxLoadAttempts} fallito durante Load('{url}').");
                }
                catch (InvalidOperationException ex)
                {
                    Debug.LogWarning($"[AnatomyImporter] Tentativo {attempt}/{maxLoadAttempts} eccezione transiente: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AnatomyImporter] Tentativo {attempt}/{maxLoadAttempts} eccezione durante Load: {ex.Message}");
                }

                if (attempt < Mathf.Max(1, maxLoadAttempts))
                {
                    float delay = retryDelaySeconds * attempt;
                    await Task.Delay(TimeSpan.FromSeconds(delay));
                }
            }

            if (!success || gltf == null)
            {
                Destroy(modelContainer);
                Debug.LogError("Errore nel caricamento del glTF dopo multipli tentativi. Verifica server localhost/export file.");
                return;
            }

            await gltf.InstantiateMainSceneAsync(modelContainer.transform);
            // 1. Assegna materiali
            AutomateMaterialSetup(modelContainer);
            

            // Nodo intermedio per la conversione visiva RAS->Unity.
            // Il root del modello deve restare con scala positiva per non rompere grab/collider.
            GameObject flipNode = new GameObject("SlicerToUnity_XZFlip");
            flipNode.transform.SetParent(modelContainer.transform, false);
            flipNode.transform.localScale    = new Vector3(-1f, 1f, -1f);
            flipNode.transform.localPosition = Vector3.zero;
            flipNode.transform.localRotation = Quaternion.identity;

            var children = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in modelContainer.transform)
            {
                if (child.gameObject != flipNode)
                    children.Add(child);
            }
            foreach (var child in children)
                child.SetParent(flipNode.transform, false);
            // -----------------------------------------------------

            modelContainer.transform.localScale    = new Vector3(0.001f, 0.001f, 0.001f);
            modelContainer.transform.localRotation = Quaternion.identity;
            modelContainer.transform.position      = new Vector3(0f, 1f, 1f);


            if (AnatomyManager.Instance != null)
            {
                AnatomyManager.Instance.RegisterImportedModel(modelContainer);
            }
            else
            {
                Debug.LogWarning("[AnatomyImporter] AnatomyManager non trovato: Modify/Fix Model non disponibili.");
            }
            
            // 3. Inizializza lo slicer basandosi sulla geometria caricata
            InitializeSliceSystem(modelContainer);

            // 4. PASSA IL MODELLO A SURGICAL ALIGNMENT
            if (SurgicalAlignment.Instance != null)
            {
                SurgicalAlignment.Instance.SetHologram(modelContainer);
            }
            else
            {
                Debug.LogWarning("[AnatomyImporter] Modello caricato, ma SurgicalAlignment non trovato nella scena.");
            }
        }
        finally
        {
            isLoadingModel = false;
        }
    }
    
    void InitializeSliceSystem(GameObject modelContainer)
    {
        if (skinRenderer == null) return;

        Bounds skinBounds = skinRenderer.bounds;

        if (slicerPrefab != null)
        {
            Vector3 worldCenter = skinBounds.center;

            GameObject slicerInstance = Instantiate(
                slicerPrefab,
                worldCenter,
                Quaternion.identity
            );

            slicerInstance.SetActive(false);
            slicerInstance.transform.SetParent(modelContainer.transform, true);
            slicerInstance.transform.localRotation = Quaternion.identity;

            if (AnatomyManager.Instance != null)
            {
                AnatomyManager.Instance.RegisterSliceSystem(slicerInstance);
            }

            SliceInteractionController controller =
                slicerInstance.GetComponentInChildren<SliceInteractionController>();

            if (controller != null)
            {
                Vector3 min = skinBounds.min;
                Vector3 max = skinBounds.max;

                Vector3[] boundsCorners = new Vector3[8]
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

                float actualMinZ = float.MaxValue;
                float actualMaxZ = float.MinValue;

                foreach (Vector3 corner in boundsCorners)
                {
                    float localZ =
                        slicerInstance.transform.InverseTransformPoint(corner).z;

                    if (localZ < actualMinZ) actualMinZ = localZ;
                    if (localZ > actualMaxZ) actualMaxZ = localZ;
                }

                controller.InitializeConstraints(actualMinZ, actualMaxZ);

                OpenIGTLinkConnect igtLink =
                    FindFirstObjectByType<OpenIGTLinkConnect>();

                if (igtLink != null &&
                    controller.visualClippingPlane != null)
                {
                    igtLink.RegisterDynamicModel(
                        controller.visualClippingPlane.gameObject,
                        "UnityReslicePlane"
                    );

                    igtLink.SetMovingPlane(
                        controller.visualClippingPlane.gameObject
                    );
                }
            }
        }
        else
        {
            Debug.LogError(
                "[AnatomyImporter] Slicer Prefab non assegnato nell'Inspector."
            );
        }
    }

    private bool IsBoneName(string lower)
    {
        return lower.Contains("bone")
            || lower.Contains("bones")
            || lower.Contains("rib")
            || lower.Contains("ribs")
            || lower.Contains("vertebra")
            || lower.Contains("spine")
            || lower.Contains("sternum")
            || lower.Contains("clavicula")
            || lower.Contains("clavicle")
            || lower.Contains("scapula")
            || lower.Contains("shoulder");
    }
    
    void AutomateMaterialSetup(GameObject loadedModel)
    {
        MeshRenderer[] renderers = loadedModel.GetComponentsInChildren<MeshRenderer>();

        foreach (MeshRenderer rend in renderers)
        {
            string objName = rend.gameObject.name;
            string lower = objName.ToLowerInvariant();
            MeshFilter mf = rend.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                Debug.Log(
                    $"[AnatomyImporter] Renderer object='{objName}', " +
                    $"mesh='{mf.sharedMesh.name}', vertices={mf.sharedMesh.vertexCount}"
                );
            }

            Material mat = null;
            if (rend.sharedMaterial != null)
            {
                mat = new Material(rend.sharedMaterial);
            }
            else
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            }

            bool hasImportedColor = TryGetMaterialColor(mat, out Color importedColor);

            bool isTransparent = false;
            bool startHidden = false;

            Color fallbackColor = Color.white;

            if (lower.Contains("skin") || lower.Contains("body"))
            {
                isTransparent = true;
                fallbackColor = new Color(1f, 0.78f, 0.58f, 0.2f);
                skinRenderer = rend;
            }
            else if (lower.Contains("lung"))
            {
                isTransparent = false;
                fallbackColor = new Color(1f, 0.4f, 0.4f, 1f);
                lungObject = rend.gameObject;
            }
            else if (IsBoneName(lower))
            {
            #if UNITY_ANDROID && !UNITY_EDITOR
                // Su Meta Quest le ossa devono partire opache:
                // molto più leggero rispetto al rendering trasparente.
                isTransparent = false;
                fallbackColor = new Color(0.85f, 0.80f, 0.68f, 1.0f);
            #else
                // In Editor puoi tenerle opache o cambiarle a true se vuoi testare trasparenze.
                isTransparent = false;
                fallbackColor = new Color(0.9f, 0.9f, 0.8f, 1.0f);
            #endif

                startHidden = true;
            }
            
            else if (lower.Contains("pulmonaryarter"))
            {
            #if UNITY_ANDROID && !UNITY_EDITOR
                // Su Meta Quest le arterie devono partire opache:
                isTransparent = false;
                fallbackColor = new Color(0.8f, 0.1f, 0.1f, 1.0f); // Rosso Arterioso
            #else       
                isTransparent = true;
                fallbackColor = new Color(0.8f, 0.1f, 0.1f, 1.0f); // Rosso Arterioso
            #endif

                startHidden = true;
            }
            else if (lower.Contains("pulmonaryvein"))
            {
            #if UNITY_ANDROID && !UNITY_EDITOR
                // Su Meta Quest le vene devono partire opache:
                isTransparent = false;
                fallbackColor = new Color(0.1f, 0.4f, 0.8f, 1.0f); // Blu Venoso
            #else
                isTransparent = true;
                fallbackColor = new Color(0.1f, 0.4f, 0.8f, 1.0f); // Blu Venoso
            #endif
                startHidden = true;
            }
            else if (lower.Contains("airways") || lower.Contains("airway") || lower.Contains("trachea") || lower.Contains("bronch"))
            {
            #if UNITY_ANDROID && !UNITY_EDITOR
                // Su Meta Quest le vie aeree devono partire opache:
                isTransparent = false;
                fallbackColor = new Color(0.6f, 0.8f, 0.9f, 1.0f); // Azzurro
            #else
                isTransparent = true;
                fallbackColor = new Color(0.6f, 0.8f, 0.9f, 1.0f);
            #endif
                startHidden = true;
            }
            else if (lower.Contains("nodule"))
            {
                fallbackColor = Color.green;
                isTransparent = false;
            }
            else if (lower.Contains("tool"))
            {
                fallbackColor = new Color(0.0f, 0.5f, 0.5f, 1.0f);
                mat.SetFloat("_Metallic", 0.5f);
                mat.SetFloat("_Smoothness", 0.5f);
            }

            if (isTransparent) SetupTransparentMaterial(mat);
            else SetupOpaqueMaterial(mat);

            Color finalColor = hasImportedColor ? importedColor : fallbackColor;

            if (lower.Contains("skin") || lower.Contains("body")) { 
                finalColor = new Color(1f, 0.78f, 0.58f, 0.2f); 
                mat.shader = Shader.Find("Universal Render Pipeline/Lit"); 
            }
            else if (lower.Contains("nodule")) { 
                finalColor = Color.green; 
                mat.shader = Shader.Find("Universal Render Pipeline/Lit"); 
            }
            else if (lower.Contains("lung")) { 
                finalColor = new Color(0.9f, 0.6f, 0.6f, 1f); 
                mat.shader = Shader.Find("Universal Render Pipeline/Lit"); 
            }
            else if (lower.Contains("pulmonaryarter")) { 
                finalColor = new Color(0.1f, 0.4f, 0.8f, 1.0f); 
                mat.shader = Shader.Find("Universal Render Pipeline/Lit"); 
            }
            else if (lower.Contains("pulmonaryvein")) { 
                finalColor = new Color(0.8f, 0.1f, 0.1f, 1.0f); 
                mat.shader = Shader.Find("Universal Render Pipeline/Lit"); 
            }

            SetMaterialColor(mat, finalColor);

            if (lower.Contains("nodule"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.green * 3f); 
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; 
            }

            if (isTransparent)
            {
                if (lower.Contains("skin") || lower.Contains("body"))
                    mat.renderQueue = 3000;
                else
                    mat.renderQueue = 3001;
            }
            else
            {
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            }

            rend.material = mat;
            DisableRendererShadows(rend);

            if (AnatomyManager.Instance != null)
            {
                AnatomyManager.Instance.RegisterOrganRenderer(objName, rend);
            }

            if (startHidden) rend.enabled = false;
        }
    }

    void SetupTransparentMaterial(Material mat)
    {
        mat.SetFloat("_Surface", 1.0f);
        mat.SetFloat("_Blend", 0.0f);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.SetShaderPassEnabled("ShadowCaster", false);
    }
    
    void SetupOpaqueMaterial(Material mat)
    {
        mat.SetFloat("_Surface", 0.0f);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        mat.SetInt("_ZWrite", 1);
        mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        mat.SetShaderPassEnabled("ShadowCaster", true);
    }
    void DisableRendererShadows(Renderer rend)
    {
        if (rend == null) return;

        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;

        if (rend.material != null)
        {
            rend.material.SetShaderPassEnabled("ShadowCaster", false);
        }
    }
    
    bool TryGetMaterialColor(Material mat, out Color color)
    {
        color = Color.white;
        if (mat == null) return false;

        if (mat.HasProperty("_BaseColor"))
        {
            color = mat.GetColor("_BaseColor");
            return true;
        }

        if (mat.HasProperty("_Color"))
        {
            color = mat.GetColor("_Color");
            return true;
        }

        return false;
    }

    void SetMaterialColor(Material mat, Color color)
    {
        if (mat == null) return;

        mat.color = color;

        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", color);
        }

        if (mat.HasProperty("_Color"))
        {
            mat.SetColor("_Color", color);
        }
    }
}
