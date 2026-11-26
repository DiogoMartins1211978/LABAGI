using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class DungeonGenerator : MonoBehaviour
{
    [Serializable]
    public class DungeonData
    {
        [Tooltip("Fallback spacing if no cell size can be determined.")]
        public float gridSpacing = 10f;

        [Tooltip("Distance between rooms on X (E/W). 0 = use RoomNode.width of the default prefab.")]
        public float cellWidth = 0f;

        [Tooltip("Distance between rooms on Z (N/S). 0 = use RoomNode.depth of the default prefab.")]
        public float cellDepth = 0f;

        public string defaultPrefab = "Room_Standard";
        public List<RoomEntry> rooms = new List<RoomEntry>();
    }

    [Serializable]
    public class RoomEntry
    {
        public string id;
        public string from;
        public string dir;
        public string prefab;
        public string enemy;
        public bool hasTreasure;
    }

    [Serializable]
    public struct PrefabMapping
    {
        public string key;
        public GameObject prefab;
    }

    [Header("Configuration")]
    [SerializeField] private TextAsset _layoutJson;
    [SerializeField] private List<PrefabMapping> _roomPrefabs = new List<PrefabMapping>();
    [Tooltip("Enemy key (used in the JSON 'enemy' field) -> enemy prefab.")]
    [SerializeField] private List<PrefabMapping> _enemyPrefabs = new List<PrefabMapping>();

    [Header("Enemy placement")]
    [Tooltip("Place each enemy at a random point inside its room. If off, uses the room's enemySpawnPoint (or a default spot).")]
    [SerializeField] private bool _randomEnemySpawn = true;
    [Tooltip("Keeps enemies this far from the room's walls (meters).")]
    [SerializeField] private float _enemyWallMargin = 1f;
    [Tooltip("Keeps enemies at least this far from where the player stands (meters).")]
    [SerializeField] private float _enemyMinDistanceFromPlayer = 3f;
    [Tooltip("Room the player starts in. If not found, the first placed room is used.")]
    [SerializeField] private string _startRoomId = "R01";

    [Header("Treasure")]
    [SerializeField] private List<GameObject> _treasurePrefabs = new List<GameObject>();

    [Header("Treasure placement")]
    [SerializeField] private float _treasureWallMargin = 1f;
    [SerializeField] private float _treasureDoorWidth = 2.5f;
    [SerializeField] private float _treasureDoorDepth = 2.5f;
    [SerializeField] private float _treasureClearance = 0.75f;
    [SerializeField] private int _treasureSpawnAttempts = 50;

    [SerializeField] private float _treasureCenterExclusionRadius = 3f;

    private readonly Dictionary<string, GameObject> _prefabLookup = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, GameObject> _enemyLookup = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, RoomNode> _nodesById = new Dictionary<string, RoomNode>();
    private readonly Dictionary<Vector2Int, RoomNode> _nodesByGrid = new Dictionary<Vector2Int, RoomNode>();

    private PlayerNodeMover _playerMover;

    public sealed class TreasureInstance
    {
        public string prefabKey;
        public GameObject root;
        public RewardChest chest;
    }
    readonly Dictionary<string, TreasureInstance> treasures = new Dictionary<string, TreasureInstance>();
    public IReadOnlyDictionary<string, TreasureInstance> Treasures => treasures;
    public bool IsTreasurePrefabAvailable(string key)
        => _treasurePrefabs.FindAll(prefab => prefab != null && prefab.name == key).Count == 1;

    public IReadOnlyDictionary<string, RoomNode> Rooms => _nodesById;
    public string LayoutHash
    {
        get
        {
            if (_layoutJson == null) return "";
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(_layoutJson.text))).Replace("-", "");
        }
    }

    public string GetRoomId(RoomNode room)
    {
        foreach (var pair in _nodesById)
            if (pair.Value == room) return pair.Key;
        return null;
    }

    public bool TryGetRoom(string roomId, out RoomNode room)
    {
        return _nodesById.TryGetValue(roomId, out room);
    }

    private void Awake()
    {
        BuildPrefabLookup();
        GenerateDungeon();
    }

    private void OnDestroy()
    {
        if (_playerMover != null)
            _playerMover.RoomEntered -= OnRoomEntered;
    }

    private void BuildPrefabLookup()
    {
        _prefabLookup.Clear();
        foreach (var mapping in _roomPrefabs)
        {
            if (!string.IsNullOrEmpty(mapping.key) && mapping.prefab != null)
                _prefabLookup[mapping.key] = mapping.prefab;
        }

        _enemyLookup.Clear();
        foreach (var mapping in _enemyPrefabs)
        {
            if (!string.IsNullOrEmpty(mapping.key) && mapping.prefab != null)
                _enemyLookup[mapping.key] = mapping.prefab;
        }

}

    private void ClearDungeon()
    {
        treasures.Clear();
        if (_playerMover != null)
        {
            _playerMover.RoomEntered -= OnRoomEntered;
            _playerMover = null;
        }

        foreach (var node in _nodesById.Values)
        {
            if (node != null) Destroy(node.gameObject);
        }
        _nodesById.Clear();
        _nodesByGrid.Clear();
    }

    public void GenerateDungeon()
    {
        if (_layoutJson == null)
        {
            Debug.LogError("[DungeonGenerator] No JSON file assigned!");
            return;
        }

        DungeonData data = JsonUtility.FromJson<DungeonData>(_layoutJson.text);
        if (data == null || data.rooms == null || data.rooms.Count == 0)
        {
            Debug.LogWarning("[DungeonGenerator] JSON has no rooms.");
            return;
        }

        ClearDungeon();

        Vector2 cell = ResolveCellSize(data);
        var gridCoords = new Dictionary<string, Vector2Int>();
        RoomNode firstNode = null;

        List<RoomEntry> pending = new List<RoomEntry>(data.rooms);
        while (pending.Count > 0)
        {
            var deferred = new List<RoomEntry>();

            foreach (RoomEntry entry in pending)
            {
                bool isRoot = string.IsNullOrEmpty(entry.from);
                if (!isRoot && !gridCoords.ContainsKey(entry.from))
                {
                    deferred.Add(entry);
                    continue;
                }

                RoomNode node = PlaceRoom(entry, data, cell, gridCoords);
                if (node != null && firstNode == null) firstNode = node;
            }

            if (deferred.Count == pending.Count)
            {
                foreach (RoomEntry e in deferred)
                    Debug.LogWarning($"[DungeonGenerator] Room '{e.id}' skipped: parent '{e.from}' was never placed.");
                break;
            }

            pending = deferred;
        }

        StitchAllAdjacentRooms();
        
        foreach (RoomEntry entry in data.rooms)
        {
            if (!entry.hasTreasure)
                continue;

            if (_nodesById.TryGetValue(entry.id, out RoomNode room))
                SpawnTreasure(room);
        }

        RoomNode startNode = null;
        if (!_nodesById.TryGetValue(_startRoomId, out startNode))
        {
            startNode = firstNode;
            if (startNode != null)
                Debug.LogWarning($"[DungeonGenerator] Start room '{_startRoomId}' not found, using first room instead.");
        }

        if (startNode == null) return;

        _playerMover = FindAnyObjectByType<PlayerNodeMover>();

        if (_playerMover != null)
        {
            _playerMover.RoomEntered -= OnRoomEntered;
            _playerMover.RoomEntered += OnRoomEntered;

            _playerMover.InitializePlayer(startNode);
        }
        else
        {
            Debug.LogWarning("[DungeonGenerator] No PlayerNodeMover found in the scene.");
        }
    }

    private void UpdateRoomVisibility(RoomNode activeRoom)
    {
        foreach (RoomNode room in _nodesById.Values)
        {
            if (room == null)
                continue;

            bool isActiveRoom = room == activeRoom;

            bool isAdjacentRoom =
                room == activeRoom.forward ||
                room == activeRoom.back ||
                room == activeRoom.left ||
                room == activeRoom.right;

            room.SetRoomVisible(isActiveRoom || isAdjacentRoom);

            if (room.enemy != null)
            {
                if (!room.enemy.Resolved && isAdjacentRoom) room.enemy.StartPresenceAudio();
                else room.enemy.StopPresenceAudio();
            }

            if (room.treasure != null)
            {
                if (!room.treasure.IsClaimed && (isActiveRoom || isAdjacentRoom)) room.treasure.StartTreasureAudio();
                else room.treasure.StopTreasureAudio();
            }
        }
    }



    private void OnRoomEntered(RoomNode activeRoom)
    {
        UpdateRoomVisibility(activeRoom);

        foreach (RoomNode room in _nodesById.Values)
        {
            if (room == null)
                continue;

            RoomAudioController audio =
                room.GetComponent<RoomAudioController>();

            if (audio == null)
                continue;

            bool isActiveRoom = room == activeRoom;

            bool isAdjacentRoom = room == activeRoom.forward || room == activeRoom.back || room == activeRoom.left || room == activeRoom.right;

            if (isActiveRoom)
            {
                bool previouslyVisited = room.MarkVisited();
                audio.EnterRoom(previouslyVisited);
            }
            else if (isAdjacentRoom) audio.EnterAdjacentRoom(room.HasBeenVisited);
            else audio.LeaveRoom();

            // debugging
            if (isAdjacentRoom) Debug.Log($"[Room Audio] Adjacent: {room.name} | Visited: {room.HasBeenVisited} | Audio Controller: {audio != null}");
        }
    }



    private Vector2 ResolveCellSize(DungeonData data)
    {
        float w = data.cellWidth;
        float d = data.cellDepth;

        if ((w <= 0f || d <= 0f) && _prefabLookup.TryGetValue(data.defaultPrefab, out GameObject prefab))
        {
            RoomNode rn = prefab.GetComponent<RoomNode>();
            if (rn != null)
            {
                if (w <= 0f) w = rn.width;
                if (d <= 0f) d = rn.depth;
            }
        }

        if (w <= 0f) w = data.gridSpacing;
        if (d <= 0f) d = data.gridSpacing;
        return new Vector2(w, d);
    }

    private RoomNode PlaceRoom(RoomEntry entry, DungeonData data, Vector2 cell, Dictionary<string, Vector2Int> gridCoords)
    {
        if (string.IsNullOrEmpty(entry.id))
        {
            Debug.LogWarning("[DungeonGenerator] Room entry without an id was skipped.");
            return null;
        }

        if (_nodesById.ContainsKey(entry.id))
        {
            Debug.LogWarning($"[DungeonGenerator] Duplicate room id '{entry.id}' skipped.");
            return null;
        }

        Vector2Int coord = Vector2Int.zero;
        if (!string.IsNullOrEmpty(entry.from))
            coord = gridCoords[entry.from] + DirectionToOffset(entry.dir);

        if (_nodesByGrid.ContainsKey(coord))
        {
            Debug.LogWarning($"[DungeonGenerator] Overlapping room at grid {coord} for room ID '{entry.id}'.");
            return null;
        }

        string prefabKey = string.IsNullOrEmpty(entry.prefab) ? data.defaultPrefab : entry.prefab;
        if (!_prefabLookup.TryGetValue(prefabKey, out GameObject prefabToSpawn))
        {
            Debug.LogError($"[DungeonGenerator] Missing prefab mapping for key '{prefabKey}'");
            return null;
        }

        Vector3 worldPos = transform.position + new Vector3(coord.x * cell.x, 0f, coord.y * cell.y);
        GameObject roomObj = Instantiate(prefabToSpawn, worldPos, Quaternion.identity, transform);
        roomObj.name = $"{entry.id}_{prefabKey}";

        RoomNode node = roomObj.GetComponent<RoomNode>();
        if (node == null) node = roomObj.AddComponent<RoomNode>();

        gridCoords[entry.id] = coord;
        _nodesById[entry.id] = node;
        _nodesByGrid[coord] = node;

        SpawnEnemy(entry, node);
        return node;
    }

    private void SpawnEnemy(RoomEntry entry, RoomNode room)
    {
        if (string.IsNullOrEmpty(entry.enemy)) return;

        bool isMultiplayer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (isMultiplayer && !NetworkManager.Singleton.IsServer) return;

        if (!_enemyLookup.TryGetValue(entry.enemy, out GameObject enemyPrefab))
        {
            Debug.LogError($"[DungeonGenerator] Missing enemy prefab mapping for key '{entry.enemy}' (room '{entry.id}')");
            return;
        }

        Vector3 pos;

        if (_randomEnemySpawn) pos = RandomPointInRoom(room);
        else pos = room.enemySpawnPoint != null ? room.enemySpawnPoint.position : room.transform.TransformPoint(new Vector3(0f, 0f, room.depth * 0.25f));

        Vector3 toPlayer = room.AnchorPosition - pos;
        toPlayer.y = 0f;

        Quaternion rot = toPlayer.sqrMagnitude > 0.001f ? Quaternion.LookRotation(toPlayer) : Quaternion.identity;

        GameObject enemyObj = isMultiplayer ? Instantiate(enemyPrefab, pos, rot) : Instantiate(enemyPrefab, pos, rot, room.transform);
        enemyObj.name = $"{entry.id}_Enemy_{entry.enemy}";
        Enemy enemy = enemyObj.GetComponent<Enemy>();

        if (enemy == null) enemy = enemyObj.AddComponent<Enemy>();

        enemy.Room = room;
        room.enemy = enemy;

        if (!isMultiplayer) return;

        NetworkObject networkObject = enemyObj.GetComponent<NetworkObject>();

        NetworkEnemyState networkState = enemyObj.GetComponent<NetworkEnemyState>();

        if (networkObject == null || networkState == null)
        {
            Debug.LogError($"[DungeonGenerator] Multiplayer enemy prefab '{enemyPrefab.name}' must have NetworkObject and NetworkEnemyState on its root.");
            Destroy(enemyObj);
            room.enemy = null;
            return;
        }

        string roomId = GetRoomId(room);

        if (string.IsNullOrEmpty(roomId))
        {
            Debug.LogError($"[DungeonGenerator] Could not get room ID for enemy '{enemyObj.name}'.");

            Destroy(enemyObj);
            room.enemy = null;
            return;
        }

        networkState.SetRoomIdBeforeSpawn(roomId);
        networkObject.Spawn();

        Debug.Log($"[DungeonGenerator] Host spawned network enemy '{enemyObj.name}' in room '{roomId}'.");
    }

    private Vector3 RandomPointInRoom(RoomNode room)
    {
        float halfW = Mathf.Max(0f, room.width * 0.5f - _enemyWallMargin);
        float halfD = Mathf.Max(0f, room.depth * 0.5f - _enemyWallMargin);
        Vector3 playerLocal = room.transform.InverseTransformPoint(room.AnchorPosition);

        Vector3 local = Vector3.zero;
        for (int i = 0; i < 20; i++) 
        {
            local = new Vector3(
                UnityEngine.Random.Range(-halfW, halfW),
                0f,
                UnityEngine.Random.Range(-halfD, halfD));

            if (Vector3.Distance(local, playerLocal) >= _enemyMinDistanceFromPlayer) break;
        }

        return room.transform.TransformPoint(local);
    }

    private void SpawnTreasure(RoomNode room)
    {
        string roomId = GetRoomId(room);
        var pending = SaveGames.LaunchPending ? SaveGames.PendingLoad : null;
        SaveGameData.SavedTreasure saved = null;
        if (pending != null && pending.version >= 5)
        {
            saved = pending.treasures.Find(item => item.roomId == roomId);
            if (saved == null) return;
        }
        if (_treasurePrefabs == null || _treasurePrefabs.Count == 0)
        {
            Debug.LogWarning("[DungeonGenerator] No treasure prefabs assigned.");
            return;
        }

        List<GameObject> validPrefabs = _treasurePrefabs.FindAll(prefab => prefab != null);

        if (validPrefabs.Count == 0)
        {
            Debug.LogWarning("[DungeonGenerator] Treasure prefab list contains no valid prefabs.");
            return;
        }

        GameObject selectedPrefab;
        if (saved != null)
        {
            var matches = validPrefabs.FindAll(prefab => prefab.name == saved.prefabKey);
            if (matches.Count != 1)
            {
                Debug.LogError("[Save] Saved treasure prefab is missing or ambiguous: " + saved.prefabKey);
                return;
            }
            selectedPrefab = matches[0];
        }
        else selectedPrefab = validPrefabs[UnityEngine.Random.Range(0, validPrefabs.Count)];

        Vector3 position;
        if (saved != null) position = room.transform.TransformPoint(saved.localPosition);
        else if (!TryGetTreasurePosition(room, out position))
        {
            Debug.LogWarning(
                $"[DungeonGenerator] Could not find a valid treasure position in room '{room.name}'."
            );
            return;
        }

        Vector3 directionToCenter = room.transform.position - position;
        directionToCenter.y = 0f;

        Quaternion chestRotation = directionToCenter.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(directionToCenter.normalized, Vector3.up)
            : room.transform.rotation;
        if (saved != null) chestRotation = room.transform.rotation * saved.localRotation;

        GameObject treasure = Instantiate(
            selectedPrefab,
            position,
            chestRotation,
            room.transform
        );

        treasure.name = $"{room.name}_Treasure_{selectedPrefab.name}";
        var chests = treasure.GetComponentsInChildren<RewardChest>(true);
        if (chests.Length != 1)
        {
            Debug.LogError("[Save] Each treasure prefab must contain exactly one RewardChest: " + selectedPrefab.name);
            return;
        }
        RewardChest chest = chests[0];
        treasures.Add(roomId, new TreasureInstance
        {
            prefabKey = selectedPrefab.name,
            root = treasure,
            chest = chest
        });
        room.treasure = chest;
    }

    private bool TryGetTreasurePosition(RoomNode room, out Vector3 worldPosition)
    {
        float halfW = room.width * 0.5f - _treasureWallMargin;
        float halfD = room.depth * 0.5f - _treasureWallMargin;

        worldPosition = Vector3.zero;

        if (halfW <= 0f || halfD <= 0f)
            return false;

        float doorHalfWidth = _treasureDoorWidth * 0.5f + _treasureClearance;
        float exclusionRadiusSqr = _treasureCenterExclusionRadius * _treasureCenterExclusionRadius;

        for (int i = 0; i < _treasureSpawnAttempts; i++)
        {
            Vector3 localPosition = new Vector3(
                UnityEngine.Random.Range(-halfW, halfW),
                0f,
                UnityEngine.Random.Range(-halfD, halfD)
            );

            if (localPosition.sqrMagnitude < exclusionRadiusSqr)
                continue;

            if (IsNearDoorway(room, localPosition, doorHalfWidth))
                continue;

            worldPosition = room.transform.TransformPoint(localPosition);
            return true;
        }

        return false;
    }

    private bool IsNearDoorway(RoomNode room, Vector3 localPosition, float doorHalfWidth)
    {
        float halfW = room.width * 0.5f;
        float halfD = room.depth * 0.5f;

        // Forward doorway (+Z)
        if (room.forward != null &&
            Mathf.Abs(localPosition.x) < doorHalfWidth &&
            localPosition.z > halfD - _treasureDoorDepth)
            return true;

        // Back doorway (-Z)
        if (room.back != null &&
            Mathf.Abs(localPosition.x) < doorHalfWidth &&
            localPosition.z < -halfD + _treasureDoorDepth)
            return true;

        // Right doorway (+X)
        if (room.right != null &&
            Mathf.Abs(localPosition.z) < doorHalfWidth &&
            localPosition.x > halfW - _treasureDoorDepth)
            return true;

        // Left doorway (-X)
        if (room.left != null &&
            Mathf.Abs(localPosition.z) < doorHalfWidth &&
            localPosition.x < -halfW + _treasureDoorDepth)
            return true;

        return false;
    }

    private void StitchAllAdjacentRooms()
    {
        foreach (var kvp in _nodesByGrid)
        {
            Vector2Int pos = kvp.Key;
            RoomNode room = kvp.Value;

            room.forward = _nodesByGrid.TryGetValue(pos + Vector2Int.up, out RoomNode n) ? n : null;
            room.back = _nodesByGrid.TryGetValue(pos + Vector2Int.down, out RoomNode s) ? s : null;
            room.right = _nodesByGrid.TryGetValue(pos + Vector2Int.right, out RoomNode e) ? e : null;
            room.left = _nodesByGrid.TryGetValue(pos + Vector2Int.left, out RoomNode w) ? w : null;
        }
    }

    private Vector2Int DirectionToOffset(string dir)
    {
        if (string.IsNullOrEmpty(dir)) return Vector2Int.up;
        return dir.ToUpper() switch
        {
            "N" => Vector2Int.up,    // +Z
            "S" => Vector2Int.down,  // -Z
            "E" => Vector2Int.right, // +X
            "W" => Vector2Int.left,  // -X
            _ => Vector2Int.up
        };
    }
}
