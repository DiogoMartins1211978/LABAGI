using System;
using System.Collections.Generic;
using Tools.Content;
using UnityEngine;
using UnityEngine.SceneManagement;

// Separate always-active root in the gameplay scene. Runs after gameplay Start.
[DefaultExecutionOrder(2000)]
public sealed class SaveGameSession : MonoBehaviour
{
    [SerializeField] DungeonGenerator dungeon;
    [SerializeField] PlayerNodeMover player;
    [SerializeField] CardSystem cards;
    [SerializeField] CameraController look;
    [SerializeField] CombatController combat;
    [SerializeField] ContentCatalog cardCatalog;
    [SerializeField] BestiaryDiscovery bestiary;
    [SerializeField] LiDARScanner lidar;
    [SerializeField, Range(1, 3)] int debugSlot = 1;

    public string LastError { get; private set; }
    public event Action Saved;
    public event Action<string> Failed;
    bool ready;
    readonly Dictionary<string, CombatManager> cardsById = new Dictionary<string, CombatManager>(StringComparer.Ordinal);
    readonly Dictionary<CombatManager, string> idsByCard = new Dictionary<CombatManager, string>();

    void Start()
    {
        try
        {
            ResolveReferences();
            if (!SaveGames.LaunchPending) SaveGames.ActiveSlot = 0;
            if (SaveGames.LaunchPending)
            {
                // Clear the request even when restoration fails: never silently
                // reinterpret a failed Continue as a new game in that slot.
                SaveGames.LaunchPending = false;
                var loaded = SaveGames.PendingLoad;
                SaveGames.PendingLoad = null;
                if (loaded != null) Restore(loaded);
                else
                {
                    bestiary.RestoreDiscoveries(Array.Empty<string>());
                    var initial = Capture(SaveGames.PendingSlot);
                    SaveGames.Files.Write(initial.slot, JsonUtility.ToJson(initial, true), SaveGames.OverwritePending);
                }
                SaveGames.ActiveSlot = SaveGames.PendingSlot;
                Debug.Log("[Save] Ready in slot " + SaveGames.ActiveSlot, this);
            }
            ready = true;
        }
        catch (Exception e)
        {
            SaveGames.ActiveSlot = 0;
            Fail(e);
            if (player != null) player.SetInputLocked(true);
        }
    }

    void ResolveReferences()
    {
        if (dungeon == null) dungeon = FindAnyObjectByType<DungeonGenerator>();
        if (player == null) player = FindAnyObjectByType<PlayerNodeMover>();
        if (cards == null) cards = FindAnyObjectByType<CardSystem>();
        if (look == null) look = FindAnyObjectByType<CameraController>();
        if (combat == null) combat = FindAnyObjectByType<CombatController>();
        if (cardCatalog == null) cardCatalog = Resources.Load<ContentCatalog>(ContentCatalog.ResourcePath);
        if (bestiary == null) bestiary = FindAnyObjectByType<BestiaryDiscovery>();
        if (lidar == null) lidar = FindAnyObjectByType<LiDARScanner>();
        if (dungeon == null || player == null || cards == null || look == null || combat == null || cardCatalog == null || bestiary == null || lidar == null)
            throw new InvalidOperationException("Assign Dungeon, Player, Cards, Look, Combat, Card Catalog, Bestiary and LiDAR on SaveGameSession.");
        foreach (var entry in cardCatalog.entries)
        {
            if (!(entry.asset is CombatManager card)) continue;
            if (string.IsNullOrWhiteSpace(entry.id) || cardsById.ContainsKey(entry.id) || idsByCard.ContainsKey(card))
                throw new InvalidOperationException("Cards need unique permanent IDs in the content catalogue.");
            cardsById.Add(entry.id, card);
            idsByCard.Add(card, entry.id);
        }
    }

    SaveGameData Capture(int slot)
    {
        if (player.CurrentRoom == null || player.IsMoving || player.InputLocked || combat.InCombat
            || cards.CurrentMode != CardSystem.Mode.Exploration || cards.IsBookDeckOpen)
            throw new InvalidOperationException("Save while exploring, after movement or combat has finished. Close the deck editor first.");
        var data = new SaveGameData
        {
            slot = slot,
            savedUtc = DateTime.UtcNow.ToString("O"),
            scene = SceneManager.GetActiveScene().name,
            layoutHash = dungeon.LayoutHash,
            roomId = dungeon.GetRoomId(player.CurrentRoom),
            cameraLocalRotation = look.transform.localRotation,
            collection = Encode(cards.collection),
            deck = Encode(cards.deck),
            bestiaryIds = bestiary.CaptureDiscoveries(),
            lidarRooms = lidar.CaptureRoomScans(dungeon)
        };
        foreach (var pair in dungeon.Rooms)
        {
            var enemy = pair.Value.enemy;
            if (enemy == null) continue;
            data.enemies.Add(new SaveGameData.SavedEnemy
            {
                roomId = pair.Key,
                localPosition = enemy.transform.localPosition,
                localRotation = enemy.transform.localRotation,
                defeated = enemy.Resolved
            });
        }
        Validate(data);
        return data;
    }

    List<string> Encode(List<CombatManager> source)
    {
        var result = new List<string>();
        foreach (var card in source)
        {
            if (card == null || !idsByCard.TryGetValue(card, out string id))
                throw new InvalidOperationException("A card is missing its content catalogue ID.");
            result.Add(id);
        }
        return result;
    }

    List<CombatManager> Decode(List<string> source)
    {
        var result = new List<CombatManager>();
        if (source.Count > 4096) throw new FormatException("Too many cards in save.");
        foreach (string id in source)
        {
            if (id == null || !cardsById.TryGetValue(id, out var card))
                throw new FormatException("Saved card is missing from the catalogue: " + id);
            result.Add(card);
        }
        return result;
    }

