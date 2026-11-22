using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class SaveGameData
{
    public const int CurrentVersion = 1;
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

    [Serializable]
    public sealed class SavedEnemy
    {
        public string roomId;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public bool defeated;
    }

    public static SaveGameData Parse(string json)
    {
        // Missing fields must not silently become a valid version-one save.
        if (string.IsNullOrWhiteSpace(json) || !json.Contains("\"version\""))
            throw new FormatException("Missing save format version.");
        var data = JsonUtility.FromJson<SaveGameData>(json);
        if (data == null) throw new FormatException("Invalid save file.");
        if (data.version != CurrentVersion)
            throw new NotSupportedException("Unsupported save format version. This slot was left unchanged.");
        if (data.slot < 1 || data.slot > 3 || string.IsNullOrWhiteSpace(data.scene)
            || string.IsNullOrWhiteSpace(data.layoutHash) || string.IsNullOrWhiteSpace(data.roomId)
            || !DateTime.TryParse(data.savedUtc, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out _)
            || data.collection == null || data.deck == null || data.enemies == null)
            throw new FormatException("Incomplete save file.");
        return data;
    }
}
