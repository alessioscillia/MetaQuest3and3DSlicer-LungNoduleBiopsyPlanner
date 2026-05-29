using UnityEngine;

/// <summary>
/// Visualizza i tre assi del sistema di riferimento del marker (6 DOF):
///   X → Rosso
///   Y → Verde
///   Z → Blu
/// </summary>
public class AxisVisualizer : MonoBehaviour
{
    [Header("Riferimento")]
    public PoseClient poseClient;

    [Header("Aspetto assi")]
    [Tooltip("Lunghezza di ogni asse in metri (es. 0.05 = 5 cm)")]
    public float axisLength = 0.05f;

    [Tooltip("Spessore del LineRenderer in metri")]
    public float axisWidth = 0.002f;

    [Header("Smoothing temporale")]
    [Range(0f, 0.95f)]
    public float smoothing = 0.4f;

    [Header("Debug on-screen")]
    public bool showHUD = true;

    private GameObject   _root;
    private LineRenderer _lrX, _lrY, _lrZ;

    private bool       _visible = false;
    private Vector3    _smoothPos;
    private Quaternion _smoothRot = Quaternion.identity;
    private float      _lastReproj = -1f;
    private int        _lastInliers = 0;

    void Start()
    {
        BuildAxisObjects();

        if (poseClient == null)
            poseClient = FindFirstObjectByType<PoseClient>();

        if (poseClient == null)
        {
            Debug.LogError("[AxisVisualizer] PoseClient non trovato nella scena!");
            return;
        }

        poseClient.OnPoseDetected += HandlePoseDetected;
        poseClient.OnMarkerLost   += HandleMarkerLost;

        SetVisible(false);
    }

    // Aggiunto Update per garantire che gli assi spariscano se il tracking viene spento dalla GUI
    void Update()
    {
        if (poseClient != null && !poseClient.IsTrackingEnabled && _visible)
        {
            HandleMarkerLost();
        }
    }

    void BuildAxisObjects()
    {
        _root = new GameObject("CylMarker_Axes");

        _lrX = CreateAxis("Axis_X", Color.red);
        _lrY = CreateAxis("Axis_Y", Color.green);
        _lrZ = CreateAxis("Axis_Z", Color.blue);
    }

    LineRenderer CreateAxis(string axisName, Color color)
    {
        var go = new GameObject(axisName);
        go.transform.SetParent(_root.transform, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount  = 2;
        lr.startWidth     = axisWidth;
        lr.endWidth       = axisWidth;
        lr.useWorldSpace  = true;
        lr.shadowCastingMode  = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        Shader sh = Shader.Find("Universal Render Pipeline/Unlit")
                 ?? Shader.Find("Unlit/Color")
                 ?? Shader.Find("Sprites/Default");

        var mat = sh != null ? new Material(sh) : new Material(lr.material);
        mat.color = color;
        lr.material = mat;

        return lr;
    }

    void HandlePoseDetected(Vector3 worldPos, Quaternion worldRot)
    {
        _lastReproj  = poseClient.LastReprojError;
        _lastInliers = poseClient.LastNInliers;

        if (!_visible)
        {
            _smoothPos = worldPos;
            _smoothRot = worldRot;
            _visible   = true;
            SetVisible(true);
        }
        else
        {
            float t   = 1f - smoothing;
            _smoothPos = Vector3.Lerp(_smoothPos, worldPos, t);
            _smoothRot = Quaternion.Slerp(_smoothRot, worldRot, t);
        }

        RedrawAxes(_smoothPos, _smoothRot);
    }

    void HandleMarkerLost()
    {
        _visible = false;
        SetVisible(false);
    }

    void RedrawAxes(Vector3 origin, Quaternion rot)
    {
        _lrX.SetPosition(0, origin);
        _lrX.SetPosition(1, origin + rot * Vector3.right   * axisLength);

        _lrY.SetPosition(0, origin);
        _lrY.SetPosition(1, origin + rot * Vector3.up      * axisLength);

        _lrZ.SetPosition(0, origin);
        _lrZ.SetPosition(1, origin + rot * Vector3.forward * axisLength);
    }

    void SetVisible(bool v)
    {
        if (_root != null) _root.SetActive(v);
    }

    void OnGUI()
    {
        if (!showHUD) return;

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 22,
            fontStyle = FontStyle.Bold
        };

        if (_visible)
        {
            style.normal.textColor = Color.green;
            GUI.Label(new Rect(10, 10, 600, 35), style.normal.textColor == Color.green
                ? $"● MARKER RILEVATO  |  Reproj: {_lastReproj:F2} px  |  Inliers: {_lastInliers}"
                : "", style);
            GUI.Label(new Rect(10, 10, 600, 35),
                $"● MARKER RILEVATO  |  Reproj: {_lastReproj:F2} px  |  Inliers: {_lastInliers}",
                style);

            var styleSmall = new GUIStyle(style) { fontSize = 16, fontStyle = FontStyle.Normal };
            styleSmall.normal.textColor = Color.white;
            GUI.Label(new Rect(10, 45, 700, 25),
                $"   Pos: {_smoothPos:F4}   Rot: {_smoothRot.eulerAngles:F1}",
                styleSmall);
        }
        else
        {
            style.normal.textColor = new Color(1f, 0.4f, 0.4f);
            GUI.Label(new Rect(10, 10, 400, 35), "○ Marker non rilevato", style);
        }
    }

    void OnDestroy()
    {
        if (poseClient != null)
        {
            poseClient.OnPoseDetected -= HandlePoseDetected;
            poseClient.OnMarkerLost   -= HandleMarkerLost;
        }
        if (_root != null)
            Destroy(_root);
    }
}