using UnityEngine;
using TMPro;

/// <summary>
/// Calcola in tempo reale la deviazione dello strumento bioptico rispetto
/// a una traiettoria laser predefinita.
///
/// Visualizzazione:
///   - endpoint sulla punta stimata dello strumento
///   - endpoint sul punto più vicino della traiettoria
///   - dotted line tra i due punti
///   - label con distanza laterale sopra la dotted line
///   - label opzionale con angolo vicino alla punta
/// </summary>
public class TrajectoryDeviationCalculator : MonoBehaviour
{
    // ---------------------------------------------------------------------
    // Riferimenti scena
    // ---------------------------------------------------------------------

    [Header("Riferimenti scena")]
    public SurgicalLaserPointer laserPointer;
    public PoseClient poseClient;

    // ---------------------------------------------------------------------
    // Geometria strumento
    // ---------------------------------------------------------------------

    [Header("Asse longitudinale strumento")]
    [Tooltip("Asse locale del marker che corrisponde alla lunghezza dello strumento. Di default X locale.")]
    public Vector3 localToolAxis = Vector3.right;

    [Tooltip("Distanza dalla origine del marker alla punta dello strumento, lungo l'asse longitudinale.")]
    public float tipOffsetAlongToolAxis = 0.12f;

    // ---------------------------------------------------------------------
    // Filtro asse longitudinale
    // ---------------------------------------------------------------------

    [Header("Filtro asse longitudinale")]
    [Range(0f, 0.95f)]
    [Tooltip("Più alto = orientamento più stabile ma più lento a seguire i movimenti.")]
    public float forwardSmoothing = 0.65f;

    [Tooltip("Salta aggiornamenti con cambi angolari improvvisi oltre questa soglia.")]
    public float maxForwardJumpDeg = 30f;

    [Tooltip("Scarta spike improvvisi dell'asse longitudinale.")]
    public bool rejectForwardSpikes = true;

    private bool _hasStableForward = false;
    private Vector3 _stableForward = Vector3.forward;

    // ---------------------------------------------------------------------
    // Soglie colore
    // ---------------------------------------------------------------------

    [Header("Soglie colore")]
    [Tooltip("Sotto questa distanza laterale il feedback diventa verde.")]
    public float thresholdGreenCm = 0.5f;

    [Tooltip("Sotto questa distanza laterale il feedback diventa giallo. Sopra diventa rosso.")]
    public float thresholdYellowCm = 1.5f;

    // ---------------------------------------------------------------------
    // Dotted line laterale
    // ---------------------------------------------------------------------

    [Header("Dotted line laterale")]
    public bool showDottedLine = true;

    [Tooltip("Numero massimo di pallini tra punta e traiettoria.")]
    public int maxErrorDots = 14;

    [Tooltip("Distanza approssimativa tra i pallini, in metri.")]
    public float dotSpacingMeters = 0.015f;

    [Tooltip("Diametro dei pallini intermedi, in metri.")]
    public float dotDiameterMeters = 0.006f;

    [Tooltip("Diametro dei due endpoint, in metri.")]
    public float endpointDiameterMeters = 0.012f;

    [Tooltip("Leggero pulse sull'endpoint della traiettoria quando la distanza è sotto soglia verde.")]
    public bool pulseWhenAligned = true;

    // ---------------------------------------------------------------------
    // Testo distanza
    // ---------------------------------------------------------------------

    [Header("Testo distanza laterale")]
    public bool showDistanceText = true;

    [Tooltip("Offset verticale del testo distanza rispetto al centro della dotted line.")]
    public float distanceTextYOffset = 0.035f;

    [Tooltip("Dimensione del testo distanza in world space.")]
    public float distanceTextFontSize = 0.18f;

    // ---------------------------------------------------------------------
    // Testo angolo
    // ---------------------------------------------------------------------

    [Header("Testo angolo")]
    public bool showAngleText = true;

    [Tooltip("Mostra il testo angolare solo sopra questa soglia.")]
    public float minAngleTextDeg = 1.0f;

