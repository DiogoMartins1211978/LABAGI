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

            if (id == null)
                throw new InvalidOperationException("A scanned room is missing its dungeon ID.");

            var data = pair.Value;

            var animatedPoints = new Dictionary<int, SurfacePointAttachment>();

            foreach (var attachment in data.SurfaceAttachments)
            {
                if (attachment.IsAnimatedEnemy)
                    animatedPoints.Add(attachment.PointIndex, attachment);
            }

            var points = new List<LidarScanCodec.Point>();

            for (int i = 0; i < data.Positions.Count; i++)
            {
                animatedPoints.TryGetValue(i, out var attachment);
                if (attachment != null && (attachment.Enemy == null || attachment.Enemy.Resolved)) continue;
                if (attachment != null && attachment.Enemy != pair.Key.enemy)
                    throw new InvalidOperationException("An enemy scan is attached to a different room's enemy.");

                points.Add(new LidarScanCodec.Point
                {
                    position = pair.Key.transform.InverseTransformPoint(data.Positions[i]),
                    normal = pair.Key.transform.InverseTransformDirection(data.Normals[i]),
                    size = data.PointSizes[i],
                    enemyPoint = attachment != null,
                    triangleIndex = attachment != null ? attachment.EnemySurface.TriangleIndex : 0,
                    barycentric = attachment != null ? attachment.EnemySurface.BarycentricCoordinate : Vector3.zero
                });
            }

            total += points.Count;

            if (total > LidarScanCodec.MaxPoints)
                throw new InvalidOperationException("LiDAR save exceeds the total point limit.");

            if (points.Count > 0)
            {
                result.Add(new SaveGameData.SavedRoomScan
                {
                    roomId = id,
                    points = LidarScanCodec.Encode(points),
                    enemySurfaceSignature = points.Exists(point => point.enemyPoint) ? pair.Key.enemy.ScanSurfaceSignature : null
                });
            }
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
            foreach (var point in points)
            {
                if (!point.enemyPoint) continue;
                var enemy = room.enemy;
                if (enemy == null || string.IsNullOrEmpty(scan.enemySurfaceSignature)
                    || scan.enemySurfaceSignature != enemy.ScanSurfaceSignature)
                    throw new FormatException("The saved enemy LiDAR mesh changed or is unavailable in room " + scan.roomId + ".");
                if (point.triangleIndex >= enemy.ScanTriangleCount)
                    throw new FormatException("Invalid saved enemy LiDAR surface in room " + scan.roomId + ".");
            }
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
                Vector3 normal = pair.Key.transform.TransformDirection(point.normal).normalized;
                if (point.enemyPoint)
                {
                    var enemy = pair.Key.enemy;
                    if (enemy.Resolved) continue;
                    var surface = new Enemy.SurfaceAttachment
                        { TriangleIndex = point.triangleIndex, BarycentricCoordinate = point.barycentric };
                    if (!enemy.GetAnimatedSurfacePoint(surface, out position, out normal))
                        throw new InvalidOperationException("Could not restore an enemy scan surface.");
                    normal.Normalize();
                    position += normal * SURFACE_OFFSET;
                    data.SurfaceAttachments.Add(new SurfacePointAttachment
                    {
                        Enemy = enemy, EnemySurface = surface, IsAnimatedEnemy = true,
                        PointIndex = data.Positions.Count
                    });
                }
                data.Positions.Add(position);
                data.Normals.Add(normal);
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
        int enemyPoints = 0;
        foreach (var data in _roomScans.Values) enemyPoints += data.SurfaceAttachments.Count;
        Debug.Log("[Save] Restored LiDAR scans for " + decoded.Count + " rooms, including " + enemyPoints + " enemy-attached points.", this);
    }
}
