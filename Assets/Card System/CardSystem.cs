using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public class CardSystem : MonoBehaviour
{
    public enum Mode { Play, DeckBuilder }

    [Header("Cards")]
    public List<CardData> collection = new List<CardData>();
    public List<CardData> deck = new List<CardData>();

    [Header("Rules")]
    public int deckSize = 8;
    public int handSize = 4;

    [Header("Turns")]
    public bool autoStartNextTurn = true;
    public float nextTurnDelay = 0.5f;

    [Header("Combat & Movement Lock")]
    [Tooltip("Lock player movement and camera when entering Play (Combat) mode")]
    public bool lockPlayerInPlayMode = true;
    [Tooltip("Tag of the player object to find for locking movement/camera")]
    public string playerTag = "Player";

    [Header("Input Action Names (Matches your Input Actions Asset)")]
    public string previousAction = "Previous";
    public string nextAction = "Next";
    public string confirmAction = "Interact";
    public string removeAction = "Remove";
    public string toggleModeAction = "ToggleDeck";

    [Header("Direct Hand Select Action Names")]
    public string slot1Action = "SelectSlot1";
    public string slot2Action = "SelectSlot2";
    public string slot3Action = "SelectSlot3";
    public string slot4Action = "SelectSlot4";

    public Mode CurrentMode { get; private set; } = Mode.Play;
    public bool IsPlayerTurn { get; private set; }
    public IReadOnlyList<CardData> Hand => hand;
    public CardData NextCard => queue.Count > 0 ? queue.Peek() : null;
    public int SelectedIndex => selected;

    public event Action TurnStarted;
    public event Action<CardData> CardPlayed;
    public event Action TurnEnded;
    public event Action StateChanged;

    readonly List<CardData> hand = new List<CardData>();
    readonly Queue<CardData> queue = new Queue<CardData>();
    int selected;
    float nextTurnTimer = -1f;

    readonly List<CardData> browsable = new List<CardData>();
    int browseIndex;

    InputAction prev, next, confirm, remove, toggle;
    InputAction slot1, slot2, slot3, slot4;

    void Awake()
    {
        prev = Find(previousAction);
        next = Find(nextAction);
        confirm = Find(confirmAction);
        remove = Find(removeAction);
        toggle = Find(toggleModeAction);

        slot1 = Find(slot1Action);
        slot2 = Find(slot2Action);
        slot3 = Find(slot3Action);
        slot4 = Find(slot4Action);
    }

    void Start()
    {
        if (collection.Count == 0)
            Debug.LogWarning("[Cards] Collection is empty. Add CardData assets to Collection.");

        if (!TryStartPlay())
            EnterBuilder();
    }

    void Update()
    {
        if (Pressed(toggle)) ToggleMode();

        if (CurrentMode == Mode.Play)
        {
            PlayInput();
            TickNextTurnTimer();
        }
        else
        {
            BuilderInput();
        }
    }

    void PlayInput()
    {
        if (Pressed(slot1)) SelectOrPlayIndex(0);
        if (Pressed(slot2)) SelectOrPlayIndex(1);
        if (Pressed(slot3)) SelectOrPlayIndex(2);
        if (Pressed(slot4)) SelectOrPlayIndex(3);

        if (Pressed(prev)) MoveSelection(-1);
        if (Pressed(next)) MoveSelection(+1);
        if (Pressed(confirm)) PlaySelected();
    }

    void BuilderInput()
    {
        if (Pressed(prev)) MoveBrowse(-1);
        if (Pressed(next)) MoveBrowse(+1);
        if (Pressed(confirm)) AddToDeck(CurrentBrowsed());
        if (Pressed(remove)) RemoveFromDeck(CurrentBrowsed());
    }

    bool TryStartPlay()
    {
        if (!DeckIsValid(out string reason))
        {
            Debug.LogWarning($"[Cards] Deck not playable: {reason}");
            return false;
        }

        CurrentMode = Mode.Play;
        hand.Clear();
        queue.Clear();
        nextTurnTimer = -1f;

        var shuffled = new List<CardData>(deck);
        Shuffle(shuffled);
        for (int i = 0; i < shuffled.Count; i++)
        {
            if (i < handSize) hand.Add(shuffled[i]);
            else queue.Enqueue(shuffled[i]);
        }

        selected = 0;
        SetPlayerMovementEnabled(false);
        StartPlayerTurn();
        return true;
    }

    public void StartPlayerTurn()
    {
        if (CurrentMode != Mode.Play || hand.Count == 0) return;

        IsPlayerTurn = true;
        LogHand("Your turn - play one card");
        TurnStarted?.Invoke();
    }

    void TickNextTurnTimer()
    {
        if (nextTurnTimer < 0f) return;

        nextTurnTimer -= Time.deltaTime;
        if (nextTurnTimer < 0f)
        {
            nextTurnTimer = -1f;
            StartPlayerTurn();
        }
    }

    void MoveSelection(int dir)
    {
        if (hand.Count == 0) return;
        selected = (selected + dir + hand.Count) % hand.Count;
        LogHand("Selected");
    }

    public void SelectOrPlayIndex(int index)
    {
        if (index < 0 || index >= hand.Count) return;

        if (selected == index)
        {
            PlaySelected();
        }
        else
        {
            selected = index;
            LogHand($"Selected slot {index + 1}");
        }
    }

    void PlaySelected()
    {
        if (!IsPlayerTurn)
        {
            Debug.Log("[Cards] It's not your turn.");
            return;
        }

        CardData card = hand[selected];
        IsPlayerTurn = false;

        card.OnPlay(gameObject);

        hand[selected] = queue.Dequeue();
        queue.Enqueue(card);

        LogHand($"Played {card.displayName}");
        CardPlayed?.Invoke(card);
        TurnEnded?.Invoke();

        if (autoStartNextTurn) nextTurnTimer = nextTurnDelay;
    }

    public void SetSelection(int index)
    {
        if (CurrentMode != Mode.Play) return;
        if (index < 0 || index >= hand.Count) return;

        if (selected != index)
        {
            selected = index;
            LogHand($"Selected slot {index + 1} (Hover)");
        }
    }

    public void PlayIndexDirectly(int index)
    {
        if (CurrentMode != Mode.Play) return;
        if (index < 0 || index >= hand.Count) return;

        selected = index;
        PlaySelected();
    }

    void ToggleMode()
    {
        if (CurrentMode == Mode.Play)
        {
            EnterBuilder();
        }
        else if (TryStartPlay())
        {
            Debug.Log("[Cards] Left deck builder. New run started.");
        }
    }

    void EnterBuilder()
    {
        CurrentMode = Mode.DeckBuilder;
        IsPlayerTurn = false;
        nextTurnTimer = -1f;
        SetPlayerMovementEnabled(true);

        browsable.Clear();
        foreach (CardData c in collection)
            if (c != null && !browsable.Contains(c)) browsable.Add(c);
        browseIndex = Mathf.Clamp(browseIndex, 0, Mathf.Max(0, browsable.Count - 1));

        LogBuilder("Deck builder");
    }

    void SetPlayerMovementEnabled(bool enabled)
    {
        if (!lockPlayerInPlayMode) return;

        GameObject playerObj = GameObject.FindWithTag(playerTag);
        if (playerObj == null) playerObj = transform.root.gameObject;

        foreach (var mb in playerObj.GetComponentsInChildren<MonoBehaviour>())
        {
            string typeName = mb.GetType().Name.ToLower();
            if (mb != this && (typeName.Contains("move") || typeName.Contains("look") || typeName.Contains("controller") || typeName.Contains("player")))
            {
                mb.enabled = enabled;
            }
        }

        if (!enabled)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    CardData CurrentBrowsed() => browsable.Count > 0 ? browsable[browseIndex] : null;

    void MoveBrowse(int dir)
    {
        if (browsable.Count == 0) return;
        browseIndex = (browseIndex + dir + browsable.Count) % browsable.Count;
        LogBuilder("Browsing");
    }

    void AddToDeck(CardData card)
    {
        if (card == null) return;
        if (deck.Count >= deckSize) { Debug.Log($"[Cards] Deck full."); return; }
        if (Count(deck, card) >= Count(collection, card)) { Debug.Log($"[Cards] Don't own more copies."); return; }
        if (Count(deck, card) >= card.maxCopies) { Debug.Log($"[Cards] Copy limit reached."); return; }

        deck.Add(card);
        LogBuilder($"Added {card.displayName}");
    }

    void RemoveFromDeck(CardData card)
    {
        if (card == null) return;
        if (deck.Remove(card)) LogBuilder($"Removed {card.displayName}");
        else Debug.Log($"[Cards] Not in deck.");
    }

    bool DeckIsValid(out string reason)
    {
        reason = null;
        if (deckSize <= handSize) reason = $"Deck Size must be bigger than Hand Size";
        else if (deck.Count != deckSize) reason = $"deck has {deck.Count} cards, needs {deckSize}";
        else if (deck.Contains(null)) reason = "deck contains null slot";
        return reason == null;
    }

    InputAction Find(string actionName)
    {
        if (string.IsNullOrEmpty(actionName)) return null;
        InputAction a = InputSystem.actions != null ? InputSystem.actions.FindAction(actionName) : null;
        if (a == null)
            Debug.LogWarning($"[Cards] Input Action '{actionName}' missing from Input System asset.");
        return a;
    }

    static bool Pressed(InputAction a) => a != null && a.WasPressedThisFrame();

    static int Count(List<CardData> list, CardData card)
    {
        int n = 0;
        foreach (CardData c in list) if (c == card) n++;
        return n;
    }

    static void Shuffle(List<CardData> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    void LogHand(string header)
    {
        var sb = new StringBuilder($"[Cards] {header}\nHand: ");
        for (int i = 0; i < hand.Count; i++)
            sb.Append(i == selected ? "> " : "  ").Append($"[{hand[i].displayName}] ");
        Debug.Log(sb.ToString());
        StateChanged?.Invoke();
    }

    void LogBuilder(string header)
    {
        Debug.Log($"[Cards] {header}");
        StateChanged?.Invoke();
    }
}