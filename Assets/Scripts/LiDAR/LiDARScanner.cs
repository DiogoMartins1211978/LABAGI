using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.VFX;
using Random = UnityEngine.Random;

public class LiDARScanner : MonoBehaviour
{
    private class RoomScanData
    {
        public VisualEffect VFX;
        public Texture2D PositionsTexture;
        public Texture2D NormalsTexture;
        public readonly List<Vector3> Positions = new();
        public readonly List<Vector3> Normals = new();
        public readonly Dictionary<Vector3Int, List<Vector3>> SpatialCells = new();
        public int LastAppliedCount;
        public Texture2D SizesTexture;

        public readonly List<float> PointSizes = new();

        public readonly List<EnemyPointAttachment> EnemyAttachments = new();
    }

    private class EnemyPointAttachment
    {
        public Enemy Enemy;
        public Enemy.SurfaceAttachment Surface;
        public int PointIndex;
    }

    private readonly Dictionary<RoomNode, RoomScanData> _roomScans = new();

    private RoomNode _activeRoom;
    private Enemy _focusedEnemy;

    private const string NORMALS_TEXTURE_NAME = "NormalsTexture";
    private const string TEXTURE_NAME = "PositionsTexture";
    private const string RESOLUTION_PARAMETER_NAME = "Resolution";

    private static readonly Vector3 HiddenPosition = new Vector3(0f, -100000f, 0f);

    [Header("References")]
    [SerializeField] private PlayerNodeMover _playerMover;
    [SerializeField] private VisualEffect _vfxPrefab;
    [SerializeField] private GameObject _vfxContainer;

    [Tooltip("Only used as a fallback for the look direction when there is no camera")]
    [SerializeField] private Transform _castPoint;

    [Header("Scan settings")]
    [SerializeField] private int _pointsPerScan = 40;
    [SerializeField] private int resolution = 256;

    [Header("Scan area")]
    [SerializeField] private Camera _camera;


    [Header("Distance & Density Falloff")]
    [SerializeField] private float _distanceThinningStart = 8f;

    [Header("Focus")]
    [SerializeField] private Transform _lookSource;

    [Header("Scan speed")]
    [SerializeField] private float _scanInterval = 0.1f;

    [Header("Debug")]
    [SerializeField] private bool _debug = true;

    [Header("Focus Scan")]
    [SerializeField] private float _initialScanRadius = 0.05f;
    [SerializeField] private float _focusExpansionSpeed = 0.35f;
    [SerializeField] private float _minimumPointSpacing = 0.04f;

    [Header("Point Pulse")]
    [SerializeField] private Transform _pulseOrigin;
    [SerializeField] private float _pulseRange = 12f;
    [SerializeField] private float _pulseSpeed = 4f;
    [SerializeField] private float _pulseWidth = 0.8f;
    [SerializeField] private float _pulseBoost = 2f;

    private float _currentScanRadius = 0f;
    private float _scanTimer;
    private string _lastState;

    private void Start()
    {
        if (_playerMover == null)
            _playerMover = FindAnyObjectByType<PlayerNodeMover>();

        if (_vfxContainer == null)
            _vfxContainer = gameObject;

        if (_playerMover == null)
        {
            Debug.LogError("[LiDAR] No PlayerNodeMover found.");
            enabled = false;
            return;
        }

        if (_vfxPrefab == null)
        {
            Debug.LogError("[LiDAR] No VFX prefab assigned.");
            enabled = false;
            return;
        }

        SubscribeToPlayer();

        _activeRoom = _playerMover.CurrentRoom;
        if (_activeRoom != null)
            GetOrCreateRoomScan(_activeRoom);

        UpdateRoomVFXVisibility();
    }