    [Tooltip("Offset verticale del testo angolare sopra la punta.")]
    public float angleTextYOffset = 0.055f;

    [Tooltip("Dimensione del testo angolare in world space.")]
    public float angleTextFontSize = 0.22f;

    // ---------------------------------------------------------------------
    // UI Canvas opzionale
    // ---------------------------------------------------------------------

    [Header("UI Canvas opzionale")]
    public TextMeshProUGUI deviationTextUI;

    // ---------------------------------------------------------------------
    // Debug
    // ---------------------------------------------------------------------

    [Header("Debug")]
    public bool showHUD = true;

    // ---------------------------------------------------------------------
    // Stato tracking
    // ---------------------------------------------------------------------

    private bool _markerDetected = false;
    private bool _trajectoryDefined = false;

    private float _lateralDistCm = -1f;
    private float _angularDevDeg = -1f;

    private Vector3 _tipPosition;
    private Vector3 _toolForward;
    private Vector3 _closestPointOnTraj;

    // ---------------------------------------------------------------------
    // Componenti visuali runtime
    // ---------------------------------------------------------------------

    private GameObject _visualRoot;
    private GameObject[] _errorDots;
    private GameObject _tipEndpoint;
    private GameObject _trajectoryEndpoint;

    private TextMeshPro _distanceText;
    private TextMeshPro _angleText;

    private Material _feedbackMaterial;

    // ---------------------------------------------------------------------
    // Unity lifecycle
    // ---------------------------------------------------------------------

    void Awake()
    {
        BuildMaterials();
        BuildDottedErrorVisual();
        BuildDistanceText();
        BuildAngleText();

        SetErrorVisualVisible(false);
        SetAngleTextVisible(false);
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

    void Update()
    {
        FaceTextsToCamera();
    }

    // ---------------------------------------------------------------------
    // Build materiali
    // ---------------------------------------------------------------------

    void BuildMaterials()
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit")
                 ?? Shader.Find("Unlit/Color")
                 ?? Shader.Find("Sprites/Default")
                 ?? Shader.Find("Standard");

        _feedbackMaterial = new Material(sh);
        _feedbackMaterial.name = "TrajectoryDeviation_Feedback_Material";
        _feedbackMaterial.color = Color.red;
        _feedbackMaterial.renderQueue = 4000;

        if (_feedbackMaterial.HasProperty("_ZTest"))
            _feedbackMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
    }

    // ---------------------------------------------------------------------
    // Build visual dotted line
    // ---------------------------------------------------------------------

    void BuildDottedErrorVisual()
    {
        _visualRoot = new GameObject("TrajectoryDeviation_DottedVisual");
        _visualRoot.transform.SetParent(transform, false);

        maxErrorDots = Mathf.Max(1, maxErrorDots);
        _errorDots = new GameObject[maxErrorDots];

        for (int i = 0; i < maxErrorDots; i++)
        {
            _errorDots[i] = CreateSphereVisual($"ErrorDot_{i}", dotDiameterMeters);
            _errorDots[i].transform.SetParent(_visualRoot.transform, false);
        }

        _tipEndpoint = CreateSphereVisual("TipEndpoint", endpointDiameterMeters);
        _tipEndpoint.transform.SetParent(_visualRoot.transform, false);

        _trajectoryEndpoint = CreateSphereVisual("TrajectoryEndpoint", endpointDiameterMeters);
        _trajectoryEndpoint.transform.SetParent(_visualRoot.transform, false);
    }

    GameObject CreateSphereVisual(string objectName, float diameter)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = objectName;

        Collider col = go.GetComponent<Collider>();
        if (col != null)
            Destroy(col);

        Renderer r = go.GetComponent<Renderer>();
        if (r != null)
            r.sharedMaterial = _feedbackMaterial;

        go.transform.localScale = Vector3.one * diameter;
        go.SetActive(false);

