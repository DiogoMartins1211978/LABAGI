// Standalone .NET checks for the real BestiaryDiscovery snapshot methods.
// Minimal Unity shims keep these checks independent of an open Editor.
using System;
using System.Collections.Generic;

static class BestiarySlotChecks
{
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    public static void Main()
    {
        var discovery = new BestiaryDiscovery();
        var snake = new BestiaryEntry { enemyId = "enemy.snake", displayName = "Snake" };
        var wolf = new BestiaryEntry { enemyId = "enemy.wolf", displayName = "Wolf" };
        Check(discovery.CaptureDiscoveries().Count == 0, "A new run must be empty.");
        Check(discovery.Discover(snake), "A new discovery must register.");
        Check(!discovery.Discover(snake), "Discoveries must not duplicate.");
        var slot1 = discovery.CaptureDiscoveries();
        discovery.RestoreDiscoveries(Array.Empty<string>());
        Check(!discovery.IsDiscovered(snake), "A new slot must not inherit another slot's discoveries.");
        discovery.Discover(wolf);
        var slot2 = discovery.CaptureDiscoveries();
        discovery.RestoreDiscoveries(slot1);
        Check(discovery.IsDiscovered(snake) && !discovery.IsDiscovered(wolf), "Continue must restore only slot one's discoveries.");
        discovery.RestoreDiscoveries(slot2);
        Check(discovery.IsDiscovered(wolf) && !discovery.IsDiscovered(snake), "Switching back to slot two must not merge slot one.");
        discovery.Discover(snake);
        Check(slot2.Count == 1, "Unsaved discoveries must not mutate the saved snapshot.");
        var snapshot = discovery.CaptureDiscoveries();
        snapshot.Clear();
        Check(discovery.CaptureDiscoveries().Count == 2, "Changing an exported snapshot must not change live discoveries.");
        bool rejected = false;
        try { discovery.RestoreDiscoveries(new[] { "enemy.valid", " " }); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected && discovery.IsDiscovered(snake) && discovery.IsDiscovered(wolf), "Invalid restoration must leave live discoveries unchanged.");
        int refreshes = 0, announcements = 0;
        discovery.Changed += () => refreshes++;
        discovery.EntryDiscovered += entry => announcements++;
        discovery.RestoreDiscoveries(new[] { "enemy.z", "enemy.a", "enemy.a" });
        Check(refreshes == 1 && announcements == 0, "Loading must refresh once without replaying discovery announcements.");
        var ids = discovery.CaptureDiscoveries();
        Check(ids.Count == 2 && ids[0] == "enemy.a" && ids[1] == "enemy.z", "Snapshots must be sorted and deduplicated, preserving unknown IDs.");
        discovery.RestoreDiscoveries(slot1);
        Check(discovery.IsDiscovered(snake) && !discovery.IsDiscovered(wolf), "Restoring the saved state must discard unsaved changes.");
        Console.WriteLine("PASS: " + checks + " bestiary snapshot checks.");
    }
}

// Test-only stand-ins; this file is outside Assets and never enters Unity builds.
namespace UnityEngine
{
    public class MonoBehaviour
    {
        protected static T FindAnyObjectByType<T>() where T : class => null;
    }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class Tooltip : Attribute { public Tooltip(string text) { } }
    [AttributeUsage(AttributeTargets.Method)] public sealed class ContextMenu : Attribute { public ContextMenu(string text) { } }
    public static class Application { public static bool isPlaying = true; }
    public static class Debug
    {
        public static void Log(object message, object context = null) { }
        public static void LogWarning(object message, object context = null) { }
    }
}
public sealed class BestiaryEntry { public string enemyId, displayName, name; }
public sealed class BestiaryIdentity { public BestiaryEntry entry; }
public sealed class Enemy { public T GetComponent<T>() where T : class => null; }
public sealed class CombatController
{
    public event Action<Enemy, bool> CombatEnded { add { } remove { } }
}