    private void Update()
    {
        if (PauseMenuController.BlocksGameplayInput)
            return;

        UpdatePulse();
        UpdateEnemyFocus();

        Mouse mouse = Mouse.current;

        if (mouse == null)
            return;

        Vector2 mouseDelta = mouse.delta.ReadValue();
        bool mouseMoved = mouseDelta.sqrMagnitude > 0.25f;

        if (mouseMoved)
        {
            _currentScanRadius = _initialScanRadius;
        }
        else
        {
            _currentScanRadius = Mathf.MoveTowards(
                _currentScanRadius,
                1f,
                _focusExpansionSpeed * Time.deltaTime
            );
        }

        _scanTimer += Time.deltaTime;

        if (_scanTimer >= _scanInterval)
        {
            _scanTimer = 0f;
            Scan();
        }
    }

    private void LateUpdate()
    {
        foreach (var entry in _roomScans)
        {
            RoomScanData roomData = entry.Value;

            if (roomData.EnemyAttachments.Count == 0)
                continue;

            bool changed = false;

            foreach (EnemyPointAttachment attachment in roomData.EnemyAttachments)
            {
                if (attachment.Enemy == null)
                    continue;

                if (!attachment.Enemy.GetAnimatedSurfacePoint(
                    attachment.Surface,
                    out Vector3 position,
                    out Vector3 normal))
                    continue;

                int index = attachment.PointIndex;
                int x = index % resolution;
                int y = index / resolution;

                roomData.Positions[index] = position;
                roomData.Normals[index] = normal;

                roomData.PositionsTexture.SetPixel(
                    x, y,
                    new Color(position.x, position.y, position.z, 1f)
                );

                Vector3 encodedNormal = normal * 0.5f + Vector3.one * 0.5f;

                roomData.NormalsTexture.SetPixel(
                    x, y,
                    new Color(encodedNormal.x, encodedNormal.y, encodedNormal.z, 1f)
                );

                changed = true;
            }

            if (changed)
            {
                roomData.PositionsTexture.Apply();
                roomData.NormalsTexture.Apply();
            }
        }
    }

    private void OnDestroy()
    {
        if (_playerMover != null)
            _playerMover.RoomEntered -= OnRoomEntered;

        foreach (RoomScanData roomData in _roomScans.Values)
        {
            if (roomData == null)
                continue;

            if (roomData.PositionsTexture != null)
                Destroy(roomData.PositionsTexture);

            if (roomData.NormalsTexture != null)
                Destroy(roomData.NormalsTexture);

            if (roomData.SizesTexture != null)
                Destroy(roomData.SizesTexture);
        }
    }

    private void UpdatePulse()
    {
        Transform origin = _pulseOrigin != null
            ? _pulseOrigin
            : _playerMover.transform;

        float pulseRadius = Mathf.Repeat(
            Time.time * _pulseSpeed,
            _pulseRange
        );

        Vector3 originPosition = origin.position;

        foreach (var entry in _roomScans)
        {
            if (!IsRoomVisible(entry.Key))
                continue;

            VisualEffect vfx = entry.Value.VFX;
            if (vfx == null)
                continue;

            vfx.SetVector3("PulseOrigin", originPosition);
            vfx.SetFloat("PulseRadius", pulseRadius);
            vfx.SetFloat("PulseWidth", _pulseWidth);
            vfx.SetFloat("PulseBoost", _pulseBoost);
        }
    }

    private void SubscribeToPlayer()
    {
        _playerMover.RoomEntered -= OnRoomEntered;
        _playerMover.RoomEntered += OnRoomEntered;
    }

    private void OnRoomEntered(RoomNode room)
    {
        _activeRoom = room;
        ResetFocus();

        if (_activeRoom != null)
            GetOrCreateRoomScan(_activeRoom);

        UpdateRoomVFXVisibility();
        SetState($"Entered room: {room.name}");
    }

    private bool IsRoomVisible(RoomNode room)
    {
        if (_activeRoom == null || room == null)
            return false;

        return room == _activeRoom ||
               room == _activeRoom.forward ||
               room == _activeRoom.back ||
               room == _activeRoom.left ||
               room == _activeRoom.right;
    }

