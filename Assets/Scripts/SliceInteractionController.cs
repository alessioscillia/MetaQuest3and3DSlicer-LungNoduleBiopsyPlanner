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
            
            // Z vincolato ai limiti del volume
            constraints.MinZ.Constrain = true;
            constraints.MinZ.Value = _minZ;
            constraints.MaxZ.Constrain = true;
            constraints.MaxZ.Value = _maxZ;

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
        if (_constraintsInitialized)
        {
            Vector3 currentPos = transform.localPosition;
            bool needsCorrection = false;
            
            // 1. FORZA X e Y a rimanere fissi
            if (Mathf.Abs(currentPos.x - _fixedX) > 0.001f || Mathf.Abs(currentPos.y - _fixedY) > 0.001f)
            {
                currentPos.x = _fixedX;
                currentPos.y = _fixedY;
                needsCorrection = true;
            }
            
            // 2. HARD CLAMP: Muro invalicabile per l'asse Z
            // Se la mano trascina fuori l'oggetto, lo riportiamo immediatamente al limite
            if (currentPos.z < _minZ || currentPos.z > _maxZ)
            {
                currentPos.z = Mathf.Clamp(currentPos.z, _minZ, _maxZ);
                needsCorrection = true;
            }
            
            // 3. DISCRETIZZAZIONE: Snap alla slice più vicina (usando la posizione già limitata)
            if (useDiscreteSlices && _sliceStepSize > 0f)
            {
                // Calcoliamo l'indice in base alla posizione attuale filtrata
                float normalizedPosition = Mathf.InverseLerp(_minZ, _maxZ, currentPos.z);
                int nearestSliceIndex = Mathf.RoundToInt(normalizedPosition * (totalSlices - 1));
                
                float targetZ = _minZ + (nearestSliceIndex * _sliceStepSize);
                
                // Aggiorniamo lo snap solo se l'indice della slice è effettivamente cambiato
                if (nearestSliceIndex != _currentSliceIndex)
                {
                    _currentSliceIndex = nearestSliceIndex;
                    currentPos.z = targetZ;
                    needsCorrection = true;
                }
            }
            
            // 4. Applica tutte le correzioni necessarie al Transform
            if (needsCorrection)
            {
                transform.localPosition = currentPos;
            }
        }
        
        // Sincronizza il piano visivo con la posizione Z finale del cubo
        if (visualClippingPlane != null)
        {
            Vector3 newPos = visualClippingPlane.localPosition;
            newPos.z = transform.localPosition.z;
            visualClippingPlane.localPosition = newPos;
        }

        // Aggiorna lo shader per effettuare il taglio effettivo
        if (clippingMaterial != null)
        {
            Plane p = new Plane(transform.up, transform.position);
            Vector4 planeRepresentation = new Vector4(p.normal.x, p.normal.y, p.normal.z, p.distance);
            clippingMaterial.SetVector("_ClippingPlane", planeRepresentation);
        }
    }
}