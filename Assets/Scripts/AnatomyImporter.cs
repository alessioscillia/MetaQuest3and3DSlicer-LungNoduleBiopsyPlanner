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
        await LoadGltfFromUrl(modelUrl);
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
            modelContainer.transform.localRotation = Quaternion.Euler(-90, 0, 0);

            // --- PUNTO CRUCIALE PER L'UNIVERSALITÀ ---
            // NON spostare il modello. Lascialo a (0,0,0).
            // In questo modo, l'origine del file GLTF coincide perfettamente con l'origine del Mondo Unity.
            // Qualsiasi coordinata contenuta nel file (che sia -10, -340 o +1000) sarà rispettata.
            modelContainer.transform.position = new Vector3(0f, -1f, 0f);


            // 3. Inizializza lo slicer basandosi sulla geometria caricata
            InitializeSliceSystem(modelContainer);
            
        }
        else
        {
            Debug.LogError("Errore nel caricamento del glTF.");
        }
    }

    void InitializeSliceSystem(GameObject modelContainer)
    {
        if (skinRenderer == null) return;

        // Recuperiamo i bounds in World Space.
        // Poiché il container è a (0,0,0), questi bounds rappresentano le coordinate REALI della TAC.
        Bounds skinBounds = skinRenderer.bounds;

        GameObject slicerPrefab = Resources.Load<GameObject>("Prefabs/InteractiveSlicer");
        if (slicerPrefab != null)
        {
            // 1. Posizionamento del piano di slicing:
            // Posizionato al 37% dell'altezza (da min a max)
            float startYPosition = skinBounds.min.y + (skinBounds.size.y * 0.63f);
            
            Vector3 worldStartPosition = new Vector3(
                skinBounds.center.x, 
                startYPosition, 
                skinBounds.center.z 
            );

            // 2. Rotazione (per avere sezione Trasversale)
            Quaternion slicerRotation = Quaternion.Euler(-90, 0, 0);

            // 3. Istanzia
            GameObject slicerInstance = Instantiate(slicerPrefab, worldStartPosition, slicerRotation);
            
            // IMPORTANTE: Disattiva inizialmente il sistema di slicing
            slicerInstance.SetActive(false);
            
            // 4. Imparenta per ordine (mantiene la posizione world corretta)
            slicerInstance.transform.SetParent(modelContainer.transform, true);

            // 5. Registra il sistema di slicing in AnatomyManager
            AnatomyManager.Instance.RegisterSliceSystem(slicerInstance);

            // --- Configurazione Controller ---
            SliceInteractionController controller = slicerInstance.GetComponentInChildren<SliceInteractionController>();
            if (controller != null)
            {
                 // Conversione in locale per i vincoli dello slider
                 Vector3 skinMinLocal = slicerInstance.transform.InverseTransformPoint(skinBounds.min);
                 Vector3 skinMaxLocal = slicerInstance.transform.InverseTransformPoint(skinBounds.max);
                 
                 // Debug: Vediamo dove siamo finiti
                 Debug.Log($"[Universal Setup] Slice Start Y: {worldStartPosition.y}m. (Corrisponde a {worldStartPosition.y * 200}mm su Slicer)");

                 // IMPORTANTE: Invertiamo i limiti per mappare correttamente:
                 // - Piano in alto (max.y) → slice 0 → -10mm
                 // - Piano in basso (min.y) → ultima slice → -340mm
                 controller.InitializeConstraints(skinMaxLocal.y, skinMinLocal.y);

                // Connessione OpenIGTLink
                OpenIGTLinkConnect igtLink = FindFirstObjectByType<OpenIGTLinkConnect>();
                if (igtLink != null && controller.visualClippingPlane != null)
                {
                    // Registra il piano per inviare la posizione a Slicer
                    igtLink.RegisterDynamicModel(controller.visualClippingPlane.gameObject, "UnityReslicePlane");
                    
                    // Imposta questo piano come destinazione per ricevere le immagini delle slice
                    igtLink.SetMovingPlane(controller.visualClippingPlane.gameObject);
                }
            }
        }
    }

    // ... (Mantieni le funzioni AutomateMaterialSetup e helper materiali identiche a prima) ...
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
                baseColor = new Color(1f, 0.78f, 0.58f, 0.3f); // Ridotta opacità per migliore visualizzazione
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