    private void UpdateRoomVFXVisibility()
    {
        foreach (var entry in _roomScans)
        {
            bool isVisible = IsRoomVisible(entry.Key);
            VisualEffect vfx = entry.Value.VFX;

            if (vfx == null)
                continue;

            bool wasActive = vfx.gameObject.activeSelf;
            vfx.gameObject.SetActive(isVisible);

            if (isVisible && !wasActive)
                vfx.Play();
        }
    }

    private Camera ResolveCamera()
    {
        if (_camera != null) return _camera;
        if (_lookSource != null) _camera = _lookSource.GetComponent<Camera>();
        if (_camera == null) _camera = Camera.main;
        return _camera;
    }

    private void ResetFocus()
    {
        _scanTimer = 0f;
        _currentScanRadius = _initialScanRadius;
    }

    private void UpdateEnemyFocus()
    {
        Camera cam = ResolveCamera();

        if (cam == null)
            return;

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        Enemy target = null;

        if (Physics.Raycast(
            ray,
            out RaycastHit hit,
            Mathf.Infinity,
            ~0,
            QueryTriggerInteraction.Ignore))
        {
            target = hit.collider.GetComponentInParent<Enemy>();
        }

        if (target != null &&
            !target.Resolved &&
            !target.Discovered &&
            (target.Room == null || target.Room == _activeRoom) &&
            target.IsSeenBy(cam.transform))
        {
            if (_focusedEnemy != null && _focusedEnemy != target)
                _focusedEnemy.UpdateGaze(false, Time.deltaTime);

            _focusedEnemy = target;
            _focusedEnemy.UpdateGaze(true, Time.deltaTime);
        }
        else
        {
            if (_focusedEnemy != null)
                _focusedEnemy.UpdateGaze(false, Time.deltaTime);

            _focusedEnemy = null;
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

        if (_activeRoom == null)
            return;

        int hits = 0;
        bool roomChanged = false;

        for (int i = 0; i < _pointsPerScan; i++)
        {
            float angle = Random.value * Mathf.PI * 2f;

            float horizontalLimit = 0.5f * cam.aspect / Mathf.Abs(Mathf.Cos(angle));
            float verticalLimit = 0.5f / Mathf.Abs(Mathf.Sin(angle));

            float screenLimit = Mathf.Min(
                horizontalLimit,
                verticalLimit
            );

            float scanLimit = Mathf.Lerp(
                _initialScanRadius,
                screenLimit,
                _currentScanRadius
            );

            float radius = Mathf.Sqrt(Random.value) * scanLimit;

            Vector3 viewport = new Vector3(
                0.5f + Mathf.Cos(angle) * radius / cam.aspect,
                0.5f + Mathf.Sin(angle) * radius,
                0f
            );

            Ray ray = cam.ViewportPointToRay(viewport);

            if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, ~0, QueryTriggerInteraction.Ignore))
                continue;

            Enemy enemy = hit.collider.GetComponentInParent<Enemy>();

            if (enemy != null)
            {
                RoomNode enemyRoom = enemy.Room != null
                    ? enemy.Room
                    : hit.collider.GetComponentInParent<RoomNode>();

                if (enemyRoom != _activeRoom) continue;

                Enemy.SurfaceAttachment attachment = enemy.CreateSurfaceAttachment(hit);

                if (AddEnemyScanPoint(
                    _activeRoom,
                    enemy,
                    attachment,
                    hit.point,
                    hit.normal))
                {
                    if (!enemy.Discovered)
                        enemy.RegisterScanPoint();

                    hits++;
                    roomChanged = true;
                }

                continue;
            }


            RoomNode hitRoom = hit.collider.GetComponentInParent<RoomNode>();

            if (hitRoom != _activeRoom) continue;

            float captureProbability = 1f;

            if (hit.distance > _distanceThinningStart)
            {
                captureProbability = Mathf.Clamp01( _distanceThinningStart / hit.distance );
            }

            if (Random.value > captureProbability) continue;

            if (AddScanPoint(_activeRoom, hit.point, hit.normal))
            {
                hits++;
                roomChanged = true;
            }
        }

        if (roomChanged) ApplyRoomTextures(_activeRoom);

