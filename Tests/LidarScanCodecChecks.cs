// Standalone checks against the production codec; Unity vector fields are shimmed below.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

static class LidarScanCodecChecks
{
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); }
        catch (FormatException) { rejected = true; }
        catch (IOException) { rejected = true; }
        Check(rejected, message);
    }
    static string Pack(Action<BinaryWriter> write)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, true))
        using (var writer = new BinaryWriter(gzip)) write(writer);
        return Convert.ToBase64String(output.ToArray());
    }
    public static void Main()
    {
        var points = new List<LidarScanCodec.Point>();
        var random = new Random(42);
        for (int i = 0; i < 1500; i++) points.Add(new LidarScanCodec.Point
        {
            position = new Vector3((float)random.NextDouble() * 500f, -2.25f, i * 0.04f),
            normal = new Vector3(0, 1, 0), size = i % 2 == 0 ? 0.35f : 1f
        });
        string saved = LidarScanCodec.Encode(points);
        var restored = LidarScanCodec.Decode(saved, 65536);
        Check(restored.Count == points.Count, "Every point must survive a round trip.");
        bool same = true;
        for (int i = 0; i < points.Count; i++)
        {
            var a = points[i]; var b = restored[i];
            same &= a.position.x == b.position.x && a.position.y == b.position.y && a.position.z == b.position.z
                && a.normal.x == b.normal.x && a.normal.y == b.normal.y && a.normal.z == b.normal.z && a.size == b.size;
        }
        Check(same, "Point positions, normals, sizes and order must remain exact.");
        Check(LidarScanCodec.Decode(LidarScanCodec.Encode(new List<LidarScanCodec.Point>()), 0).Count == 0,
            "Empty scans must round-trip.");
        points.Clear();
        Check(LidarScanCodec.Decode(saved, 65536).Count == 1500, "Snapshots must be independent of live point lists.");
        Reject(() => LidarScanCodec.Decode(saved, 100), "A smaller scanner must reject data instead of truncating it.");
        Reject(() => LidarScanCodec.Decode(null, 100), "Null payloads must be rejected.");
        Reject(() => LidarScanCodec.Decode("not base64", 100), "Invalid base64 must be rejected.");
        Reject(() => LidarScanCodec.Decode(Pack(w => { w.Write(123); w.Write(0); }), 100), "Wrong payload versions must be rejected.");
        Reject(() => LidarScanCodec.Decode(Pack(w => { w.Write(0x3144494c); w.Write(-1); }), 100), "Negative counts must be rejected.");
        Reject(() => LidarScanCodec.Decode(Pack(w => { w.Write(0x3144494c); w.Write(int.MaxValue); }), int.MaxValue),
            "Oversized counts must be rejected before allocation.");
        Reject(() => LidarScanCodec.Decode(Pack(w => { w.Write(0x3144494c); w.Write(1); w.Write(1f); }), 100),
            "Incomplete point records must be rejected.");
        Reject(() => LidarScanCodec.Decode(Pack(w => { w.Write(0x3144494c); w.Write(0); w.Write((byte)1); }), 100),
            "Trailing decompressed data must be rejected.");
        Reject(() => LidarScanCodec.Encode(new List<LidarScanCodec.Point>
            { new LidarScanCodec.Point { position = new Vector3(float.NaN, 0, 0), size = 1f } }), "NaN must be rejected.");
        Reject(() => LidarScanCodec.Encode(new List<LidarScanCodec.Point>
            { new LidarScanCodec.Point { size = -1f } }), "Invalid point sizes must be rejected.");
        Console.WriteLine("PASS: " + checks + " LiDAR codec checks.");
    }
}
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
}
