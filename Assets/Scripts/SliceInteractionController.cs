using UnityEngine;
using Oculus.Interaction; // Assicurati di avere l'Interaction SDK importato

public class SliceInteractionController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Il piano visivo che deve muoversi insieme al cubo")]
    public Transform visualClippingPlane;

    [Tooltip("Il materiale che esegue il clipping (deve avere una property Vector4 o Float per l'altezza)")]
    public Material clippingMaterial;

    [Header("Interaction Settings")]
    [Tooltip("Riferimento al componente OneGrabTranslateTransformer del cubo")]
    public OneGrabTranslateTransformer translateTransformer;

    [Header("Slice Discretization")]
    [Tooltip("Numero totale di slice nel volume CT (da Slicer)")]
    public int totalSlices = 140;
    
    [Tooltip("Abilita movimento discreto (ogni step = 1 slice)")]
    public bool useDiscreteSlices = true;

    private float _minZ;
    private float _maxZ;
    private float _fixedX; // X fisso del cubo
    private float _fixedY; // Y fisso del cubo
    private bool _constraintsInitialized = false;
    private float _lastZPosition = float.MinValue; // Per tracciare cambiamenti Z
    private int _currentSliceIndex = 0;
    private float _sliceStepSize = 0f;

    // Chiama questo metodo dal tuo AnatomyImporter per configurare il movimento
    public void InitializeConstraints(float minZ, float maxZ)
    {
        _minZ = minZ;
        _maxZ = maxZ;

        if (translateTransformer != null)
        {
            var constraints = translateTransformer.Constraints;
            
            // Z libero (nessun limite di altezza)
            constraints.MinZ.Constrain = false;
            constraints.MaxZ.Constrain = false;

            // Blocca X e Y alla posizione CORRENTE del cubo (non a 0)
            float currentX = transform.localPosition.x;
            float currentY = transform.localPosition.y;
            
            _fixedX = currentX;
            _fixedY = currentY;
            _constraintsInitialized = true;
            
            constraints.MinX.Constrain = true; 
            constraints.MinX.Value = currentX;
            constraints.MaxX.Constrain = true; 
            constraints.MaxX.Value = currentX;
            
            constraints.MinY.Constrain = true; 
            constraints.MinY.Value = currentY;
            constraints.MaxY.Constrain = true; 
            constraints.MaxY.Value = currentY;

            translateTransformer.InjectOptionalConstraints(constraints);
            
            // Calcola la dimensione di ogni step (distanza tra slice)
            float totalRange = _maxZ - _minZ;
            _sliceStepSize = totalRange / Mathf.Max(1, totalSlices - 1);
            
            Debug.Log($"[SliceInteraction] Vincoli impostati - X fisso: {_fixedX:F3}, Y fisso: {_fixedY:F3}, Z illimitato");
            Debug.Log($"[SliceInteraction] {totalSlices} slice, step size: {_sliceStepSize:F4} unità");
        }
    }
    
    /// <summary>
    /// Imposta il numero totale di slice (chiamato da codice esterno)
    /// </summary>
    public void SetTotalSlices(int slices)
    {
        totalSlices = Mathf.Max(1, slices);
        if (_constraintsInitialized)
        {
            float totalRange = _maxZ - _minZ;
            _sliceStepSize = totalRange / Mathf.Max(1, totalSlices - 1);
            Debug.Log($"[SliceInteraction] Aggiornato a {totalSlices} slice, step: {_sliceStepSize:F4}");
        }
    }
    
    /// <summary>
    /// Ottieni l'indice della slice corrente (0 = minZ, totalSlices-1 = maxZ)
    /// </summary>
    public int GetCurrentSliceIndex()
    {
        if (!_constraintsInitialized) return 0;
        
        float currentZ = transform.localPosition.z;
        float normalizedPosition = Mathf.InverseLerp(_minZ, _maxZ, currentZ);
        return Mathf.RoundToInt(normalizedPosition * (totalSlices - 1));
    }
    
    /// <summary>
    /// Ottieni la posizione normalizzata (0-1) del piano
    /// </summary>
    public float GetNormalizedPosition()
    {
        if (!_constraintsInitialized) return 0.5f;
        return Mathf.InverseLerp(_minZ, _maxZ, transform.localPosition.z);
    }

    void Update()
    {
        // Forza X e Y a rimanere fissi se i vincoli sono stati inizializzati
        if (_constraintsInitialized)
        {
            Vector3 currentPos = transform.localPosition;
            bool needsCorrection = false;
            
            // Se X o Y sono cambiati, resettali ai valori fissi
            if (Mathf.Abs(currentPos.x - _fixedX) > 0.001f || Mathf.Abs(currentPos.y - _fixedY) > 0.001f)
            {
                currentPos.x = _fixedX;
                currentPos.y = _fixedY;
                needsCorrection = true;
            }
            
            // DISCRETIZZAZIONE: Snap alla slice più vicina
            if (useDiscreteSlices && _sliceStepSize > 0f)
            {
                // Calcola quale slice è più vicina
                int nearestSliceIndex = GetCurrentSliceIndex();
                float targetZ = _minZ + (nearestSliceIndex * _sliceStepSize);
                
                // Solo se cambiato rispetto alla slice corrente
                if (nearestSliceIndex != _currentSliceIndex)
                {
                    _currentSliceIndex = nearestSliceIndex;
                    currentPos.z = targetZ;
                    needsCorrection = true;
                    
                    Debug.Log($"[Slicer] Slice {_currentSliceIndex + 1}/{totalSlices} - Posizione Z: {targetZ:F4}");
                }
            }
            else
            {
                // Movimento continuo: forza solo i limiti min/max
                if (currentPos.z < _minZ)
                {
                    currentPos.z = _minZ;
                    needsCorrection = true;
                }
                else if (currentPos.z > _maxZ)
                {
                    currentPos.z = _maxZ;
                    needsCorrection = true;
                }
            }
            
            if (needsCorrection)
            {
                transform.localPosition = currentPos;
            }
        }
        
        // Debug: mostra informazioni solo in modalità continua (discrete mode già logga sopra)
        if (_constraintsInitialized && !useDiscreteSlices)
        {
            float currentZ = transform.localPosition.z;
            if (Mathf.Abs(currentZ - _lastZPosition) > 0.01f)
            {
                float normalized = GetNormalizedPosition();
                Debug.Log($"[Slicer] Z: {currentZ:F3} | Normalized: {normalized:F3} | Limiti: [{_minZ:F3}, {_maxZ:F3}]");
                _lastZPosition = currentZ;
            }
        }
        
        // 1. Sincronizza il piano visivo con la posizione Z del cubo (handle)
        if (visualClippingPlane != null)
        {
            Vector3 newPos = visualClippingPlane.localPosition;
            newPos.z = transform.localPosition.z;
            visualClippingPlane.localPosition = newPos;
        }

        // 2. Aggiorna lo shader per effettuare il taglio effettivo
        if (clippingMaterial != null)
        {
            // Esempio: Passiamo la posizione Y mondiale o locale allo shader
            // Nota: Dipende da come è scritto il tuo shader di clipping. 
            // Spesso si passa un Piano (Normale + Distanza).

            Plane p = new Plane(transform.up, transform.position);
            Vector4 planeRepresentation = new Vector4(p.normal.x, p.normal.y, p.normal.z, p.distance);
            clippingMaterial.SetVector("_ClippingPlane", planeRepresentation);
        }
    }
}