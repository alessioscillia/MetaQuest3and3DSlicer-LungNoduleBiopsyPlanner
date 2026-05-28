using UnityEngine;
using GLTFast;
using System;
using System.Threading.Tasks;
public class AnatomyImporter : MonoBehaviour
{
    public string modelUrl = "http://127.0.0.1:8080/model.glb";
    public int maxLoadAttempts = 5;
    public float retryDelaySeconds = 0.75f;
    private Renderer skinRenderer;
    private GameObject lungObject;
    private bool isLoadingModel;
    async void Start()
    {
        InitializeCamera();
        await LoadGltfFromUrl(modelUrl);
    }
    void InitializeCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            // Imposta sfondo nero per VR/passthrough
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = new Color(0, 0, 0, 0);
        }
        else
        {
            Debug.LogWarning("Nessuna MainCamera trovata. Verifica che il Camera Rig sia attivo.");
        }
    }
    async Task LoadGltfFromUrl(string url)
    {
        if (isLoadingModel)
        {
            Debug.LogWarning("[AnatomyImporter] Caricamento già in corso, richiesta ignorata.");
            return;
        }

        isLoadingModel = true;
        // --- MODIFICA ANTI-CACHE ---
        // Aggiungiamo i Ticks (il tempo attuale) alla fine dell'URL.
        // Esempio: http://127.0.0.1:8080/model.glb?t=6384218934...
        // Il server lo ignorerà, ma Unity lo vedrà come un URL nuovo e NON userà la cache!
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
                    // glTFast può lanciare questa eccezione se il download HTTP viene interrotto.
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
            

            // --- NUOVO: Nodo intermedio per il flip dell'asse X ---
            // modelContainer ha scala SEMPRE positiva → GrabFreeTransformer funziona correttamente
            // FlipNode porta il segno negativo su X → il modello appare specchiato come prima
            GameObject flipNode = new GameObject("FlipNode");
            flipNode.transform.SetParent(modelContainer.transform, false);
            flipNode.transform.localScale    = new Vector3(-1f, 1f, 1f); // solo il flip, nessun'altra scala
            flipNode.transform.localPosition = Vector3.zero;
            flipNode.transform.localRotation = Quaternion.identity;

            // Re-parent tutti i figli del container dentro il flipNode
            // (sono stati creati da InstantiateMainSceneAsync direttamente sotto modelContainer)
            var children = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in modelContainer.transform)
            {
                if (child.gameObject != flipNode)
                    children.Add(child);
            }
            foreach (var child in children)
                child.SetParent(flipNode.transform, false);
            // -----------------------------------------------------

            // Scala POSITIVA sul container — GrabFreeTransformer non vedrà mai valori negativi
            modelContainer.transform.localScale    = new Vector3(0.001f, 0.001f, 0.001f);
            modelContainer.transform.localRotation = Quaternion.identity;
            modelContainer.transform.position      = new Vector3(0f, -1f, 0f);


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
           if (SurgicalAlignment.Instance != null)
           {
               SurgicalAlignment.Instance.SetHologram(modelContainer);
           }
           else
           {
               Debug.LogWarning("Modello caricato, ma SurgicalAlignment non trovato nella scena.");
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
        GameObject slicerPrefab = Resources.Load<GameObject>("Prefabs/InteractiveSlicer");
        if (slicerPrefab != null)
        {
            // 1. IL FIX: Trova il centro geometrico reale nello spazio globale (World Space)
            Vector3 worldCenter = skinBounds.center;
            // 2. Istanzia il piano ESATTAMENTE al centro
            GameObject slicerInstance = Instantiate(slicerPrefab, worldCenter, Quaternion.identity);
            slicerInstance.SetActive(false);
            // 3. Imparenta al modello (true = mantieni la posizione globale al centro)
            slicerInstance.transform.SetParent(modelContainer.transform, true);
            // 4. Allinea la rotazione localmente al modello
            slicerInstance.transform.localRotation = Quaternion.identity;
            // Registra il sistema
            AnatomyManager.Instance.RegisterSliceSystem(slicerInstance);
            // --- Configurazione Controller ---
            SliceInteractionController controller = slicerInstance.GetComponentInChildren<SliceInteractionController>();
            if (controller != null)
            {
                // Calcoliamo gli 8 vertici del Bounding Box globale
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
                // Ora che il piano Ã¨ GIA' al centro, i limiti calcolati
                // saranno perfettamente bilanciati (es. da -10 a +10)
                foreach (Vector3 corner in boundsCorners)
                {
                    float localZ = slicerInstance.transform.InverseTransformPoint(corner).z;
                    if (localZ < actualMinZ) actualMinZ = localZ;
                    if (localZ > actualMaxZ) actualMaxZ = localZ;
                }
                // Inizializza i vincoli
                controller.InitializeConstraints(actualMinZ, actualMaxZ);
                // RIMOSSO: Il blocco in cui cercavi di calcolare 'startZ' e spostare
                // la localPosition. Non serve piÃ¹ perchÃ© il piano nasce giÃ  centrato!
                // Connessione OpenIGTLink
                OpenIGTLinkConnect igtLink = FindFirstObjectByType<OpenIGTLinkConnect>();
                if (igtLink != null && controller.visualClippingPlane != null)
                {
                    igtLink.RegisterDynamicModel(controller.visualClippingPlane.gameObject, "UnityReslicePlane");
                    igtLink.SetMovingPlane(controller.visualClippingPlane.gameObject);
                }
            }
        }
    }
