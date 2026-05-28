using UnityEngine;
using TMPro;

/// <summary>
/// Calcola in tempo reale la deviazione dello strumento bioptico
/// rispetto alla traiettoria teorica (skin hit → nodulo).
///
/// METRICHE:
///   • Distanza laterale  — distanza perpendicolare dalla punta
///                          dell'ago alla retta della traiettoria [cm]
///   • Deviazione angolare — angolo tra asse longitudinale dell'ago
///                           e direzione della traiettoria [°]
///
/// SETUP:
///   1. Aggiungi questo script a un GameObject vuoto.
///   2. Nell'Inspector assegna LaserPointer, PoseClient e (opz.) il TextUI.
///   3. Misura l'offset fisico marker→punta lungo l'asse dell'ago
///      e inseriscilo in tipOffsetLocal (Z = avanti nel local space del marker).
/// </summary>
public class TrajectoryDeviationCalculator : MonoBehaviour
{
    // -----------------------------------------------------------------------
    // Inspector
    // -----------------------------------------------------------------------
    [Header("Riferimenti scena")]
    public SurgicalLaserPointer laserPointer;
    public PoseClient poseClient;

    [Header("Offset punta ago (spazio locale marker)")]
    [Tooltip("Distanza in METRI dal centro del marker alla punta. " +
             "Z > 0 = avanti rispetto al marker. Es: (0, 0, 0.12) = 12 cm avanti.")]
    public Vector3 tipOffsetLocal = new Vector3(0f, 0f, 0.12f);

    [Header("Soglie colore HUD")]
    [Tooltip("Distanza laterale [cm] sotto cui il feedback è verde")]
    public float thresholdGreenCm = 0.5f;
    [Tooltip("Distanza laterale [cm] sotto cui il feedback è giallo")]
    public float thresholdYellowCm = 1.5f;

    [Header("Linea di errore laterale 3D")]
    [Tooltip("Spessore del LineRenderer in metri")]
    public float errorLineWidth = 0.002f;

    [Header("UI Canvas (opzionale)")]
    [Tooltip("TextMeshPro su un Canvas World Space per feedback in-headset")]
    public TextMeshProUGUI deviationTextUI;

    [Header("Debug")]
    public bool showHUD = true;

    // -----------------------------------------------------------------------
    // Stato
    // -----------------------------------------------------------------------
    private bool _markerDetected = false;
    private bool _trajectoryDefined = false;
    private float _lateralDistCm = -1f;
    private float _angularDevDeg = -1f;
    private Vector3 _tipPosition;
    private Vector3 _toolForward;
    private Vector3 _closestPointOnTraj;

    // Linea 3D
    private LineRenderer _errorLine;

    // -----------------------------------------------------------------------
    void Awake()
    {
        BuildErrorLine();
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

    // -----------------------------------------------------------------------
    // Costruzione LineRenderer per la linea di errore
    // -----------------------------------------------------------------------
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

        // Materiale unlit — funziona sia in URP che built-in
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit")
                 ?? Shader.Find("Unlit/Color")
                 ?? Shader.Find("Sprites/Default");
        _errorLine.material = sh != null ? new Material(sh) : new Material(_errorLine.material);
        _errorLine.material.color = Color.red;

        // Forza rendering sopra la geometria (come gli assi)
        _errorLine.material.renderQueue = 4000;
        _errorLine.material.SetInt("_ZTest",
            (int)UnityEngine.Rendering.CompareFunction.Always);

        _errorLine.gameObject.SetActive(false);
    }

    // -----------------------------------------------------------------------
    // Callback da PoseClient
    // -----------------------------------------------------------------------
    void HandlePoseDetected(Vector3 worldPos, Quaternion worldRot)
    {
        if (poseClient != null && !poseClient.IsTrackingEnabled) return;

        _markerDetected = true;
        _tipPosition = worldPos + worldRot * tipOffsetLocal;
        _toolForward = worldRot * Vector3.right;

        ComputeDeviations();
    }

