using System;
using System.Collections.Generic;
using UnityEngine;

public partial class LiDARScanner
{
    public List<SaveGameData.SavedRoomScan> CaptureRoomScans(DungeonGenerator dungeon)
    {
        EnsurePersistenceReady();
        var result = new List<SaveGameData.SavedRoomScan>();
        int total = 0;
        foreach (var pair in _roomScans)
        {
            string id = dungeon.GetRoomId(pair.Key);
            if (id == null) throw new InvalidOperationException("A scanned room is missing its dungeon ID.");
            var data = pair.Value;
            var moving = new HashSet<int>();
            foreach (var attachment in data.EnemyAttachments) moving.Add(attachment.PointIndex);
            var points = new List<LidarScanCodec.Point>();
            for (int i = 0; i < data.Positions.Count; i++)
            {
                // Enemy dots require mesh/animation attachments, not static world positions.
                if (moving.Contains(i)) continue;
                points.Add(new LidarScanCodec.Point
                {
                    position = pair.Key.transform.InverseTransformPoint(data.Positions[i]),
                    normal = pair.Key.transform.InverseTransformDirection(data.Normals[i]),
                    size = data.PointSizes[i]
                });
            }
            total += points.Count;
            if (total > LidarScanCodec.MaxPoints) throw new InvalidOperationException("LiDAR save exceeds the total point limit.");
            if (points.Count > 0)
                result.Add(new SaveGameData.SavedRoomScan { roomId = id, points = LidarScanCodec.Encode(points) });
        }
        result.Sort((a, b) => string.CompareOrdinal(a.roomId, b.roomId));
        return result;
    }

    void EnsurePersistenceReady()
    {
        if (_vfxPrefab == null || _vfxContainer == null || _playerMover == null || resolution <= 0)
            throw new InvalidOperationException("The LiDAR scanner is not ready for saving or loading.");
    }

    Dictionary<RoomNode, List<LidarScanCodec.Point>> DecodeRoomScans(
        List<SaveGameData.SavedRoomScan> scans, DungeonGenerator dungeon)
    {
        EnsurePersistenceReady();
        if (scans == null || scans.Count > dungeon.Rooms.Count) throw new FormatException("Invalid saved LiDAR rooms.");
        var decoded = new Dictionary<RoomNode, List<LidarScanCodec.Point>>();
        int total = 0;
        foreach (var scan in scans)
        {
            if (scan == null || scan.roomId == null || !dungeon.Rooms.TryGetValue(scan.roomId, out var room)
                || decoded.ContainsKey(room)) throw new FormatException("Saved LiDAR room is missing or duplicated.");
            var points = LidarScanCodec.Decode(scan.points, (int)Math.Min((long)resolution * resolution, LidarScanCodec.MaxPoints));
            total += points.Count;
            if (total > LidarScanCodec.MaxPoints) throw new FormatException("Too many saved LiDAR points.");
            decoded.Add(room, points);
        }
        return decoded;
    }

    public void ValidateRoomScans(List<SaveGameData.SavedRoomScan> scans, DungeonGenerator dungeon)
        => DecodeRoomScans(scans, dungeon);

    public void RestoreRoomScans(List<SaveGameData.SavedRoomScan> scans, DungeonGenerator dungeon)
    {
        // Decode everything before destroying the current textures or visual effects.
        var decoded = DecodeRoomScans(scans, dungeon);
        foreach (var old in _roomScans.Values)
        {
            if (old.VFX != null) { old.VFX.gameObject.SetActive(false); Destroy(old.VFX.gameObject); }
            if (old.PositionsTexture != null) Destroy(old.PositionsTexture);
            if (old.NormalsTexture != null) Destroy(old.NormalsTexture);
            if (old.SizesTexture != null) Destroy(old.SizesTexture);
        }
        _roomScans.Clear();
        foreach (var pair in decoded)
        {
            var data = GetOrCreateRoomScan(pair.Key);
            foreach (var point in pair.Value)
            {
                Vector3 position = pair.Key.transform.TransformPoint(point.position);
                data.Positions.Add(position);
                data.Normals.Add(pair.Key.transform.TransformDirection(point.normal).normalized);
                data.PointSizes.Add(point.size);
                Vector3Int cell = GetCell(position);
                if (!data.SpatialCells.TryGetValue(cell, out var positions))
                {
                    positions = new List<Vector3>(4);
                    data.SpatialCells.Add(cell, positions);
                }
                positions.Add(position);
            }
            ApplyRoomTextures(pair.Key);
        }
        _activeRoom = _playerMover.CurrentRoom;
        ResetFocus();
        UpdateRoomVFXVisibility();
        Debug.Log("[Save] Restored LiDAR scenery scans for " + decoded.Count + " rooms.", this);
    }
}
