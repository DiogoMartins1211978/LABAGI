// Standalone metadata checks. System.Text.Json stands in for Unity's serializer;
// native Unity serialization and chest visuals still require an Editor round trip.
using System;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;

static class RewardSaveDataChecks
{
    static int checks;
    static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };
    static string Json(SaveGameData data) => JsonSerializer.Serialize(data, Options);
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    static void Reject(Action action, string message)
    {
        bool failed = false;
        try { action(); } catch (FormatException) { failed = true; }
        Check(failed, message);
    }
    public static void Main()
    {
        var data = new SaveGameData
        {
            slot = 1, savedUtc = DateTime.UtcNow.ToString("O"), scene = "Test Scene", layoutHash = "test", roomId = "R01",
            cameraLocalRotation = new Quaternion(0, 0, 0, 1),
            collection = new List<string> { "card.attack", "card.attack" }
        };
        var chest = new SaveGameData.SavedTreasure
        {
            roomId = "R05", prefabKey = "Treasure_01", cardId = "card.attack", claimed = true,
            localPosition = new Vector3(2, 0, -3), localRotation = new Quaternion(0, 0, 0, 1)
        };
        data.treasures.Add(chest);
        data.treasures.Add(new SaveGameData.SavedTreasure
        {
            roomId = "R07", prefabKey = "Treasure_02", claimed = false,
            localRotation = new Quaternion(0, 0, 0, 1)
        });
        var restored = SaveGameData.Parse(Json(data));
        Check(restored.treasures.Count == 2 && restored.treasures[0].claimed && !restored.treasures[1].claimed,
            "Claimed and unclaimed chests must remain independent.");
        Check(restored.treasures[0].prefabKey == "Treasure_01" && restored.treasures[0].cardId == "card.attack",
            "Treasure variant and reward ID must survive serialization.");
        Check(restored.treasures[0].localPosition.x == 2 && restored.treasures[0].localPosition.z == -3,
            "Chest placement must survive serialization.");
        Check(restored.collection.Count == 2 && restored.collection[0] == restored.collection[1],
            "Earned duplicate card copies must be preserved.");
        data.version = 4;
        Check(SaveGameData.Parse(Json(data)).treasures.Count == 0, "Older saves must not invent a chest history.");
        data.version = SaveGameData.CurrentVersion;
        data.treasures[1].roomId = "R05";
        Reject(() => SaveGameData.Parse(Json(data)), "Duplicate treasure room IDs must be rejected.");
        data.treasures[1].roomId = "R07";
        chest.prefabKey = "";
        Reject(() => SaveGameData.Parse(Json(data)), "Missing treasure prefab keys must be rejected.");
        chest.prefabKey = "Treasure_01";
        chest.localRotation = new Quaternion(0, 0, 0, 0);
        Reject(() => SaveGameData.Parse(Json(data)), "Invalid treasure rotations must be rejected.");
        chest.localRotation = new Quaternion(0, 0, 0, 1);
        string missing = Json(data).Replace("\"treasures\":", "\"missingTreasures\":");
        Reject(() => SaveGameData.Parse(missing), "Current-format saves must include treasure state.");
        Console.WriteLine("PASS: " + checks + " reward save metadata checks (standalone serializer).");
    }
}
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    }
    public static class JsonUtility
    {
        public static T FromJson<T>(string json)
            => JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { IncludeFields = true });
    }
}
