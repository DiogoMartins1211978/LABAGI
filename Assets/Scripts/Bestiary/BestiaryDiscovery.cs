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

    const string SaveKey = "Bestiary.DiscoveredEnemies.v1";
    readonly HashSet<string> discoveredIds = new HashSet<string>(StringComparer.Ordinal);
    CombatController subscribedController;
    bool started;

    [Serializable]
    class SaveData
    {
        public List<string> ids = new List<string>();
    }

    public IReadOnlyList<BestiaryEntry> Entries => entries;
    public event Action<BestiaryEntry> EntryDiscovered;
    public event Action Changed;

    void Awake() => Load();

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
        Save();
        Debug.Log("[Bestiary] Discovered: " + (string.IsNullOrEmpty(entry.displayName) ? entry.enemyId : entry.displayName), this);
        EntryDiscovered?.Invoke(entry);
        Changed?.Invoke();
        return true;
    }

    void Load()
    {
        if (!PlayerPrefs.HasKey(SaveKey)) return;
        try
        {
            var data = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(SaveKey));
            if (data == null || data.ids == null) return;
            foreach (string id in data.ids)
                if (!string.IsNullOrWhiteSpace(id)) discoveredIds.Add(id);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Could not load bestiary discoveries: " + exception.Message, this);
        }
    }

    void Save()
    {
        var ids = new List<string>(discoveredIds);
        ids.Sort(StringComparer.Ordinal);
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(new SaveData { ids = ids }));
        PlayerPrefs.Save();
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
        PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();
        Changed?.Invoke();
        Debug.Log("[Bestiary] Discovery progress reset.", this);
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