    void Validate(SaveGameData data)
    {
        lidar.ValidateRoomScans(data.lidarRooms, dungeon);
        if (data.layoutHash != dungeon.LayoutHash)
            throw new InvalidOperationException("The dungeon layout changed since this save was created.");
        if (data.scene != SceneManager.GetActiveScene().name || data.roomId == null || !dungeon.Rooms.ContainsKey(data.roomId))
            throw new FormatException("Saved room or scene is unavailable.");
        CheckRotation(data.cameraLocalRotation);
        var collection = Decode(data.collection);
        var deck = Decode(data.deck);
        if (deck.Count > cards.deckSize) throw new FormatException("Saved deck exceeds the current deck size.");
        foreach (var card in deck)
            if (!collection.Contains(card)) throw new FormatException("Saved deck contains an unowned card.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var saved in data.enemies)
        {
            if (saved == null || saved.roomId == null || !seen.Add(saved.roomId)
                || !dungeon.Rooms.TryGetValue(saved.roomId, out var room) || room.enemy == null)
                throw new FormatException("Saved enemy room is missing or duplicated.");
            if (!Finite(saved.localPosition.x) || !Finite(saved.localPosition.y) || !Finite(saved.localPosition.z))
                throw new FormatException("Invalid saved enemy position.");
            CheckRotation(saved.localRotation);
        }
        foreach (var pair in dungeon.Rooms)
            if (pair.Value.enemy != null && !seen.Contains(pair.Key))
                throw new FormatException("Save is missing an enemy state.");
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static void CheckRotation(Quaternion value)
    {
        if (!Finite(value.x) || !Finite(value.y) || !Finite(value.z) || !Finite(value.w)
            || Mathf.Abs(Quaternion.Dot(value, value) - 1f) > 0.01f)
            throw new FormatException("Invalid saved camera or enemy rotation.");
    }

    void Restore(SaveGameData data)
    {
        // Resolve and validate everything before changing live gameplay.
        Validate(data);
        var discoveries = new List<string>(data.bestiaryIds);
        if (data.version == 1)
        {
            foreach (var saved in data.enemies)
            {
                if (!saved.defeated) continue;
                var identity = dungeon.Rooms[saved.roomId].enemy.GetComponent<BestiaryIdentity>();
                if (identity != null && identity.entry != null && !string.IsNullOrWhiteSpace(identity.entry.enemyId))
                    discoveries.Add(identity.entry.enemyId);
            }
        }
        bestiary.RestoreDiscoveries(discoveries);
        cards.collection = Decode(data.collection);
        cards.deck = Decode(data.deck);
        foreach (var saved in data.enemies)
        {
            var enemy = dungeon.Rooms[saved.roomId].enemy;
            enemy.transform.localPosition = saved.localPosition;
            enemy.transform.localRotation = saved.localRotation;
            if (saved.defeated) enemy.Resolve();
        }
        cards.EnterExploration();
        player.InitializePlayer(dungeon.Rooms[data.roomId]);
        look.RestoreSavedRotation(data.cameraLocalRotation);
        lidar.RestoreRoomScans(data.lidarRooms, dungeon);
    }

    public bool TrySave(out string error)
    {
        try
        {
            if (!ready || SaveGames.ActiveSlot == 0)
                throw new InvalidOperationException("Start or continue a save slot first.");
            var data = Capture(SaveGames.ActiveSlot);
            SaveGames.Files.Write(data.slot, JsonUtility.ToJson(data, true), true);
            LastError = error = null;
            Debug.Log("[Save] Saved slot " + data.slot, this);
            Saved?.Invoke();
            return true;
        }
        catch (Exception e) { error = e.Message; Fail(e); return false; }
    }

    public bool CanSave(out string reason)
    {
        reason = null;
        if (!ready || SaveGames.ActiveSlot == 0)
            reason = "No active save slot. Start or continue a game from Single Player first.";
        else if (combat == null || player == null || cards == null)
            reason = "The save system is not ready.";
        else if (combat.InCombat || cards.CurrentMode == CardSystem.Mode.Combat)
            reason = "Cannot save during combat. Finish the battle first.";
        else if (player.IsMoving)
            reason = "Cannot save during movement. Resume and finish moving first.";
        else if (player.InputLocked || player.CurrentRoom == null)
            reason = "Cannot save during this transition. Resume and try again once you are exploring.";
        else if (cards.CurrentMode != CardSystem.Mode.Exploration || cards.IsBookDeckOpen)
            reason = "Close the deck editor before saving.";
        return reason == null;
    }

    void Fail(Exception e)
    {
        LastError = e.Message;
        Debug.LogError("[Save] " + LastError, this);
        Failed?.Invoke(LastError);
    }

    [ContextMenu("Save Testing/Start New Game in Debug Slot (empty only)")]
    void DebugNew()
    {
        if (!Application.isPlaying) return;
        if (!SaveGames.StartNew(debugSlot, false, out string error)) Debug.LogError("[Save] " + error, this);
    }

    [ContextMenu("Save Testing/Continue Debug Slot")]
    void DebugContinue()
    {
        if (!Application.isPlaying) return;
        if (!SaveGames.Continue(debugSlot, out string error)) Debug.LogError("[Save] " + error, this);
    }

    [ContextMenu("Save Testing/Save Active Slot")]
    public void SaveActiveSlot() { if (Application.isPlaying) TrySave(out _); }

    [ContextMenu("Save Testing/Log Save Folder")]
    void LogFolder() => Debug.Log(SaveGames.DirectoryPath, this);
}
