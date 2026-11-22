using System;
using System.Collections.Generic;
using UnityEngine;

// Keep this on the always-active BestiaryManager, so victories are recorded
// while the book is closed. UI can query Entries and IsDiscovered later.
[DisallowMultipleComponent]
public class BestiaryDiscovery : MonoBehaviour
{
    [SerializeField] BestiaryEntry[] entries = new BestiaryEntry[0];
    [Tooltip("Optional; the scene's combat controller is found automatically.")]
    [SerializeField] CombatController combatController;

    readonly HashSet<string> discoveredIds = new HashSet<string>(StringComparer.Ordinal);
    CombatController subscribedController;
    bool started;

    public IReadOnlyList<BestiaryEntry> Entries => entries;
    public event Action<BestiaryEntry> EntryDiscovered;
    public event Action Changed;

    void Start()
    {
        started = true;
        Connect();
    }

    void OnEnable()
    {
        if (started) Connect();
    }

    void Connect()
    {
        if (combatController == null) combatController = FindAnyObjectByType<CombatController>();
        if (combatController == null)
        {
            Debug.LogWarning("Bestiary Discovery: assign a Combat Controller to record encounters.", this);
            return;
        }
        if (subscribedController != null) subscribedController.CombatEnded -= OnCombatEnded;
        subscribedController = combatController;
        subscribedController.CombatEnded += OnCombatEnded;
    }

    void OnCombatEnded(Enemy enemy, bool defeated)
    {
        if (defeated) RegisterDefeat(enemy);
    }

    public bool RegisterDefeat(Enemy enemy)
    {
        if (enemy == null) return false;
        var identity = enemy.GetComponent<BestiaryIdentity>();
        return identity != null && Discover(identity.entry);
    }

    public bool IsDiscovered(BestiaryEntry entry) => entry != null
        && !string.IsNullOrWhiteSpace(entry.enemyId) && discoveredIds.Contains(entry.enemyId);

    public bool Discover(BestiaryEntry entry)
    {
        if (entry == null) return false;
        if (string.IsNullOrWhiteSpace(entry.enemyId))
        {
            Debug.LogWarning("Bestiary entry '" + entry.name + "' needs a permanent Enemy Id.", entry);
            return false;
        }
        if (!discoveredIds.Add(entry.enemyId)) return false;
        Debug.Log("[Bestiary] Discovered: " + (string.IsNullOrEmpty(entry.displayName) ? entry.enemyId : entry.displayName), this);
        EntryDiscovered?.Invoke(entry);
        Changed?.Invoke();
        return true;
    }

    // Snapshots belong to SaveGameSession's active slot. Never read or overwrite
    // the legacy shared PlayerPrefs key: its discoveries cannot be attributed to a slot.
    public List<string> CaptureDiscoveries()
    {
        var ids = new List<string>(discoveredIds);
        ids.Sort(StringComparer.Ordinal);
        return ids;
    }

    public void RestoreDiscoveries(IEnumerable<string> ids)
    {
        if (ids == null) throw new ArgumentNullException(nameof(ids));
        var restored = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in ids)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Bestiary IDs cannot be blank.", nameof(ids));
            restored.Add(id);
        }
        discoveredIds.Clear();
        discoveredIds.UnionWith(restored);
        // Refresh the book without replaying individual discovery notifications.
        Changed?.Invoke();
    }

    [ContextMenu("Debug/Discover First Enemy (Play Mode)")]
    void DiscoverFirstEnemy()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("Enter Play Mode before testing bestiary discoveries.", this);
            return;
        }
        foreach (var entry in entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.enemyId)) continue;
            if (Discover(entry))
                Debug.Log("[Bestiary] Test discovery added to the current run. Use Save Game to keep it.", this);
            else
                Debug.Log("[Bestiary] The first configured enemy is already discovered in this run.", this);
            return;
        }
        Debug.LogWarning("Bestiary Discovery: configure an entry with an Enemy Id before testing.", this);
    }

    [ContextMenu("Debug/Reset Bestiary Discoveries (Play Mode)")]
    void ResetDiscoveries()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("Enter Play Mode before resetting bestiary discoveries.", this);
            return;
        }
        discoveredIds.Clear();
        Changed?.Invoke();
        Debug.Log("[Bestiary] Current run discoveries reset. Use Save Game to keep this change.", this);
    }

    void OnDisable()
    {
        if (subscribedController != null) subscribedController.CombatEnded -= OnCombatEnded;
        subscribedController = null;
    }

    void OnValidate()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
            if (entry != null && !string.IsNullOrWhiteSpace(entry.enemyId) && !ids.Add(entry.enemyId))
                Debug.LogWarning("Duplicate bestiary Enemy Id: " + entry.enemyId, this);
    }
}
