using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

// Menu-facing entry points. Slots are always numbered 1, 2, 3.
public static class SaveGames
{
    public static int ActiveSlot { get; internal set; }
    internal static int PendingSlot;
    internal static SaveGameData PendingLoad;
    internal static bool OverwritePending;
    internal static bool LaunchPending;

    public static string DirectoryPath => Path.Combine(Application.persistentDataPath, "Saves");
    internal static SaveSlotFiles Files => new SaveSlotFiles(DirectoryPath, (json, slot) =>
    {
        if (SaveGameData.Parse(json).slot != slot) throw new FormatException("Save belongs to another slot.");
    });

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        ActiveSlot = PendingSlot = 0;
        PendingLoad = null;
        OverwritePending = LaunchPending = false;
    }

    public static bool TryReadSlot(int slot, out SaveGameData data, out bool recovered, out string error)
    {
        data = null;
        recovered = false;
        try
        {
            data = SaveGameData.Parse(Files.Read(slot, out recovered));
            error = null;
            return true;
        }
        catch (Exception e) { error = e.Message; return false; }
    }

    public static bool StartNew(int slot, bool overwriteConfirmed, out string error, string scene = "Test Scene")
    {
        try
        {
            if (Files.Exists(slot) && !overwriteConfirmed)
                throw new InvalidOperationException("This slot is occupied. Confirm overwriting it first.");
            ValidateScene(scene);
            PendingSlot = slot;
            PendingLoad = null;
            OverwritePending = overwriteConfirmed;
            Launch(scene);
            error = null;
            return true;
        }
        catch (Exception e) { error = e.Message; return false; }
    }

    public static bool Continue(int slot, out string error)
    {
        if (!TryReadSlot(slot, out var data, out bool recovered, out error)) return false;
        try
        {
            ValidateScene(data.scene);
            PendingSlot = slot;
            PendingLoad = data;
            OverwritePending = false;
            if (recovered) Debug.LogWarning("[Save] Slot " + slot + " recovered from its backup.");
            Launch(data.scene);
            return true;
        }
        catch (Exception e) { error = e.Message; return false; }
    }

    static void ValidateScene(string scene)
    {
        if (!Application.CanStreamedLevelBeLoaded(scene))
            throw new InvalidOperationException("Saved gameplay scene is not in the build: " + scene);
    }

    static void Launch(string scene)
    {
        ActiveSlot = 0;
        LaunchPending = true;
        Time.timeScale = 1f;
        SceneManager.LoadScene(scene);
    }
}
