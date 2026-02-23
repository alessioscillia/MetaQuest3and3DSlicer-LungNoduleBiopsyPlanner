using UnityEngine;
using UnityEngine.UI;

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
    [SerializeField] private GameObject sliceSystemInstance; // Riferimento al prefab InteractiveSlicer

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

    /// <summary>
    /// Registra il sistema di slicing (chiamato da AnatomyImporter)
    /// </summary>
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
        // Applica l'opacità ai vasi sanguigni (rossi)
        SetOpacity(vesselsRenderer, value);
        // Applica la STESSA opacità alle vie aeree (celesti)
        SetOpacity(airwaysRenderer, value);
    }


    // --- NUOVI TOGGLE (ON/OFF) ---
    // Collega questi metodi all'evento "On Value Changed" dei tuoi Toggle UI

    public void ToggleSkin(bool isVisible)
    {
        if (skinRenderer) skinRenderer.enabled = isVisible;
    }

    public void ToggleLungs(bool isVisible)
    {
        if (lungRenderer) lungRenderer.enabled = isVisible;
    }

    public void ToggleBones(bool isVisible)
    {
        if (bonesRenderer) bonesRenderer.enabled = isVisible;
    }

    public void ToggleVessels(bool isVisible)
    {
        if (vesselsRenderer) vesselsRenderer.enabled = isVisible;
        if (airwaysRenderer) airwaysRenderer.enabled = isVisible;
    }

    public void ToggleNodule(bool isVisible)
    {
        if (noduleRenderer) noduleRenderer.enabled = isVisible;
    }

    // --- TOGGLE SISTEMA DI SLICING ---
    /// <summary>
    /// Attiva/disattiva il sistema di slicing interattivo
    /// Collega questo metodo al Toggle UI "Slice System"
    /// </summary>
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