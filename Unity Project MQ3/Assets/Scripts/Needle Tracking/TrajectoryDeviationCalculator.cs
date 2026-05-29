using UnityEngine;
using TMPro;

/// <summary>
/// Calcola in tempo reale la deviazione dello strumento bioptico.
/// </summary>
public class TrajectoryDeviationCalculator : MonoBehaviour
{
    [Header("Riferimenti scena")]
    public SurgicalLaserPointer laserPointer;
    public PoseClient poseClient;

    [Header("Offset punta ago (spazio locale marker)")]
    public Vector3 tipOffsetLocal = new Vector3(0f, 0f, 0.12f);

    [Header("Soglie colore HUD")]
    public float thresholdGreenCm = 0.5f;
    public float thresholdYellowCm = 1.5f;

    [Header("Linea di errore laterale 3D")]
    public float errorLineWidth = 0.002f;

    [Header("Testo Angolo di Rotazione")]
    [Tooltip("Colore del testo dei gradi fluttuante")]
    public Color angleTextColor = Color.cyan;

    [Header("UI Canvas (opzionale)")]
    public TextMeshProUGUI deviationTextUI;

    [Header("Debug")]
    public bool showHUD = true;

    // Stato
    private bool _markerDetected = false;
    private bool _trajectoryDefined = false;
    private float _lateralDistCm = -1f;
    private float _angularDevDeg = -1f;
    private Vector3 _tipPosition;
    private Vector3 _toolForward;
    private Vector3 _closestPointOnTraj;

    // Componenti Visivi 3D
    private LineRenderer _errorLine;
    private TextMeshPro _rotationText;

    void Awake()
    {
        BuildErrorLine();
        BuildRotationText();
    }

    void Start()
    {
        if (poseClient == null)
            poseClient = FindFirstObjectByType<PoseClient>();

        if (poseClient != null)
        {
            poseClient.OnPoseDetected += HandlePoseDetected;
            poseClient.OnMarkerLost += HandleMarkerLost;
        }
        else
        {
            Debug.LogError("[TrajectoryDeviation] PoseClient non trovato!");
        }

        if (laserPointer == null)
            laserPointer = FindFirstObjectByType<SurgicalLaserPointer>();
    }

    void BuildErrorLine()
    {
        var go = new GameObject("ErrorLateralLine");
        go.transform.SetParent(transform, false);
        _errorLine = go.AddComponent<LineRenderer>();

        _errorLine.positionCount = 2;
        _errorLine.startWidth = errorLineWidth;
        _errorLine.endWidth = errorLineWidth;
        _errorLine.useWorldSpace = true;
        _errorLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _errorLine.receiveShadows = false;

        Shader sh = Shader.Find("Universal Render Pipeline/Unlit")
                 ?? Shader.Find("Unlit/Color")
                 ?? Shader.Find("Sprites/Default");
        _errorLine.material = sh != null ? new Material(sh) : new Material(_errorLine.material);
        _errorLine.material.color = Color.red;

        _errorLine.material.renderQueue = 4000;
        _errorLine.material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);

