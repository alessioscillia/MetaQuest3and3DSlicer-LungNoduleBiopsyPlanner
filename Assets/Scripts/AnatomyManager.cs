using UnityEngine;
using UnityEngine.UI;
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

    [Header("Slice System")]
    [SerializeField] private GameObject sliceSystemInstance;

    [Header("Toggle Texts")]
    // Cambia "Text" in "TextMeshProUGUI" se usi TextMeshPro
    [SerializeField] private Text lungsToggleText; 
    [SerializeField] private Text bonesToggleText;
    [SerializeField] private Text awVesselsToggleText;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // --- REGISTRAZIONE AUTOMATICA ---
    public void RegisterOrganRenderer(string objName, Renderer rend)
    {
        string lowerName = objName.ToLower();

        if (lowerName.Contains("skin")) skinRenderer = rend;
        else if (lowerName.Contains("lung")) lungRenderer = rend;
        else if (lowerName.Contains("bone") || lowerName.Contains("rib") || lowerName.Contains("vertebra")) bonesRenderer = rend;
        else if (lowerName.Contains("vessel")) 
        {
            vesselsRenderer = rend;
        }
        else if (lowerName.Contains("airways") || lowerName.Contains("trachea")) 
        {
            airwaysRenderer = rend;
        }
        else if (lowerName.Contains("nodule")) noduleRenderer = rend;
    }

    public void RegisterSliceSystem(GameObject sliceSystem)
    {
        sliceSystemInstance = sliceSystem;
        Debug.Log("[AnatomyManager] Sistema di slicing registrato.");
    }

    // --- OPACITY SLIDERS ---
    private void SetOpacity(Renderer rend, float alphaVal)
    {
        if (rend != null && rend.material != null)
        {
            Color color = rend.material.color;
            color.a = alphaVal;
            rend.material.color = color;
            if (rend.material.HasProperty("_BaseColor")) rend.material.SetColor("_BaseColor", color);
        }
    }

    public void UpdateSkinOpacity(float value) => SetOpacity(skinRenderer, value);
    public void UpdateLungOpacity(float value) => SetOpacity(lungRenderer, value);
    public void UpdateBonesOpacity(float value) => SetOpacity(bonesRenderer, value);
    public void UpdateVesselsOpacity(float value) 
    {
        SetOpacity(vesselsRenderer, value);
        SetOpacity(airwaysRenderer, value);
    }

    // --- NUOVI TOGGLE (ON/OFF) ---

    public void ToggleSkin(bool isVisible)
    {
        if (skinRenderer) skinRenderer.enabled = isVisible;
    }

    public void ToggleLungs(bool isVisible)
    {
        if (lungRenderer) lungRenderer.enabled = isVisible;
        // Aggiorna il testo in base allo stato
        if (lungsToggleText) lungsToggleText.text = isVisible ? "Lungs ON" : "Lungs OFF";
    }

    public void ToggleBones(bool isVisible)
    {
        if (bonesRenderer) bonesRenderer.enabled = isVisible;
        // Aggiorna il testo in base allo stato
        if (bonesToggleText) bonesToggleText.text = isVisible ? "Bones ON" : "Bones OFF";
    }

    public void ToggleVessels(bool isVisible)
    {
        if (vesselsRenderer) vesselsRenderer.enabled = isVisible;
        if (airwaysRenderer) airwaysRenderer.enabled = isVisible;
        // Aggiorna il testo in base allo stato
        if (awVesselsToggleText) awVesselsToggleText.text = isVisible ? "AWVessels ON" : "AWVessels OFF";
    }

    public void ToggleNodule(bool isVisible)
    {
        if (noduleRenderer) noduleRenderer.enabled = isVisible;
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
}