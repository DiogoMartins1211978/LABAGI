using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class SaveGameData
{
    public const int CurrentVersion = 5;
    public int version = CurrentVersion;
    public int slot;
    public string savedUtc;
    public string scene;
    public string layoutHash;
    public string roomId;
    public Quaternion cameraLocalRotation;
    public List<string> collection = new List<string>();
    public List<string> deck = new List<string>();
    public List<SavedEnemy> enemies = new List<SavedEnemy>();
    public List<string> bestiaryIds = new List<string>();
    public List<SavedRoomScan> lidarRooms = new List<SavedRoomScan>();
    public List<SavedTreasure> treasures = new List<SavedTreasure>();

    [Serializable]
    public sealed class SavedTreasure
    {
        public string roomId;
        public string prefabKey;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public string cardId;
        public bool claimed;
    }

    [Serializable]
    public sealed class SavedRoomScan
    {
        public string roomId;
        public string points;
        public string enemySurfaceSignature;
    }

    [Serializable]
    public sealed class SavedEnemy
    {
        public string roomId;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public bool defeated;
        public int registeredScanPoints;
        public float scanFocusTime;
        public bool discovered;
    }

    public static SaveGameData Parse(string json)
    {
        // Missing fields must not silently become a valid version-one save.
        if (string.IsNullOrWhiteSpace(json) || !json.Contains("\"version\""))
            throw new FormatException("Missing save format version.");
        var data = JsonUtility.FromJson<SaveGameData>(json);
        if (data == null) throw new FormatException("Invalid save file.");
        if (data.version < 1 || data.version > CurrentVersion)
            throw new NotSupportedException("Unsupported save format version. This slot was left unchanged.");
        if (data.slot < 1 || data.slot > 3 || string.IsNullOrWhiteSpace(data.scene)
            || string.IsNullOrWhiteSpace(data.layoutHash) || string.IsNullOrWhiteSpace(data.roomId)
            || !DateTime.TryParse(data.savedUtc, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out _)
            || data.collection == null || data.deck == null || data.enemies == null)
            throw new FormatException("Incomplete save file.");
        if (data.version == 1)
        {
            // Session restoration derives only discoveries supported by this
            // slot's defeated enemies. Shared legacy PlayerPrefs are not imported.
            data.bestiaryIds = new List<string>();
        }
        else
        {
            if (!json.Contains("\"bestiaryIds\"") || data.bestiaryIds == null || data.bestiaryIds.Count > 4096)
                throw new FormatException("Missing or invalid bestiary data.");
            foreach (string id in data.bestiaryIds)
                if (string.IsNullOrWhiteSpace(id)) throw new FormatException("Invalid bestiary ID.");
        }
        if (data.version < 3) data.lidarRooms = new List<SavedRoomScan>();
        else
        {
            if (!json.Contains("\"lidarRooms\"") || data.lidarRooms == null || data.lidarRooms.Count > 4096)
                throw new FormatException("Missing or invalid LiDAR room data.");
            var roomIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var scan in data.lidarRooms)
                if (scan == null || string.IsNullOrWhiteSpace(scan.roomId) || !roomIds.Add(scan.roomId)
                    || string.IsNullOrWhiteSpace(scan.points)) throw new FormatException("Invalid saved LiDAR room.");
        }
        if (data.version < 5) data.treasures = new List<SavedTreasure>();
        else
        {
            if (!json.Contains("\"treasures\"") || data.treasures == null || data.treasures.Count > 4096)
                throw new FormatException("Missing or invalid saved treasure data.");
            var rooms = new HashSet<string>(StringComparer.Ordinal);
            foreach (var treasure in data.treasures)
            {
                if (treasure == null || string.IsNullOrWhiteSpace(treasure.roomId) || !rooms.Add(treasure.roomId)
                    || string.IsNullOrWhiteSpace(treasure.prefabKey)) throw new FormatException("Invalid saved treasure.");
                var p = treasure.localPosition; var q = treasure.localRotation;
                float norm = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
                if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z) || !Finite(norm) || Math.Abs(norm - 1f) > 0.01f)
                    throw new FormatException("Invalid saved treasure placement.");
            }
        }
        return data;
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