        _errorLine.gameObject.SetActive(false);
    }

    void BuildRotationText()
    {
        // Setup Testo Gradi 3D
        var textGo = new GameObject("RotationDegreesText");
        textGo.transform.SetParent(transform, false);
        _rotationText = textGo.AddComponent<TextMeshPro>();
        _rotationText.fontSize = 0.3f; // Dimensione ottimizzata per visore VR
        _rotationText.alignment = TextAlignmentOptions.Center;
        _rotationText.color = angleTextColor;
        _rotationText.fontStyle = FontStyles.Bold;
        
        // Rende il testo sempre visibile in overlay sopra la geometria
        _rotationText.isOverlay = true; 
        _rotationText.gameObject.SetActive(false);
    }

    void HandlePoseDetected(Vector3 worldPos, Quaternion worldRot)
    {
        if (poseClient != null && !poseClient.IsTrackingEnabled) return;

        _markerDetected = true;
        _tipPosition = worldPos + worldRot * tipOffsetLocal;
        _toolForward = worldRot * Vector3.right; // L'asse longitudinale del tuo strumento

        ComputeDeviations();
    }

    void HandleMarkerLost()
    {
        _markerDetected = false;
        if (_errorLine != null) _errorLine.gameObject.SetActive(false);
        if (_rotationText != null) _rotationText.gameObject.SetActive(false);
        UpdateUI();
    }

    void ComputeDeviations()
    {
        if (laserPointer == null || !laserPointer.TrajectoryDefined)
        {
            _trajectoryDefined = false;
            if (_errorLine != null) _errorLine.gameObject.SetActive(false);
            if (_rotationText != null) _rotationText.gameObject.SetActive(false);
            UpdateUI();
            return;
        }

        _trajectoryDefined = true;

        Vector3 A = laserPointer.SkinHitPoint;
        Vector3 B = laserPointer.NoduleHitPoint;
        Vector3 trajDir = (B - A).normalized;

        // 1. Distanza laterale
        Vector3 v = _tipPosition - A;
        Vector3 vParallel = Vector3.Dot(v, trajDir) * trajDir;
        Vector3 vPerp = v - vParallel;
        _lateralDistCm = vPerp.magnitude * 100f;

        _closestPointOnTraj = A + vParallel;

        // 2. Deviazione angolare (Angolo totale 3D tra asse strumento e traiettoria)
        float rawAngle = Vector3.Angle(_toolForward, trajDir);
        _angularDevDeg = rawAngle > 90f ? 180f - rawAngle : rawAngle;

        // 3. Aggiorna grafica
        UpdateErrorLine();
        UpdateRotationText();
        UpdateUI();
    }

    void UpdateErrorLine()
    {
        if (_errorLine == null) return;

        bool shouldShow = _markerDetected && _trajectoryDefined && _lateralDistCm > 0.01f;
        _errorLine.gameObject.SetActive(shouldShow);

        if (!shouldShow) return;

        _errorLine.SetPosition(0, _tipPosition);
        _errorLine.SetPosition(1, _closestPointOnTraj);

        Color lineColor =
            _lateralDistCm < thresholdGreenCm ? Color.green :
            _lateralDistCm < thresholdYellowCm ? Color.yellow :
                                                 new Color(1f, 0.25f, 0.25f);

        _errorLine.material.color = lineColor;

        float t = Mathf.Clamp01(_lateralDistCm / thresholdYellowCm);
        float w = Mathf.Lerp(errorLineWidth, errorLineWidth * 4f, t);
        _errorLine.startWidth = w;
        _errorLine.endWidth = w;
    }

    void UpdateRotationText()
    {
        if (_rotationText == null) return;

        // Mostra il testo dei gradi solo se l'errore angolare è maggiore di 1 grado
        bool shouldShowText = _markerDetected && _trajectoryDefined && _angularDevDeg > 1.0f;
        _rotationText.gameObject.SetActive(shouldShowText);

        if (!shouldShowText) return;

        // Posiziona il testo dei gradi poco sopra la punta dell'ago
        _rotationText.transform.position = _tipPosition + Vector3.up * 0.04f;
        _rotationText.text = $"{_angularDevDeg:F1}°";

        // Fai ruotare il testo affinché guardi sempre l'utente (Camera)
        if (Camera.main != null)
        {
            _rotationText.transform.rotation = Quaternion.LookRotation(_rotationText.transform.position - Camera.main.transform.position);
        }
    }

    void UpdateUI()
    {
        if (deviationTextUI == null) return;

        if (poseClient != null && !poseClient.IsTrackingEnabled)
        {
            deviationTextUI.text = "";
            return;
        }
        if (!_markerDetected)
        {
            deviationTextUI.text = "Marker: N/D";
            return;
        }
        if (!_trajectoryDefined)
        {
            deviationTextUI.text = "Traiettoria: N/D\n(punta il nodulo col laser)";
            return;
        }

        deviationTextUI.text =
            $"Lat:  {_lateralDistCm:F1} cm\n" +
            $"Ang: {_angularDevDeg:F1}°";
    }

    void OnGUI()
    {
        if (!showHUD || poseClient != null && !poseClient.IsTrackingEnabled) return;

        var styleBox = new GUIStyle(GUI.skin.box)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(14, 14, 8, 8)
        };

        if (!_markerDetected)
        {
            styleBox.normal.textColor = new Color(1f, 0.45f, 0.45f);
            GUI.Box(new Rect(10, 90, 380, 44), "  Marker non rilevato", styleBox);
            return;
        }

        if (!_trajectoryDefined)
        {
            styleBox.normal.textColor = new Color(1f, 0.8f, 0.3f);
            GUI.Box(new Rect(10, 90, 500, 44),
                "  Traiettoria N/D — punta il nodulo col laser", styleBox);
            return;
        }

        Color feedbackColor =
            _lateralDistCm < thresholdGreenCm ? Color.green :
            _lateralDistCm < thresholdYellowCm ? Color.yellow :
                                                 new Color(1f, 0.3f, 0.3f);

        styleBox.normal.textColor = feedbackColor;
        GUI.Box(new Rect(10, 90, 580, 44),
            $"  Laterale: {_lateralDistCm:F1} cm    Angolo: {_angularDevDeg:F1}°",
            styleBox);

        var styleDetail = new GUIStyle(styleBox)
        {
            fontSize = 16,
            fontStyle = FontStyle.Normal
        };
        styleDetail.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
        GUI.Box(new Rect(10, 138, 580, 30),
            $"  Punta: {_tipPosition:F3}    Traj: {_closestPointOnTraj:F3}",
            styleDetail);
    }

    public float LateralDistanceCm => _lateralDistCm;
    public float AngularDeviationDeg => _angularDevDeg;
    public bool IsMarkerDetected => _markerDetected;
    public bool IsTrajectoryDefined => _trajectoryDefined;
    public Vector3 TipPosition => _tipPosition;
    public Vector3 ClosestPointOnTraj => _closestPointOnTraj;

    void OnDestroy()
    {
        if (poseClient != null)
        {
            poseClient.OnPoseDetected -= HandlePoseDetected;
            poseClient.OnMarkerLost -= HandleMarkerLost;
        }
    }
}