        SetState(hits > 0 ? "Scanning" : "No valid scan hits in this room.");
    }

    private bool AddEnemyScanPoint(RoomNode room, Enemy enemy, Enemy.SurfaceAttachment attachment, Vector3 position, Vector3 normal)
    {
        if (room == null || enemy == null || resolution <= 0)
            return false;

        RoomScanData roomData = GetOrCreateRoomScan(room);

        if (roomData.Positions.Count >= resolution * resolution)
            return false;

        if (IsTooClose(roomData, position))
            return false;

        int pointIndex = roomData.Positions.Count;

        roomData.Positions.Add(position);
        roomData.Normals.Add(normal.normalized);
        roomData.PointSizes.Add(0.35f);

        roomData.EnemyAttachments.Add(new EnemyPointAttachment
        {
            Enemy = enemy,
            Surface = attachment,
            PointIndex = pointIndex
        });

        Vector3Int cell = GetCell(position);

        if (!roomData.SpatialCells.TryGetValue(cell, out List<Vector3> cellPoints))
        {
            cellPoints = new List<Vector3>(4);
            roomData.SpatialCells.Add(cell, cellPoints);
        }

        cellPoints.Add(position);

        return true;
    }

    private Vector3Int GetCell(Vector3 position)
    {
        float cellSize = Mathf.Max(0.001f, _minimumPointSpacing);
        return new Vector3Int(
            Mathf.FloorToInt(position.x / cellSize),
            Mathf.FloorToInt(position.y / cellSize),
            Mathf.FloorToInt(position.z / cellSize)
        );
    }

    private bool IsTooClose(RoomScanData roomData, Vector3 position)
    {
        if (_minimumPointSpacing <= 0f)
            return false;

        float minimumSpacingSqr = _minimumPointSpacing * _minimumPointSpacing;
        Vector3Int center = GetCell(position);

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    Vector3Int cell = new Vector3Int(center.x + dx, center.y + dy, center.z + dz);
                    if (!roomData.SpatialCells.TryGetValue(cell, out List<Vector3> points))
                        continue;

                    for (int i = 0; i < points.Count; i++)
                    {
                        if ((points[i] - position).sqrMagnitude < minimumSpacingSqr)
                            return true;
                    }
                }
            }
        }

        return false;
    }

    private RoomScanData GetOrCreateRoomScan(RoomNode room)
    {
        if (_roomScans.TryGetValue(room, out RoomScanData existing))
            return existing;

        VisualEffect vfx = Instantiate(_vfxPrefab, _vfxContainer.transform);
        vfx.name = $"LiDAR_{room.name}";
        vfx.gameObject.SetActive(true);

        int totalPixels = resolution * resolution;

        Texture2D positionsTexture = new Texture2D(
            resolution,
            resolution,
            TextureFormat.RGBAFloat,
            false,
            true
        );
        positionsTexture.name = $"{room.name}_Positions";
        positionsTexture.filterMode = FilterMode.Point;
        positionsTexture.wrapMode = TextureWrapMode.Clamp;

        Color[] posPixels = new Color[totalPixels];
        Color hiddenPos = new Color(HiddenPosition.x, HiddenPosition.y, HiddenPosition.z, 0f);
        for (int i = 0; i < totalPixels; i++)
            posPixels[i] = hiddenPos;

        positionsTexture.SetPixels(posPixels);
        positionsTexture.Apply();

        Texture2D normalsTexture = new Texture2D(
            resolution,
            resolution,
            TextureFormat.RGBAFloat,
            false,
            true
        );
        normalsTexture.name = $"{room.name}_Normals";
        normalsTexture.filterMode = FilterMode.Point;
        normalsTexture.wrapMode = TextureWrapMode.Clamp;

        Color[] normPixels = new Color[totalPixels];
        Color defaultNorm = new Color(0.5f, 1f, 0.5f, 0f);
        for (int i = 0; i < totalPixels; i++)
            normPixels[i] = defaultNorm;

        normalsTexture.SetPixels(normPixels);
        normalsTexture.Apply();

        Texture2D sizesTexture = new Texture2D(
            resolution,
            resolution,
            TextureFormat.RGBAFloat,
            false,
            true
        );

        sizesTexture.name = $"{room.name}_Sizes";
        sizesTexture.filterMode = FilterMode.Point;
        sizesTexture.wrapMode = TextureWrapMode.Clamp;

        Color[] sizePixels = new Color[totalPixels];

        for (int i = 0; i < totalPixels; i++)
            sizePixels[i] = Color.white;

        sizesTexture.SetPixels(sizePixels);
        sizesTexture.Apply();

        if (vfx.HasInt(RESOLUTION_PARAMETER_NAME))
            vfx.SetInt(RESOLUTION_PARAMETER_NAME, resolution);

        if (vfx.HasTexture(TEXTURE_NAME))
            vfx.SetTexture(TEXTURE_NAME, positionsTexture);

        if (vfx.HasTexture(NORMALS_TEXTURE_NAME))
            vfx.SetTexture(NORMALS_TEXTURE_NAME, normalsTexture);

        if (vfx.HasTexture("SizesTexture"))
            vfx.SetTexture("SizesTexture", sizesTexture);

        vfx.Play();

        RoomScanData roomData = new RoomScanData
        {
            VFX = vfx,
            PositionsTexture = positionsTexture,
            NormalsTexture = normalsTexture,
            SizesTexture = sizesTexture,
            LastAppliedCount = 0
        };

        _roomScans.Add(room, roomData);
        return roomData;
    }

    private bool AddScanPoint(RoomNode room, Vector3 position, Vector3 normal)
    {
        if (room == null || resolution <= 0)
            return false;

        int maxPointsPerRoom = resolution * resolution;
        RoomScanData roomData = GetOrCreateRoomScan(room);

        if (roomData.Positions.Count >= maxPointsPerRoom)
        {
            SetState($"Room scan full: {roomData.Positions.Count}/{maxPointsPerRoom}");
            return false;
        }

        if (IsTooClose(roomData, position))
            return false;

        roomData.Positions.Add(position);
        roomData.Normals.Add(normal.normalized);
        roomData.PointSizes.Add(1f);

        Vector3Int cell = GetCell(position);
        if (!roomData.SpatialCells.TryGetValue(cell, out List<Vector3> cellPoints))
        {
            cellPoints = new List<Vector3>(4);
            roomData.SpatialCells.Add(cell, cellPoints);
        }
        cellPoints.Add(position);

        return true;
    }

    private void ApplyRoomTextures(RoomNode room)
    {
        if (room == null || !_roomScans.TryGetValue(room, out RoomScanData roomData))
            return;

        if (roomData.VFX == null || roomData.PositionsTexture == null || roomData.NormalsTexture == null || roomData.SizesTexture == null)
            return;

        int newCount = roomData.Positions.Count;
        if (roomData.LastAppliedCount >= newCount)
            return;

        for (int i = roomData.LastAppliedCount; i < newCount; i++)
        {
            int x = i % resolution;
            int y = i / resolution;

            Vector3 position = roomData.Positions[i];
            roomData.PositionsTexture.SetPixel(x, y, new Color(position.x, position.y, position.z, 1f));

            Vector3 normal = roomData.Normals[i];
            Vector3 encodedNormal = normal * 0.5f + Vector3.one * 0.5f;
            roomData.NormalsTexture.SetPixel(x, y, new Color(encodedNormal.x, encodedNormal.y, encodedNormal.z, 1f));

            float pointSize = roomData.PointSizes[i];
            roomData.SizesTexture.SetPixel(
                x,
                y,
                new Color(pointSize, 0f, 0f, 1f)
            );
        }

        roomData.PositionsTexture.Apply();
        roomData.NormalsTexture.Apply();
        roomData.LastAppliedCount = newCount;
    }

    private void SetState(string state)
    {
        if (!_debug || state == _lastState) return;
        _lastState = state;
        Debug.Log($"[LiDAR] {state}");
    }
}