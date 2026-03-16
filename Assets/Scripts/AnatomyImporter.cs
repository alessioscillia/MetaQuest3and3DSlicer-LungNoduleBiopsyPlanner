using UnityEngine;
using GLTFast;
using System.Threading.Tasks;

public class AnatomyImporter : MonoBehaviour
{
    public string modelUrl = "http://localhost:8080/model.gltf";

    private Renderer skinRenderer; 
    private GameObject lungObject;

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
        GameObject modelContainer = new GameObject("TotalSegmentatorModel");
        modelContainer.transform.SetParent(null); // Assicura che sia nella root della scena
        var gltf = new GltfImport();
        bool success = await gltf.Load(url);

        if (success)
        {
            await gltf.InstantiateMainSceneAsync(modelContainer.transform);

            // 1. Assegna materiali
            AutomateMaterialSetup(modelContainer);

            // 2. SETUP UNIVERSALE DELLE TRASFORMAZIONI
            // Scala: se Slicer è in mm e Unity in m, serve 0.001. 
            // Tu usi 0.005 probabilmente perché il modello originale era molto piccolo o per preferenza visiva.
            // NOTA: Se cambi scala, assicurati che 'OpenIGTLinkConnect' abbia il moltiplicatore inverso corretto.
            modelContainer.transform.localScale = new Vector3(0.005f, 0.005f, 0.005f);
            
            // Rotazione: Corregge l'orientamento (da supino a in piedi)
            modelContainer.transform.localRotation = Quaternion.Euler(-90f, 0, 0);

            // --- PUNTO CRUCIALE PER L'UNIVERSALITÀ ---
            // NON spostare il modello. Lascialo a (0,0,0).
            // In questo modo, l'origine del file GLTF coincide perfettamente con l'origine del Mondo Unity.
            // Qualsiasi coordinata contenuta nel file (che sia -10, -340 o +1000) sarà rispettata.
            modelContainer.transform.position = new Vector3(0f, 0f, 0f);

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

//            if (SurgicalAlignment.Instance != null)
//            {
//                SurgicalAlignment.Instance.SetHologram(modelContainer);
//            }
//            else
//            {
//                Debug.LogWarning("Modello caricato, ma SurgicalAlignment non trovato nella scena.");


  //          }
        }
        else
        {
            Debug.LogError("Errore nel caricamento del glTF.");
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

                // Ora che il piano è GIA' al centro, i limiti calcolati 
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
                // la localPosition. Non serve più perché il piano nasce già centrato!

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
            Material newMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Color baseColor = Color.white;
            bool isTransparent = false;
            bool startHidden = false;

            if (objName.Contains("skin") || objName.Contains("Skin"))
            {
                baseColor = new Color(1f, 0.78f, 0.58f, 0.2f); // Pelle rosa chiaro con trasparenza
                isTransparent = true;
                skinRenderer = rend; 
            }
            else if (objName.Contains("Lung"))
            {
                baseColor = new Color(1f, 0.4f, 0.4f, 1f); // Rosso chiaro per polmoni
                isTransparent = true;
                lungObject = rend.gameObject;
            }
            else if (objName.Contains("Bones"))
            {
                baseColor = new Color(0.9f, 0.9f, 0.8f, 1.0f);
                isTransparent = true;
                startHidden = true;
            }

            else if (objName.Contains("Vessels")) // Riconosce i Vasi
            {
                // Rosso scuro/Arterioso per i vasi
                baseColor = new Color(0.8f, 0.1f, 0.1f, 1.0f); 
                isTransparent = true; // Importante per il tuo slider opacità
                startHidden = true;   // Se vuoi che partano nascosti come prima
            }
            else if (objName.Contains("Airways")) // Riconosce le Vie Aeree
            {
                // Celeste/Grigio chiaro per i bronchi (simile all'immagine)
                baseColor = new Color(0.6f, 0.8f, 0.9f, 1.0f); 
                isTransparent = true; // Importante per il tuo slider opacità
                startHidden = true;
            }

            else if (objName.Contains("nodule"))
            {
                baseColor = Color.green;
                newMat.EnableKeyword("_EMISSION");
                newMat.SetColor("_EmissionColor", Color.green * 2);
                isTransparent = false;
            }
            else if (objName.Contains("Tool") || objName.Contains("tool"))
            {
                baseColor = new Color(0.0f, 0.5f, 0.5f, 1.0f); // Verde scuro/teal per strumenti chirurgici
                isTransparent = false; // Lo strumento chirurgico è solido
                
                // Opzionale: Rendiamolo un po' più "metallico" in Unity
                newMat.SetFloat("_Metallic", 0.5f);
                newMat.SetFloat("_Smoothness", 0.5f);
            }

            if (isTransparent) SetupTransparentMaterial(newMat);
            else SetupOpaqueMaterial(newMat);

            newMat.color = baseColor;
            if (newMat.HasProperty("_BaseColor")) newMat.SetColor("_BaseColor", baseColor);
            
            // Imposta render queue più alto per organi interni (renderizzati dopo la pelle)
            if (objName.Contains("Lung") || objName.Contains("Vessels") || objName.Contains("Airways"))
            {
                newMat.renderQueue = 3001; // Renderizzati dopo la pelle
            }
            
            rend.material = newMat;
            
            // Registriamo TUTTI i renderer. 
            // Se il tuo AnatomyManager usa una lista, controllerà l'opacità di Vessels e Airways insieme agli altri.
            AnatomyManager.Instance.RegisterOrganRenderer(objName, rend);
            
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
}