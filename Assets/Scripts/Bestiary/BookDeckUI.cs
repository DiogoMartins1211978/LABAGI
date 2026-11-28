using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class BookDeckUI : MonoBehaviour
{
    [SerializeField] CardSystem cards;
    [SerializeField] BestiaryBookView bookView;
    [SerializeField] Transform collectionGrid, deckGrid;
    [SerializeField] BookDeckSlot slotPrefab;
    [SerializeField] Button collectionPrevious, collectionNext, deckPrevious, deckNext;
    [SerializeField] Button bestiaryTab, deckTab, closeButton;
    [SerializeField] TMP_Text collectionFolio, deckFolio, deckHeading;
    [SerializeField] MenuNarration narration;
    [SerializeField] BookTypography typography;
    readonly List<BookDeckSlot> collectionSlots = new List<BookDeckSlot>(), deckSlots = new List<BookDeckSlot>();
    readonly List<Button> controls = new List<Button>();
    TMP_Text[] texts;
    float[] fontSizes;
    int collectionPage, deckPage, collectionPageCount = 1, deckPageCount = 1;
    Coroutine pendingFocus;
    bool vrControls;
    public void ConfigureVR() => vrControls = true;
    public void ShowMinimumRequired()
    {
        if (deckHeading != null && cards != null)
            deckHeading.text = "Need " + cards.minDeckRequired + " cards to close";
    }
    public bool CanEdit => cards != null && cards.CurrentMode != CardSystem.Mode.Combat;

    void Awake()
    {
        texts = collectionGrid.parent.GetComponentsInChildren<TMP_Text>(true).Concat(deckGrid.parent.GetComponentsInChildren<TMP_Text>(true)).ToArray();
        fontSizes = texts.Select(t => t.fontSize).ToArray();
    }
    void OnEnable()
    {
        cards.SetBookDeckOpen(true);
        cards.StateChanged += Refresh;
        collectionPrevious.onClick.AddListener(PreviousCollection); collectionNext.onClick.AddListener(NextCollection);
        deckPrevious.onClick.AddListener(PreviousDeck); deckNext.onClick.AddListener(NextDeck);
        Refresh();
    }
    void OnDisable()
    {
        if (cards != null) { cards.SetBookDeckOpen(false); cards.StateChanged -= Refresh; }
        collectionPrevious.onClick.RemoveListener(PreviousCollection); collectionNext.onClick.RemoveListener(NextCollection);
        deckPrevious.onClick.RemoveListener(PreviousDeck); deckNext.onClick.RemoveListener(NextDeck);
        if (pendingFocus != null) StopCoroutine(pendingFocus);
        pendingFocus = null;
    }
    void Update()
    {
        if (vrControls) return; // XR rays and VRMenuNavigation submit the buttons.
        BookInput.HandleInteract(collectionGrid.parent);
        BookInput.HandleInteract(deckGrid.parent);
        BookInput.HandleInteract(closeButton.transform.parent);
        if (Keyboard.current == null || !Keyboard.current.tabKey.wasPressedThisFrame || controls.Count == 0 || EventSystem.current == null) return;
        int index = controls.FindIndex(b => b.gameObject == EventSystem.current.currentSelectedGameObject);
        int direction = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed ? -1 : 1;
        for (int i = 1; i <= controls.Count; i++)
        {
            var b = controls[(index + direction * i + controls.Count * 2) % controls.Count];
            if (b.IsActive() && b.IsInteractable()) { b.Select(); break; }
        }
    }
    int Capacity(Transform grid) => grid.GetComponent<BookGridLayout>().Capacity;
    void EnsureSlots(List<BookDeckSlot> list, Transform parent)
    {
        while (list.Count < Capacity(parent)) { var slot = Instantiate(slotPrefab, parent); slot.gameObject.SetActive(true); list.Add(slot); }
    }
    public void Refresh()
    {
        var collectionEntries = new List<(CardData card, int level)>();
        var ownedLevels = new Dictionary<CardData, int>();

        foreach (var card in cards.collection)
        {
            if (card == null) continue;
            ownedLevels.TryGetValue(card, out int level);
            level++;
            ownedLevels[card] = level;
            collectionEntries.Add((card, level));
        }

        int leftCapacity = Capacity(collectionGrid), rightCapacity = Capacity(deckGrid);
        int leftPages = Mathf.Max(1, Mathf.CeilToInt(collectionEntries.Count / (float)leftCapacity));
        int rightPages = Mathf.Max(1, Mathf.CeilToInt(cards.deckSize / (float)rightCapacity));
        collectionPageCount = leftPages; deckPageCount = rightPages;
        collectionPage = (collectionPage % leftPages + leftPages) % leftPages;
        deckPage = (deckPage % rightPages + rightPages) % rightPages;
        int setting = Mathf.Clamp(PlayerPrefs.GetInt("Accessibility.TextSize", 0), 0, 2);
        float scale = setting == 1 ? 1.2f : setting == 2 ? 1.4f : 1;
        if (typography != null) typography.Apply();
        else for (int i = 0; i < texts.Length; i++) if (texts[i] != null) texts[i].fontSize = fontSizes[i] * scale;
        EnsureSlots(collectionSlots, collectionGrid); EnsureSlots(deckSlots, deckGrid);
        for (int i = 0; i < leftCapacity; i++)
        {
            int index = collectionPage * leftCapacity + i;
            CardData card = null;
            int level = 0;
            if (index < collectionEntries.Count)
            {
                card = collectionEntries[index].card;
                level = collectionEntries[index].level;
            }
            collectionSlots[i].Bind(this, card, false, level, scale);
        }
        var deckLevels = new Dictionary<CardData, int>();

        for (int i = 0; i < rightCapacity; i++)
        {
            int index = deckPage * rightCapacity + i;

            CardData card = index < cards.deck.Count && index < cards.deckSize
                ? cards.deck[index]
                : null;

            int level = 0;

            if (card != null)
            {
                deckLevels.TryGetValue(card, out level);
                level++;
                deckLevels[card] = level;
            }

            deckSlots[i].Bind(this, card, true, level, scale);
        }
        deckHeading.text = vrControls ? "Deck — minimum " + cards.minDeckRequired : "Deck";
        collectionFolio.text = "— " + BestiaryUI.Roman(collectionPage + 1) + " —";
        deckFolio.text = "— " + (cards.deck.Count == 0 ? "0" : BestiaryUI.Roman(cards.deck.Count))
            + " / " + BestiaryUI.Roman(cards.deckSize) + " —";
        collectionPrevious.interactable = collectionNext.interactable = leftPages > 1;
        deckPrevious.interactable = deckNext.interactable = rightPages > 1;
        controls.Clear();
        controls.AddRange(collectionSlots.Where(s => s.Button.interactable).Select(s => s.Button));
        if (collectionPrevious.interactable) controls.Add(collectionPrevious); if (collectionNext.interactable) controls.Add(collectionNext);
        controls.AddRange(deckSlots.Where(s => s.Button.interactable).Select(s => s.Button));
        if (deckPrevious.interactable) controls.Add(deckPrevious); if (deckNext.interactable) controls.Add(deckNext);
        controls.Add(bestiaryTab); if (deckTab.interactable) controls.Add(deckTab); controls.Add(closeButton);
        for (int i = 0; i < controls.Count; i++)
        {
            var nav = new Navigation { mode = Navigation.Mode.Explicit };
            nav.selectOnLeft = nav.selectOnUp = controls[(i + controls.Count - 1) % controls.Count];
            nav.selectOnRight = nav.selectOnDown = controls[(i + 1) % controls.Count]; controls[i].navigation = nav;
        }
        if (pendingFocus != null) StopCoroutine(pendingFocus);
        pendingFocus = StartCoroutine(FocusWhenReady());
    }
    IEnumerator FocusWhenReady()
    {
        yield return null; yield return null;
        while (bookView.IsTransitioning) yield return null;
        pendingFocus = null;
        if (EventSystem.current == null) yield break;
        var selected = EventSystem.current.currentSelectedGameObject;
        var current = selected != null ? selected.GetComponent<Button>() : null;
        if (current != null && current.IsActive() && current.IsInteractable()) yield break;
        controls.FirstOrDefault(b => b.IsActive() && b.IsInteractable())?.Select();
    }
    public void Activate(CardData card, bool inDeck)
    {
        if (bookView != null && bookView.IsTransitioning) return;
        if (!CanEdit) { Say("Deck editing is unavailable during combat."); return; }
        int before = cards.deck.Count;
        if (inDeck) cards.RemoveFromDeck(card); else cards.AddToDeck(card);
        if (cards.deck.Count != before) Say((inDeck ? "Removed " : "Added ") + card.displayName + ". Deck " + cards.deck.Count + " of " + cards.deckSize + ".");
        else Say(cards.deck.Count >= cards.deckSize ? "Deck full. Maximum " + cards.deckSize + " cards." : "No additional owned copies can be added. This card's limit is " + card.maxCopies + ".");
    }
    public void Describe(CardData card, bool inDeck)
    {
        Say((inDeck ? "Remove " : "Add ") + card.displayName + (inDeck ? " from the deck. " : " to the deck. ")
            + "Owned " + cards.collection.Count(c => c == card) + ". In deck " + cards.deck.Count(c => c == card) + ". " + card.description);
    }
    void Say(string text) { if (narration != null) narration.Speak(text); }
    void Page(bool collection, int direction)
    {
        var button = collection ? direction > 0 ? collectionNext : collectionPrevious : direction > 0 ? deckNext : deckPrevious;
        if (!button.interactable || bookView.IsTransitioning) return;
        bookView.TurnPage(() => { if (collection) collectionPage = (collectionPage + direction + collectionPageCount) % collectionPageCount; else deckPage = (deckPage + direction + deckPageCount) % deckPageCount; Refresh(); Say((collection ? "Collection" : "Deck") + " page " + ((collection ? collectionPage : deckPage) + 1) + "."); }, direction < 0);
    }
    public void NextCollection() => Page(true, 1);
    public void PreviousCollection() => Page(true, -1);
    public void NextDeck() => Page(false, 1);
    public void PreviousDeck() => Page(false, -1);
}
