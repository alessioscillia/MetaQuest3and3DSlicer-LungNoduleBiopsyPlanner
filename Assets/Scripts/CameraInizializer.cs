using UnityEngine;

public class CameraInitializer : MonoBehaviour
{
    void Start()
    {
        // Tenta di trovare la camera principale tramite il tag "MainCamera"
        // Questo funziona sia con Meta Quest, sia con MRTK, sia con progetti standard
        Camera mainCamera = Camera.main;

        if (mainCamera != null)
        {
            InitializeCamera(mainCamera);
        }
        else
        {
            Debug.LogError("ERRORE: Nessuna Camera trovata nella scena! Assicurati che il '[BuildingBlock] Camera Rig' sia attivo.");
        }
    }

    void InitializeCamera(Camera cam)
    {
        // Imposta la modalità di pulizia dello sfondo a "Solid Color" (invece di Skybox)
        cam.clearFlags = CameraClearFlags.SolidColor;

        // Imposta il colore dello sfondo a Nero impostando anche alpha = 0
        // (Il nero è ideale per la realtà mista/AR perché diventa trasparente nel passthrough)
        cam.backgroundColor = new Color(0, 0, 0, 0);
    }
}