using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;
using Random = UnityEngine.Random;

public class LiDARScanner : MonoBehaviour
{
    private List<Vector3> _positionsList = new();
    private List<VisualEffect> _vfxList = new();
    private VisualEffect _currentVFX;
    private Texture2D _texture;
    private Color[] _positions;
    private bool _createNewVFX;
    private int _particleAmount;

    private const string TEXTURE_NAME = "PositionsTexture";
    private const string RESOLUTION_PARAMETER_NAME = "Resolution";

    private static readonly Vector3 HiddenPosition = new Vector3(0f, -100000f, 0f);

    [SerializeField] private LayerMask _layerMask;
    [SerializeField] private VisualEffect _vfxPrefab;
    [SerializeField] private GameObject _vfxContainer;
    [Tooltip("Only used as a fallback for the look direction when there is no camera")]
    [SerializeField] private Transform _castPoint;
    [SerializeField] private int _pointsPerScan = 40;

    [SerializeField] private int resolution = 100;

    [Header("Scan area (what the camera sees)")]
    [Tooltip("The camera whose view is scanned. If empty, the Camera on Look Source is used, then the Main Camera.")]
    [SerializeField] private Camera _camera;
    [Tooltip("How much of the camera's view is scanned. 1 = the whole view, using the camera's own Field of View. Smaller = only the center of the view.")]
    [Range(0.05f, 1f)]
    [SerializeField] private float _viewCoverage = 1f;

    [Header("Distance & Density Falloff")]
    [Tooltip("Distance (in meters) where dots start becoming sparser. Closer = denser, Further = fewer spots.")]
    [SerializeField] private float _distanceThinningStart = 8f;

    [Header("Focus (look at the same spot before scanning)")]
    [Tooltip("Seconds the player must keep looking at the same spot before points appear")]
    [SerializeField] private float _focusTimeRequired = 1.5f;
    [Tooltip("How far (in meters) the look point can move and still count as the same spot")]
    [SerializeField] private float _focusTolerance = 1f;
    [Tooltip("What the player looks through. Drag the Camera here.")]
    [SerializeField] private Transform _lookSource;

    [Header("Scan speed")]
    [Tooltip("Seconds between each scan burst. Bigger = slower scan.")]
    [SerializeField] private float _scanInterval = 0.1f;

    [Header("Debug")]
    [Tooltip("Draws the focus line in the Scene view (green = hit, red = no hit) and writes the scanner state to the Console")]
    [SerializeField] private bool _debug = true;

    private Vector3 _focusPoint;
    private bool _hasFocusPoint;
    private float _focusTimer;
    private float _scanTimer;
    private string _lastState;

    public float FocusProgress => Mathf.Clamp01(_focusTimer / Mathf.Max(0.01f, _focusTimeRequired));

    private void Start()
    {
        _createNewVFX = true;
        CreateNewVFX();
        ApplyPositions();
    }

    private void Update()
    {
        if (!UpdateFocus()) return;

        _scanTimer += Time.deltaTime;
        if (_scanTimer < _scanInterval) return;
        _scanTimer = 0f;

        Scan();
    }

    private Camera ResolveCamera()
    {
        if (_camera != null) return _camera;
        if (_lookSource != null) _camera = _lookSource.GetComponent<Camera>();
        if (_camera == null) _camera = Camera.main;
        return _camera;
    }

    private bool UpdateFocus()
    {
        GetLookRay(out Vector3 origin, out Vector3 direction);

        if (!Physics.Raycast(origin, direction, out RaycastHit hit, Mathf.Infinity, _layerMask))
        {
            if (_debug) Debug.DrawRay(origin, direction * 100f, Color.red);
            SetState("No hit: nothing on the Layer Mask layer in front of the camera");
            ResetFocus();
            return false;
        }

        if (_debug) Debug.DrawLine(origin, hit.point, Color.green);

        if (!_hasFocusPoint || Vector3.Distance(hit.point, _focusPoint) > _focusTolerance)
        {
            _focusPoint = hit.point;
            _hasFocusPoint = true;
            _focusTimer = 0f;
            SetState("Looking at a new spot, timer restarted");
            return false;
        }

        _focusTimer += Time.deltaTime;
        if (_focusTimer < _focusTimeRequired)
        {
            SetState("Charging...");
            return false;
        }

        return true;
    }

    private void ResetFocus()
    {
        _hasFocusPoint = false;
        _focusTimer = 0f;
        _scanTimer = 0f;
    }

    private void GetLookRay(out Vector3 origin, out Vector3 direction)
    {
        Transform source = _lookSource;
        if (source == null && Camera.main != null) source = Camera.main.transform;

        if (source != null)
        {
            origin = source.position;
            direction = source.forward;
        }
        else
        {
            origin = transform.position;
            direction = (_castPoint.position - origin).normalized;
        }
    }

    private void Scan()
    {
        Camera cam = ResolveCamera();
        if (cam == null)
        {
            SetState("No camera found. Drag the Camera into Look Source.");
            return;
        }

        int hits = 0;

        for (int i = 0; i < _pointsPerScan; i++)
        {
            Vector3 viewport = new Vector3(
                0.5f + (Random.value - 0.5f) * _viewCoverage,
                0.5f + (Random.value - 0.5f) * _viewCoverage,
                0f);

            Ray ray = cam.ViewportPointToRay(viewport);

            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, _layerMask))
            {
                float distance = hit.distance;
                float captureProbability = 1f;

                if (distance > _distanceThinningStart)
                {
                    captureProbability = Mathf.Clamp01(_distanceThinningStart / distance);
                }

                if (Random.value <= captureProbability)
                {
                    hits++;

                    if (_positionsList.Count < resolution * resolution)
                    {
                        _positionsList.Add(hit.point);
                        _particleAmount++;
                    }
                    else
                    {
                        _createNewVFX = true;
                        CreateNewVFX();
                        break;
                    }
                }
            }
        }

        SetState(hits > 0
            ? "Scanning"
            : "Focus is ready, but the scan rays hit nothing (check Layer Mask)");

        ApplyPositions();
    }

    private void CreateNewVFX()
    {
        if (!_createNewVFX) return;

        _vfxList.Add(_currentVFX);

        _currentVFX = Instantiate(_vfxPrefab, transform.position, Quaternion.identity, _vfxContainer.transform);
        _currentVFX.SetUInt(RESOLUTION_PARAMETER_NAME, (uint)resolution);

        _texture = new Texture2D(resolution, resolution, TextureFormat.RGBAFloat, false);
        _texture.filterMode = FilterMode.Point;
        _texture.wrapMode = TextureWrapMode.Clamp;

        _positions = new Color[resolution * resolution];

        _positionsList.Clear();

        _particleAmount = 0;

        _createNewVFX = false;
    }

    private void ApplyPositions()
    {
        Vector3[] pos = _positionsList.ToArray();

        int loopLength = _texture.width * _texture.height;
        int posListLen = pos.Length;

        for (int i = 0; i < loopLength; i++)
        {
            Color data;
            if (i < posListLen)
            {
                data = new Color(pos[i].x, pos[i].y, pos[i].z, 1);
            }
            else
            {
                data = new Color(HiddenPosition.x, HiddenPosition.y, HiddenPosition.z, 0);
            }
            _positions[i] = data;
        }

        _texture.SetPixels(_positions);
        _texture.Apply();

        _currentVFX.SetTexture(TEXTURE_NAME, _texture);
        _currentVFX.Reinit();
    }

    private void SetState(string state)
    {
        if (!_debug || state == _lastState) return;
        _lastState = state;
        Debug.Log($"[LiDAR] {state}");
    }
}