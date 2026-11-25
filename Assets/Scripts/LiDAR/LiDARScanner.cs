using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;
using Random = UnityEngine.Random;

public partial class LiDARScanner : MonoBehaviour
{
    bool allowEnemyDiscovery = true;

    // VR supplies its tracked camera without editing the desktop prefab references.
    public void ConfigureView(Camera view, PlayerNodeMover player, bool enableEnemyDiscovery = true)
    {
        _camera = view;
        _playerMover = player;
        _lookSource = view.transform;
        _castPoint = view.transform;
        _pulseOrigin = view.transform;
        allowEnemyDiscovery = enableEnemyDiscovery;
        ResetFocus();
    }
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

        public readonly List<SurfacePointAttachment> SurfaceAttachments = new();
    }

    private class SurfacePointAttachment
    {
        public Transform Surface;
        public Vector3 LocalPosition;
        public Vector3 LocalNormal;
        public int PointIndex;
        public Enemy Enemy;
        public Enemy.SurfaceAttachment EnemySurface;
        public bool IsAnimatedEnemy;
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
    [SerializeField] private LayerMask ignoredObjectLayers;
    [SerializeField] private int _pointsPerScan = 40;
    [SerializeField] private int resolution = 256;

    [Header("Scan area")]
    [SerializeField] private Camera _camera;

    [Header("Distance & Density Falloff")]
    [SerializeField] private float _distanceThinningStart = 8f;

    [Header("Focus")]
    [SerializeField] private Transform _lookSource;
    [SerializeField] private float _cameraRotationThresholdDegrees = 0.1f;

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

    private Quaternion _lastCameraRotation;
    private bool _hasLastCameraRotation;


    private const float SURFACE_OFFSET = 0.02f;

    private void Start()
    {
        if (_playerMover == null) _playerMover = FindAnyObjectByType<PlayerNodeMover>();

        if (_vfxContainer == null) _vfxContainer = gameObject;

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
        if (_activeRoom != null) GetOrCreateRoomScan(_activeRoom);

        UpdateRoomVFXVisibility();
    }

    private void Update()
    {
        if (PauseMenuController.BlocksGameplayInput) return;

        UpdatePulse();
        UpdateEnemyFocus();

        if (HasCameraRotated()) _currentScanRadius = _initialScanRadius;
        else _currentScanRadius = Mathf.MoveTowards(_currentScanRadius, 1f, _focusExpansionSpeed * Time.deltaTime);

        _scanTimer += Time.deltaTime;

        if (_scanTimer >= _scanInterval)
        {
            _scanTimer = 0f;
            Scan();
        }
    }

    private bool HasCameraRotated()
    {
        Transform rotationTransform = GetRotationTransform();
        if (rotationTransform == null) return false;

        Quaternion currentRotation = rotationTransform.rotation;

        if (!_hasLastCameraRotation)
        {
            _lastCameraRotation = currentRotation;
            _hasLastCameraRotation = true;
            return false;
        }

        float angleDelta = Quaternion.Angle(_lastCameraRotation, currentRotation);
        _lastCameraRotation = currentRotation;

        return angleDelta > _cameraRotationThresholdDegrees;
    }

    private Transform GetRotationTransform()
    {
        Camera cam = ResolveCamera();
        if (cam != null) return cam.transform;
        if (_lookSource != null) return _lookSource;
        return _castPoint;
    }

    private void LateUpdate()
    {
        if (resolution <= 0) return;

        foreach (var entry in _roomScans)
        {
            RoomScanData roomData = entry.Value;
            if (roomData.SurfaceAttachments.Count == 0) continue;
            bool changed = false;
            foreach (SurfacePointAttachment attachment in roomData.SurfaceAttachments)
            {
                Vector3 position;
                Vector3 normal;

                if (attachment.IsAnimatedEnemy)
                {
                    if (attachment.Enemy == null || !attachment.Enemy.GetAnimatedSurfacePoint(attachment.EnemySurface, out position, out normal))
                    {
                        continue;
                    }
                }
                else
                {
                    if (attachment.Surface == null)
                        continue;

                    position = attachment.Surface.TransformPoint(
                        attachment.LocalPosition
                    );

                    normal = attachment.Surface.TransformDirection(
                        attachment.LocalNormal
                    ).normalized;
                }

                normal.Normalize();

                int index = attachment.PointIndex;

                if (index < 0 || index >= roomData.Positions.Count)
                    continue;

                int x = index % resolution;
                int y = index / resolution;

                Vector3 offsetPosition = position + normal * SURFACE_OFFSET;

                roomData.Positions[index] = offsetPosition;
                roomData.Normals[index] = normal;

                roomData.PositionsTexture.SetPixel(
                    x,
                    y,
                    new Color(
                        offsetPosition.x,
                        offsetPosition.y,
                        offsetPosition.z,
                        1f
                    )
                );

                Vector3 encodedNormal = normal * 0.5f + Vector3.one * 0.5f;

                roomData.NormalsTexture.SetPixel(
                    x,
                    y,
                    new Color(
                        encodedNormal.x,
                        encodedNormal.y,
                        encodedNormal.z,
                        1f
                    )
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
        _hasLastCameraRotation = false;
    }

    private void UpdateEnemyFocus()
    {
        if (!allowEnemyDiscovery) return;
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

            if (!TryGetValidScanHit(ray, out RaycastHit hit))
                continue;

            Enemy enemy = hit.collider.GetComponentInParent<Enemy>();

            if (enemy != null)
            {
                
                if (enemy.Resolved) continue;
                
                RoomNode enemyRoom = enemy.Room != null
                    ? enemy.Room
                    : hit.collider.GetComponentInParent<RoomNode>();

                if (enemyRoom != _activeRoom)
                    continue;

                if (!enemy.TryGetLidarSurfaceHit(ray, out RaycastHit enemyHit))
                    continue;

                Enemy.SurfaceAttachment attachment = enemy.CreateSurfaceAttachment(enemyHit);

                if (AddEnemyScanPoint(
                    _activeRoom,
                    enemy,
                    attachment,
                    enemyHit.point,
                    enemyHit.normal))
                {
                    if (allowEnemyDiscovery && !enemy.Discovered)
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
                captureProbability = Mathf.Clamp01(_distanceThinningStart / hit.distance);
            }

            if (Random.value > captureProbability) continue;

            if (AddScanPoint(_activeRoom, hit.point, hit.normal, hit.collider.transform))
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

        normal.Normalize();

        roomData.Positions.Add(position + normal * SURFACE_OFFSET);
        roomData.Normals.Add(normal);
        roomData.PointSizes.Add(1f);

        roomData.SurfaceAttachments.Add(new SurfacePointAttachment
        {
            Enemy = enemy,
            EnemySurface = attachment,
            IsAnimatedEnemy = true,
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

    private bool AddScanPoint(RoomNode room, Vector3 position, Vector3 normal, Transform surface)
    {
        if (room == null || surface == null || resolution <= 0)
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

        normal.Normalize();

        int pointIndex = roomData.Positions.Count;

        roomData.Positions.Add(position + normal * SURFACE_OFFSET);
        roomData.Normals.Add(normal);
        roomData.PointSizes.Add(1f);

        roomData.SurfaceAttachments.Add(new SurfacePointAttachment
        {
            Surface = surface,
            LocalPosition = surface.InverseTransformPoint(position),
            LocalNormal = surface.InverseTransformDirection(normal).normalized,
            IsAnimatedEnemy = false,
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
        roomData.SizesTexture.Apply();
        roomData.LastAppliedCount = newCount;
    }

    private void SetState(string state)
    {
        if (!_debug || state == _lastState) return;
        _lastState = state;
        Debug.Log($"[LiDAR] {state}");
    }

    private bool HasIgnoredLayerInObject(Transform objectRoot)
    {
        if (objectRoot == null)
            return false;

        foreach (Transform child in objectRoot.GetComponentsInChildren<Transform>(true))
        {
            if ((ignoredObjectLayers.value & (1 << child.gameObject.layer)) != 0)
                return true;
        }

        return false;
    }

    private bool TryGetValidScanHit(Ray ray, out RaycastHit validHit)
    {
        validHit = default;

        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            Mathf.Infinity,
            ~0,
            QueryTriggerInteraction.Ignore
        );

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (HasIgnoredLayerInObject(hit.collider.transform))
                continue;

            validHit = hit;
            return true;
        }

        return false;
    }

    public void RemoveEnemyDots(Enemy enemy)
    {
        if (enemy == null)
            return;

        foreach (var entry in _roomScans)
        {
            RoomScanData roomData = entry.Value;
            bool changed = false;

            for (int i = roomData.SurfaceAttachments.Count - 1; i >= 0; i--)
            {
                SurfacePointAttachment attachment = roomData.SurfaceAttachments[i];

                if (attachment.Enemy != enemy)
                    continue;

                int index = attachment.PointIndex;

                if (index >= 0 && index < roomData.Positions.Count)
                {
                    int x = index % resolution;
                    int y = index / resolution;

                    roomData.Positions[index] = HiddenPosition;
                    roomData.PointSizes[index] = 0f;

                    roomData.PositionsTexture.SetPixel(
                        x, y,
                        new Color(HiddenPosition.x, HiddenPosition.y, HiddenPosition.z, 0f)
                    );

                    roomData.NormalsTexture.SetPixel(
                        x, y,
                        new Color(0.5f, 1f, 0.5f, 0f)
                    );

                    roomData.SizesTexture.SetPixel(
                        x, y,
                        Color.clear
                    );

                    changed = true;
                }

                roomData.SurfaceAttachments.RemoveAt(i);
            }

            if (changed)
            {
                roomData.PositionsTexture.Apply();
                roomData.NormalsTexture.Apply();
                roomData.SizesTexture.Apply();
            }
        }

        Debug.Log($"[LiDAR] Removed dots belonging to {enemy.name}.");
    }
}
