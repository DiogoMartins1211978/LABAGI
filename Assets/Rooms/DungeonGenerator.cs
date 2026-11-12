using System;
using System.Collections.Generic;
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
        public string dir;    // "N", "S", "E", "W"
        public string prefab; // Optional
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
    [Tooltip("Room the player starts in. If not found, the first placed room is used.")]
    [SerializeField] private string _startRoomId = "R01";

    private readonly Dictionary<string, GameObject> _prefabLookup = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, RoomNode> _nodesById = new Dictionary<string, RoomNode>();
    private readonly Dictionary<Vector2Int, RoomNode> _nodesByGrid = new Dictionary<Vector2Int, RoomNode>();

    private void Awake()
    {
        BuildPrefabLookup();
        GenerateDungeon();
    }

    private void BuildPrefabLookup()
    {
        _prefabLookup.Clear();
        foreach (var mapping in _roomPrefabs)
        {
            if (!string.IsNullOrEmpty(mapping.key) && mapping.prefab != null)
                _prefabLookup[mapping.key] = mapping.prefab;
        }
    }

    private void ClearDungeon()
    {
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

        // Place rooms. A room may appear in the JSON before its parent; we retry
        // deferred rooms until nothing more can be placed.
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

        // Link every pair of rooms that sit next to each other on the grid.
        StitchAllAdjacentRooms();

        // Hand the start room to the player.
        RoomNode startNode = null;
        if (!_nodesById.TryGetValue(_startRoomId, out startNode))
        {
            startNode = firstNode;
            if (startNode != null)
                Debug.LogWarning($"[DungeonGenerator] Start room '{_startRoomId}' not found, using first room instead.");
        }

        if (startNode == null) return;

        PlayerNodeMover playerMover = FindAnyObjectByType<PlayerNodeMover>();
        if (playerMover != null)
        {
            playerMover.InitializePlayer(startNode);
        }
        else
        {
            Debug.LogWarning("[DungeonGenerator] No PlayerNodeMover found in the scene.");
        }
    }

    private Vector2 ResolveCellSize(DungeonData data)
    {
        float w = data.cellWidth;
        float d = data.cellDepth;

        // Rooms only touch if the spacing matches their real size.
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
        return node;
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