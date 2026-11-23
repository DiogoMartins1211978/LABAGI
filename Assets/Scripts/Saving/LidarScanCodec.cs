using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

// Compact room-local geometry and animated enemy surface attachments.
public static class LidarScanCodec
{
    public const int MaxPoints = 1000000;
    const uint Magic = 0x3144494c; // LID1
    const uint AttachmentMagic = 0x3244494c; // LID2; decoder also accepts scenery-only LID1.
    public struct Point
    {
        public Vector3 position;
        public Vector3 normal;
        public float size;
        public bool enemyPoint;
        public int triangleIndex;
        public Vector3 barycentric;
    }

    public static string Encode(List<Point> points)
    {
        if (points == null || points.Count > MaxPoints) throw new FormatException("Too many LiDAR points.");
        using (var output = new MemoryStream())
        {
            using (var compressed = new GZipStream(output, System.IO.Compression.CompressionLevel.Fastest, true))
            using (var writer = new BinaryWriter(compressed))
            {
                writer.Write(AttachmentMagic);
                writer.Write(points.Count);
                foreach (var point in points)
                {
                    Validate(point);
                    writer.Write(point.position.x); writer.Write(point.position.y); writer.Write(point.position.z);
                    writer.Write(point.normal.x); writer.Write(point.normal.y); writer.Write(point.normal.z);
                    writer.Write(point.size);
                    writer.Write(point.enemyPoint);
                    if (point.enemyPoint)
                    {
                        writer.Write(point.triangleIndex);
                        writer.Write(point.barycentric.x); writer.Write(point.barycentric.y); writer.Write(point.barycentric.z);
                    }
                }
            }
            return Convert.ToBase64String(output.ToArray());
        }
    }

    public static List<Point> Decode(string encoded, int capacity)
    {
        if (encoded == null || encoded.Length > 40 * 1024 * 1024)
            throw new FormatException("Invalid LiDAR scan payload.");
        using (var input = new MemoryStream(Convert.FromBase64String(encoded)))
        using (var compressed = new GZipStream(input, CompressionMode.Decompress))
        using (var reader = new BinaryReader(compressed))
        {
            uint magic = reader.ReadUInt32();
            if (magic != Magic && magic != AttachmentMagic) throw new FormatException("Unsupported LiDAR scan encoding.");
            int count = reader.ReadInt32();
            if (count < 0 || count > MaxPoints || count > capacity)
                throw new FormatException("Saved LiDAR scan exceeds the scanner capacity.");
            var points = new List<Point>(count);
            for (int i = 0; i < count; i++)
            {
                var point = new Point
                {
                    position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    normal = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    size = reader.ReadSingle()
                };
                if (magic == AttachmentMagic)
                {
                    byte kind = reader.ReadByte();
                    if (kind > 1) throw new FormatException("Invalid LiDAR attachment type.");
                    point.enemyPoint = kind == 1;
                    if (point.enemyPoint)
                    {
                        point.triangleIndex = reader.ReadInt32();
                        point.barycentric = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    }
                }
                Validate(point);
                points.Add(point);
            }
            if (compressed.ReadByte() != -1) throw new FormatException("Unexpected data after LiDAR points.");
            return points;
        }
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static void Validate(Point point)
    {
        if (!Finite(point.position.x) || !Finite(point.position.y) || !Finite(point.position.z)
            || !Finite(point.normal.x) || !Finite(point.normal.y) || !Finite(point.normal.z)
            || !Finite(point.size) || point.size <= 0f)
            throw new FormatException("Invalid LiDAR point values.");
        if (point.enemyPoint)
        {
            var bary = point.barycentric;
            if (point.triangleIndex < 0 || point.triangleIndex > (int.MaxValue - 2) / 3
                || !Finite(bary.x) || !Finite(bary.y) || !Finite(bary.z)
                || bary.x < -0.0001f || bary.y < -0.0001f || bary.z < -0.0001f
                || Math.Abs(bary.x + bary.y + bary.z - 1f) > 0.001f)
                throw new FormatException("Invalid enemy surface attachment.");
        }
    }
}