    void HandleMarkerLost()
    {
        _markerDetected = false;
        if (_errorLine != null) _errorLine.gameObject.SetActive(false);
        UpdateUI();
    }

    // -----------------------------------------------------------------------
    // Calcolo metriche
    // -----------------------------------------------------------------------
    void ComputeDeviations()
    {
        if (laserPointer == null || !laserPointer.TrajectoryDefined)
        {
            _trajectoryDefined = false;
            if (_errorLine != null) _errorLine.gameObject.SetActive(false);
            UpdateUI();
            return;
        }

        _trajectoryDefined = true;

        Vector3 A = laserPointer.SkinHitPoint;
        Vector3 B = laserPointer.NoduleHitPoint;
        Vector3 trajDir = (B - A).normalized;

        // ------------------------------------------------------------------
        // 1. Distanza laterale (point-to-line)
        // ------------------------------------------------------------------
        Vector3 v = _tipPosition - A;
        Vector3 vParallel = Vector3.Dot(v, trajDir) * trajDir;
        Vector3 vPerp = v - vParallel;
        _lateralDistCm = vPerp.magnitude * 100f;

        // Punto più vicino sulla retta
        _closestPointOnTraj = A + vParallel;

        // ------------------------------------------------------------------
        // 2. Deviazione angolare (angolo acuto tra asse ago e traiettoria)
        // ------------------------------------------------------------------
        float rawAngle = Vector3.Angle(_toolForward, trajDir);
        _angularDevDeg = rawAngle > 90f ? 180f - rawAngle : rawAngle;

        // ------------------------------------------------------------------
        // 3. Aggiorna la linea 3D di errore
        // ------------------------------------------------------------------
        UpdateErrorLine();
        UpdateUI();
    }

    // -----------------------------------------------------------------------
    // Linea 3D di errore
    // -----------------------------------------------------------------------
    void UpdateErrorLine()
    {
        if (_errorLine == null) return;

        // Mostra la linea solo se c'è un errore misurabile
        bool shouldShow = _markerDetected && _trajectoryDefined && _lateralDistCm > 0.01f;
        _errorLine.gameObject.SetActive(shouldShow);

        if (!shouldShow) return;

        _errorLine.SetPosition(0, _tipPosition);
        _errorLine.SetPosition(1, _closestPointOnTraj);

        // Colore dinamico in base alla distanza laterale
        Color lineColor =
            _lateralDistCm < thresholdGreenCm ? Color.green :
            _lateralDistCm < thresholdYellowCm ? Color.yellow :
                                                 new Color(1f, 0.25f, 0.25f);

        _errorLine.material.color = lineColor;

        // Spessore proporzionale all'errore (max 4× lo spessore base)
        float t = Mathf.Clamp01(_lateralDistCm / thresholdYellowCm);
        float w = Mathf.Lerp(errorLineWidth, errorLineWidth * 4f, t);
        _errorLine.startWidth = w;
        _errorLine.endWidth = w;
    }

    // -----------------------------------------------------------------------
    // Aggiornamento UI Canvas World Space
    // -----------------------------------------------------------------------
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

    // -----------------------------------------------------------------------
    // HUD on-screen
    // -----------------------------------------------------------------------
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

        // Feedback cromatico
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

    // -----------------------------------------------------------------------
    // Proprietà pubbliche
    // -----------------------------------------------------------------------
    public float LateralDistanceCm => _lateralDistCm;
    public float AngularDeviationDeg => _angularDevDeg;
    public bool IsMarkerDetected => _markerDetected;
    public bool IsTrajectoryDefined => _trajectoryDefined;
    public Vector3 TipPosition => _tipPosition;
    public Vector3 ClosestPointOnTraj => _closestPointOnTraj;

    // -----------------------------------------------------------------------
    void OnDestroy()
    {
        if (poseClient != null)
        {
            poseClient.OnPoseDetected -= HandlePoseDetected;
            poseClient.OnMarkerLost -= HandleMarkerLost;
        }
    }
}