void AutomateMaterialSetup(GameObject loadedModel)
{
    MeshRenderer[] renderers = loadedModel.GetComponentsInChildren<MeshRenderer>();

    foreach (MeshRenderer rend in renderers)
    {
        string objName = rend.gameObject.name;
        string lower = objName.ToLowerInvariant();

        // Clona il materiale importato (se presente) così preservi shader e colore glTF
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

        // Fallback colore solo se il glTF non ne ha uno utile
        Color fallbackColor = Color.white;

        if (lower.Contains("skin") || lower.Contains("body"))
        {
            isTransparent = true;
            fallbackColor = new Color(1f, 0.78f, 0.58f, 0.2f);
            skinRenderer = rend;
        }
        else if (lower.Contains("lung"))
        {
            isTransparent = true;
            fallbackColor = new Color(1f, 0.4f, 0.4f, 1f);
            lungObject = rend.gameObject;
        }
        else if (lower.Contains("bones") || lower.Contains("rib") || lower.Contains("vertebra"))
        {
            isTransparent = true;
            startHidden = true;
            fallbackColor = new Color(0.9f, 0.9f, 0.8f, 1.0f);
        }
        
        else if (lower.Contains("pulmonaryarter"))
        {
            isTransparent = true;
            startHidden = true;
            fallbackColor = new Color(0.8f, 0.1f, 0.1f, 1.0f); // Rosso Arterioso
        }
        // Cerca ESATTAMENTE "pulmonaryvein" (tutto attaccato)
        else if (lower.Contains("pulmonaryvein"))
        {
            isTransparent = true;
            startHidden = true;
            fallbackColor = new Color(0.1f, 0.4f, 0.8f, 1.0f); // Blu Venoso
        }
        else if (lower.Contains("airways") || lower.Contains("airway") || lower.Contains("trachea") || lower.Contains("bronch"))
        {
            isTransparent = true;
            startHidden = true;
            fallbackColor = new Color(0.6f, 0.8f, 0.9f, 1.0f);
        }
        else if (lower.Contains("nodule"))
        {
            fallbackColor = Color.green; // Torniamo al verde puro originale
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

        // Colore finale: prima importato, altrimenti fallback
        Color finalColor = hasImportedColor ? importedColor : fallbackColor;
        
        // --- MODIFICA PER I NODULI E I POLMONI ---
        if (lower.Contains("nodule"))
        {
            finalColor = Color.green; // Forza il verde puro ignorando il glTF
            // Forza lo shader standard URP per far funzionare l'emissione
            mat.shader = Shader.Find("Universal Render Pipeline/Lit"); 
        }
        else if (lower.Contains("lung"))
        {
            // Forza il colore anatomico per i polmoni (Rosa Salmone) ignorando il glTF verde
            finalColor = new Color(0.9f, 0.6f, 0.6f, 1f); 
            // Rimuove lo shader glTF (che legge i vertici verdi) e usa quello standard di Unity
            mat.shader = Shader.Find("Universal Render Pipeline/Lit"); 
        }
        else if (lower.Contains("pulmonaryarter"))
        {
            // Forza le ARTERIE polmonari al BLU (sangue deossigenato)
            finalColor = new Color(0.1f, 0.4f, 0.8f, 1.0f); 
            // Rimuove lo shader glTF e i suoi vertex colors
            mat.shader = Shader.Find("Universal Render Pipeline/Lit"); 
        }
        else if (lower.Contains("pulmonaryvein"))
        {
            // Forza le VENE polmonari al ROSSO (sangue ossigenato)
            finalColor = new Color(0.8f, 0.1f, 0.1f, 1.0f); 
            // Rimuove lo shader glTF e i suoi vertex colors
            mat.shader = Shader.Find("Universal Render Pipeline/Lit"); 
        }
        // -----------------------------------------
        
        SetMaterialColor(mat, finalColor);

        // Evidenziazione noduli
        if (lower.Contains("nodule"))
        {
            mat.EnableKeyword("_EMISSION");
            // Moltiplica il colore per renderlo molto acceso (puoi alzare il '3f' se lo vuoi ancora più luminoso)
            mat.SetColor("_EmissionColor", Color.green * 3f); 
            // Aiuta Unity a registrare l'emissione
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; 
        }
        // ------------------------------

        // Mantieni gli organi interni renderizzati dopo la pelle trasparente
        if (lower.Contains("lung") || lower.Contains("vessels") || lower.Contains("airways") || 
            lower.Contains("bone") || lower.Contains("rib") || lower.Contains("vertebra"))
        {
            mat.renderQueue = 3001;
        }

        rend.material = mat;

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