        return go;
    }

    // ---------------------------------------------------------------------
    // Build testi 3D
    // ---------------------------------------------------------------------

    void BuildDistanceText()
    {
        GameObject textGo = new GameObject("LateralDistanceText");
        textGo.transform.SetParent(transform, false);

        _distanceText = textGo.AddComponent<TextMeshPro>();
        _distanceText.alignment = TextAlignmentOptions.Center;
        _distanceText.fontStyle = FontStyles.Bold;
        _distanceText.fontSize = distanceTextFontSize;
        _distanceText.color = Color.white;
        _distanceText.isOverlay = true;
        _distanceText.text = "";

        textGo.SetActive(false);
    }

    void BuildAngleText()
    {
        GameObject textGo = new GameObject("AngularDeviationText");
        textGo.transform.SetParent(transform, false);

        _angleText = textGo.AddComponent<TextMeshPro>();
        _angleText.alignment = TextAlignmentOptions.Center;
        _angleText.fontStyle = FontStyles.Bold;
        _angleText.fontSize = angleTextFontSize;
        _angleText.color = Color.cyan;
        _angleText.isOverlay = true;
        _angleText.text = "";

        textGo.SetActive(false);
    }

    // ---------------------------------------------------------------------
    // Eventi PoseClient
    // ---------------------------------------------------------------------

    void HandlePoseDetected(Vector3 worldPos, Quaternion worldRot)
    {
        if (poseClient != null && !poseClient.IsTrackingEnabled)
            return;

        _markerDetected = true;

        Vector3 axisLocal = localToolAxis.sqrMagnitude > 0.0001f
            ? localToolAxis.normalized
            : Vector3.right;

        Vector3 rawForward = (worldRot * axisLocal).normalized;

        /*
         * Evita inversioni improvvise dell'asse.
         * Utile perché per il confronto angolare l'asse longitudinale può essere ambiguo
         * tra verso + e verso -, ma per calcolare la punta serve un verso stabile.
         */
        if (_hasStableForward && Vector3.Dot(rawForward, _stableForward) < 0f)
            rawForward = -rawForward;

        if (!_hasStableForward)
        {
            _stableForward = rawForward;
            _hasStableForward = true;
        }
        else
        {
            float jumpDeg = Vector3.Angle(_stableForward, rawForward);
            bool isSpike = rejectForwardSpikes && jumpDeg > maxForwardJumpDeg;

            if (!isSpike)
            {
                float t = 1f - forwardSmoothing;
                _stableForward = Vector3.Slerp(_stableForward, rawForward, t).normalized;
            }
            else
            {
                Debug.LogWarning(
                    $"[TrajectoryDeviation] Spike orientamento scartato: jump={jumpDeg:F1}°"
                );
            }
        }

        _toolForward = _stableForward;

        // Punta stimata lungo il solo asse longitudinale filtrato.
        _tipPosition = worldPos + _toolForward * tipOffsetAlongToolAxis;

        ComputeDeviations();
    }

    void HandleMarkerLost()
    {
        _markerDetected = false;
        _hasStableForward = false;

        SetErrorVisualVisible(false);
        SetAngleTextVisible(false);

        UpdateUI();
    }

    // ---------------------------------------------------------------------
    // Calcolo deviazioni
    // ---------------------------------------------------------------------

    void ComputeDeviations()
    {
        if (laserPointer == null || !laserPointer.TrajectoryDefined)
        {
            _trajectoryDefined = false;

            SetErrorVisualVisible(false);
            SetAngleTextVisible(false);

            UpdateUI();
            return;
        }

        _trajectoryDefined = true;

        Vector3 A = laserPointer.SkinHitPoint;
        Vector3 B = laserPointer.NoduleHitPoint;
        Vector3 trajVector = B - A;

        if (trajVector.sqrMagnitude < 0.000001f)
        {
            _trajectoryDefined = false;

            SetErrorVisualVisible(false);
            SetAngleTextVisible(false);

            UpdateUI();
            return;
        }

        Vector3 trajDir = trajVector.normalized;

        // 1. Distanza laterale punta-traiettoria
        Vector3 v = _tipPosition - A;
        Vector3 vParallel = Vector3.Dot(v, trajDir) * trajDir;
        Vector3 vPerp = v - vParallel;

        _lateralDistCm = vPerp.magnitude * 100f;
        _closestPointOnTraj = A + vParallel;

        // 2. Deviazione angolare tra asse strumento e traiettoria
        float rawAngle = Vector3.Angle(_toolForward, trajDir);
        _angularDevDeg = rawAngle > 90f ? 180f - rawAngle : rawAngle;

        // 3. Aggiornamento visuale
        UpdateDottedErrorVisual();
        UpdateAngleText();
        UpdateUI();
    }

    // ---------------------------------------------------------------------
    // Dotted line visual
    // ---------------------------------------------------------------------

    void UpdateDottedErrorVisual()
    {
        bool shouldShow =
            showDottedLine &&
            _markerDetected &&
            _trajectoryDefined &&
            _lateralDistCm >= 0f;

        SetErrorVisualVisible(shouldShow);

        if (!shouldShow)
            return;

        Color feedbackColor = GetFeedbackColor();
        ApplyFeedbackColor(feedbackColor);

        Vector3 start = _tipPosition;
        Vector3 end = _closestPointOnTraj;

        float distanceMeters = Vector3.Distance(start, end);

        // Endpoint sempre visibili
        _tipEndpoint.transform.position = start;
        _trajectoryEndpoint.transform.position = end;

        float endpointScale = endpointDiameterMeters;

        if (pulseWhenAligned && _lateralDistCm < thresholdGreenCm)
        {
            float pulse = 1f + 0.18f * Mathf.Sin(Time.time * 7.5f);
            _trajectoryEndpoint.transform.localScale = Vector3.one * endpointScale * pulse;
        }
        else
        {
            _trajectoryEndpoint.transform.localScale = Vector3.one * endpointScale;
        }

        _tipEndpoint.transform.localScale = Vector3.one * endpointScale;

        // Pallini intermedi
        int nDots = 0;

        if (distanceMeters > 0.002f)
        {
            nDots = Mathf.Clamp(
                Mathf.FloorToInt(distanceMeters / Mathf.Max(0.001f, dotSpacingMeters)),
                1,
                maxErrorDots
            );
        }

        float errorT = Mathf.Clamp01(_lateralDistCm / Mathf.Max(0.001f, thresholdYellowCm));
        float dotSize = Mathf.Lerp(dotDiameterMeters, dotDiameterMeters * 1.35f, errorT);

        for (int i = 0; i < maxErrorDots; i++)
        {
            bool active = i < nDots;
            _errorDots[i].SetActive(active);

            if (!active)
                continue;

            float t = (i + 1f) / (nDots + 1f);
            Vector3 p = Vector3.Lerp(start, end, t);

            _errorDots[i].transform.position = p;
            _errorDots[i].transform.localScale = Vector3.one * dotSize;
        }

        UpdateDistanceText(start, end, feedbackColor);
    }

    void UpdateDistanceText(Vector3 start, Vector3 end, Color feedbackColor)
    {
        if (_distanceText == null)
            return;

        bool shouldShow =
            showDistanceText &&
            _markerDetected &&
            _trajectoryDefined &&
            _lateralDistCm >= 0f;

        _distanceText.gameObject.SetActive(shouldShow);

        if (!shouldShow)
            return;

        Vector3 midpoint = (start + end) * 0.5f;

        /*
         * Offset verso l'alto in world-space.
         * Semplice e leggibile in AR; evita che il testo copra esattamente la dotted line.
         */
        _distanceText.transform.position = midpoint + Vector3.up * distanceTextYOffset;

        _distanceText.fontSize = distanceTextFontSize;
        _distanceText.color = feedbackColor;
        _distanceText.text = $"{_lateralDistCm:F1} cm";
    }

    void SetErrorVisualVisible(bool visible)
    {
        if (_visualRoot != null)
            _visualRoot.SetActive(visible);

        if (_distanceText != null)
            _distanceText.gameObject.SetActive(visible && showDistanceText);
    }

    void ApplyFeedbackColor(Color color)
    {
        if (_feedbackMaterial != null)
            _feedbackMaterial.color = color;
    }

    Color GetFeedbackColor()
    {
        if (_lateralDistCm < thresholdGreenCm)
            return Color.green;

        if (_lateralDistCm < thresholdYellowCm)
            return Color.yellow;

        return new Color(1f, 0.25f, 0.25f);
    }

    // ---------------------------------------------------------------------
    // Testo angolare
    // ---------------------------------------------------------------------

    void UpdateAngleText()
    {
        if (_angleText == null)
            return;

        bool shouldShow =
            showAngleText &&
            _markerDetected &&
            _trajectoryDefined &&
            _angularDevDeg > minAngleTextDeg;

        SetAngleTextVisible(shouldShow);

        if (!shouldShow)
            return;

        _angleText.transform.position = _tipPosition + Vector3.up * angleTextYOffset;
        _angleText.fontSize = angleTextFontSize;
        _angleText.text = $"{_angularDevDeg:F1}°";
    }

    void SetAngleTextVisible(bool visible)
    {
        if (_angleText != null)
            _angleText.gameObject.SetActive(visible);
    }

    // ---------------------------------------------------------------------
    // Billboard testi verso camera
    // ---------------------------------------------------------------------

    void FaceTextsToCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;

        FaceTextToCamera(_distanceText, cam);
        FaceTextToCamera(_angleText, cam);
    }

    void FaceTextToCamera(TextMeshPro text, Camera cam)
    {
        if (text == null || !text.gameObject.activeSelf)
            return;

        Vector3 dir = text.transform.position - cam.transform.position;

        if (dir.sqrMagnitude < 0.0001f)
            return;

        text.transform.rotation = Quaternion.LookRotation(dir.normalized);
    }

    // ---------------------------------------------------------------------
    // UI 2D opzionale
    // ---------------------------------------------------------------------

    void UpdateUI()
    {
        if (deviationTextUI == null)
            return;

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

    // ---------------------------------------------------------------------
    // HUD debug OnGUI
    // ---------------------------------------------------------------------

    void OnGUI()
    {
        if (!showHUD || poseClient != null && !poseClient.IsTrackingEnabled)
            return;

        GUIStyle styleBox = new GUIStyle(GUI.skin.box)
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
            GUI.Box(
                new Rect(10, 90, 500, 44),
                "  Traiettoria N/D — punta il nodulo col laser",
                styleBox
            );
            return;
        }

        Color feedbackColor = GetFeedbackColor();
        styleBox.normal.textColor = feedbackColor;

        GUI.Box(
            new Rect(10, 90, 580, 44),
            $"  Laterale: {_lateralDistCm:F1} cm    Angolo: {_angularDevDeg:F1}°",
            styleBox
        );

        GUIStyle styleDetail = new GUIStyle(styleBox)
        {
            fontSize = 16,
            fontStyle = FontStyle.Normal
        };

        styleDetail.normal.textColor = new Color(0.75f, 0.75f, 0.75f);

        GUI.Box(
            new Rect(10, 138, 580, 30),
            $"  Punta: {_tipPosition:F3}    Traj: {_closestPointOnTraj:F3}",
            styleDetail
        );
    }

    // ---------------------------------------------------------------------
    // Proprietà pubbliche
    // ---------------------------------------------------------------------

    public float LateralDistanceCm => _lateralDistCm;
    public float AngularDeviationDeg => _angularDevDeg;
    public bool IsMarkerDetected => _markerDetected;
    public bool IsTrajectoryDefined => _trajectoryDefined;
    public Vector3 TipPosition => _tipPosition;
    public Vector3 ClosestPointOnTraj => _closestPointOnTraj;
    public Vector3 ToolForward => _toolForward;

    // ---------------------------------------------------------------------
    // Cleanup
    // ---------------------------------------------------------------------

    void OnDestroy()
    {
        if (poseClient != null)
        {
            poseClient.OnPoseDetected -= HandlePoseDetected;
            poseClient.OnMarkerLost -= HandleMarkerLost;
        }

        if (_feedbackMaterial != null)
            Destroy(_feedbackMaterial);
    }
}