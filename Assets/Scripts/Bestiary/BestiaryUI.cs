using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.Events;

// Put this on BestiaryPanel so it subscribes/refreshes whenever the book opens.
public class BestiaryUI : MonoBehaviour
{
    [Header("Left page")]
    [SerializeField] BestiaryDiscovery discovery;
    [SerializeField] Transform enemyList;
    [SerializeField] BestiaryEntryButton entryButtonPrefab;

    [Header("Right page")]
    [SerializeField] TMP_Text enemyName;
    [SerializeField] Image enemyPortrait;
    [SerializeField] TMP_Text dropsText;
    [SerializeField] TMP_Text regionText;
    [SerializeField] BestiaryDropCarousel dropCarousel;
    [SerializeField] TMP_Text descriptionText;
    [SerializeField] MenuNarration narration;
    [SerializeField] BookTypography typography;
    Transform rightPageRoot;
    [Header("Book navigation")]
    [Tooltip("Optional visible Close button. Its On Click should call BestiaryController.Close.")]
    [SerializeField] Button closeButton;
    [SerializeField] BestiaryBookView bookView;
    [SerializeField] Button previousPage;
    [SerializeField] Button nextPage;
    [SerializeField] TMP_Text pageNumber;
    [SerializeField] Button bestiaryBookmark;
    [SerializeField] Button deckBookmark;
    [Header("Echo inspection")]
    [SerializeField] Button inspectEchoButton;
    [SerializeField] UnityEvent<BestiaryEntry> inspectEchoRequested = new UnityEvent<BestiaryEntry>();
    public event Action<BestiaryEntry> EchoInspectionRequested;
    BookGridLayout gridLayout;
    int PageSize => gridLayout != null ? gridLayout.Capacity : 6;
    int pageIndex;
    int pageCount = 1;
    float textScale = 1;

    readonly List<BestiaryEntryButton> rows = new List<BestiaryEntryButton>();
    readonly List<Button> selectable = new List<Button>();
    BestiaryEntry selectedEntry;
    TMP_Text[] staticTexts;
    float[] originalFontSizes;
    Coroutine pendingFocus;
    bool subscribed;
    GameObject lastFocusedObject;

    void Awake()
    {
        if (enemyList != null) gridLayout = enemyList.GetComponent<BookGridLayout>();
        // Row labels are instantiated later and scale separately from their prefab baseline.
        var texts = new HashSet<TMP_Text>(GetComponentsInChildren<TMP_Text>(true));
        if (enemyList != null) foreach (var text in enemyList.parent.GetComponentsInChildren<TMP_Text>(true)) texts.Add(text);
        if (enemyName != null)
        {
            var layout = enemyName.GetComponentInParent<BookPageLayout>(true);
            rightPageRoot = layout != null ? layout.transform : enemyName.transform.parent;
            foreach (var text in rightPageRoot.GetComponentsInChildren<TMP_Text>(true)) texts.Add(text);
        }
        if (closeButton != null) foreach (var text in closeButton.transform.parent.GetComponentsInChildren<TMP_Text>(true)) texts.Add(text);
        staticTexts = new TMP_Text[texts.Count];
        texts.CopyTo(staticTexts);
        originalFontSizes = new float[staticTexts.Length];
        for (int i = 0; i < staticTexts.Length; i++) originalFontSizes[i] = staticTexts[i].fontSize;
    }

    void Update()
    {
        BookInput.HandleInteract(transform);
        if (enemyList != null) BookInput.HandleInteract(enemyList.parent);
        if (rightPageRoot != null) BookInput.HandleInteract(rightPageRoot);
        if (closeButton != null) BookInput.HandleInteract(closeButton.transform.parent);
        var keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.tabKey.wasPressedThisFrame || EventSystem.current == null) return;
        var current = EventSystem.current.currentSelectedGameObject;
        int index = selectable.FindIndex(b => b != null && b.gameObject == current);
        int direction = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed ? -1 : 1;
        for (int step = 1; step <= selectable.Count; step++)
        {
            var target = selectable[(index + direction * step + selectable.Count * 2) % selectable.Count];
            if (target == null || !target.IsActive() || !target.IsInteractable()) continue;
            target.Select();
            break;
        }
    }

    void OnEnable()
    {
        if (discovery == null || enemyList == null || entryButtonPrefab == null || enemyName == null
            || dropsText == null || descriptionText == null)
        {
            Debug.LogError("Bestiary UI: assign Discovery, Enemy List, Entry Button Prefab and the right-page text fields.", this);
            return;
        }
        discovery.Changed += Rebuild;
        if (dropCarousel != null) dropCarousel.NavigationChanged += RefreshNavigation;
        if (previousPage != null) previousPage.onClick.AddListener(PreviousPage);
        if (nextPage != null) nextPage.onClick.AddListener(NextPage);
        if (inspectEchoButton != null) inspectEchoButton.onClick.AddListener(InspectEcho);
        subscribed = true;
        Rebuild();
    }

    void Rebuild()
    {
        if (pendingFocus != null) StopCoroutine(pendingFocus);
        var events = EventSystem.current;
        bool wasSelectingRow = events != null && events.currentSelectedGameObject != null
            && events.currentSelectedGameObject.transform.IsChildOf(enemyList);
        if (wasSelectingRow) events.SetSelectedGameObject(null);
        foreach (var row in rows)
            if (row != null)
            {
                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
            }
        rows.Clear();

        int size = Mathf.Clamp(PlayerPrefs.GetInt("Accessibility.TextSize", 0), 0, 2);
        float scale = size == 1 ? 1.2f : size == 2 ? 1.4f : 1f;
        textScale = scale;
        if (typography != null) typography.Apply();
        else for (int i = 0; i < staticTexts.Length; i++)
            if (staticTexts[i] != null)
            {
                staticTexts[i].enableAutoSizing = false;
                staticTexts[i].fontSize = originalFontSizes[i] * scale;
            }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<BestiaryEntry>();
        foreach (var entry in discovery.Entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.enemyId)) continue;
            if (!ids.Add(entry.enemyId)) continue;
            entries.Add(entry);
        }
        pageCount = Mathf.Max(1, Mathf.CeilToInt(entries.Count / (float)PageSize));
        pageIndex = (pageIndex % pageCount + pageCount) % pageCount;
        if (previousPage != null) previousPage.interactable = pageCount > 1;
        if (nextPage != null) nextPage.interactable = pageCount > 1;
        if (pageNumber != null) pageNumber.text = "— " + Roman(pageIndex + 1) + " —";
        for (int i = 0; i < PageSize; i++)
        {
            int index = pageIndex * PageSize + i;
            var entry = index < entries.Count ? entries[index] : null;
            var row = Instantiate(entryButtonPrefab, enemyList);
            row.gameObject.SetActive(true);
            row.Bind(this, entry, discovery.IsDiscovered(entry), scale);
            rows.Add(row);
        }

        BestiaryEntryButton selectedRow = rows.Find(row => row.Entry != null && row.Entry == selectedEntry && row.Button.interactable);
        if (selectedRow == null) selectedRow = rows.Find(row => row.Button.interactable);
        if (selectedRow == null)
        {
            ShowEntry(null);
            if (closeButton != null) pendingFocus = StartCoroutine(FocusAfterOpening(closeButton));
            return;
        }
        ShowEntry(selectedRow.Entry);
        pendingFocus = StartCoroutine(FocusAfterOpening(selectedRow.Button));
    }

    IEnumerator FocusAfterOpening(Button button)
    {
        // The pause controller clears old gameplay selection on the first frame.
        yield return null;
        yield return null;
        while (bookView != null && bookView.IsTransitioning) yield return null;
        pendingFocus = null;
        if (button == null || !button.IsActive() || EventSystem.current == null) yield break;
        GameObject current = EventSystem.current.currentSelectedGameObject;
        if (current != null && current.transform.IsChildOf(transform)) yield break;
        button.Select();
    }

    public void FocusEntry(BestiaryEntry entry)
    {
        ShowEntry(entry);
        Say(discovery.IsDiscovered(entry)
            ? "Bestiary. " + EntryName(entry) + ". Activate to read details."
            : "Bestiary. Unknown enemy. Defeat this enemy to reveal its entry.");
    }

    public void ShowEntry(BestiaryEntry entry)
    {
        selectedEntry = entry;
        bool known = entry != null && discovery.IsDiscovered(entry);
        if (inspectEchoButton != null) inspectEchoButton.interactable = known;
        enemyName.text = entry == null ? "Bestiary" : known ? EntryName(entry) : "Unknown enemy";
        if (enemyPortrait != null)
        {
            enemyPortrait.sprite = known ? entry.portrait : null;
            enemyPortrait.enabled = enemyPortrait.sprite != null;
            enemyPortrait.color = Color.white;
            enemyPortrait.preserveAspect = true;
        }
        if (dropCarousel == null) dropsText.text = known ? "Drops: " + DropNames(entry) : "Drops · Unknown";
        if (regionText != null) regionText.gameObject.SetActive(false);
        descriptionText.text = entry == null ? "Defeat enemies to unlock their entries."
            : !known ? "Defeat this enemy to reveal its entry."
            : string.IsNullOrWhiteSpace(entry.description) ? "Description unavailable." : entry.description;
        if (descriptionText.text.Trim('-', ' ', '\r', '\n').Length == 0) descriptionText.text = string.Empty;
        if (dropCarousel != null) dropCarousel.SetEntry(entry,known,textScale);
        RefreshNavigation();
    }

    public void ReadSelectedEntry()
    {
        Say(enemyName.text + ". " + descriptionText.text + ". Drops: " + (selectedEntry != null && discovery.IsDiscovered(selectedEntry) ? DropNames(selectedEntry) : "Unknown"));
    }

    public void InspectEcho()
    {
        if (selectedEntry == null || !discovery.IsDiscovered(selectedEntry)) return;
        if (EchoInspectionRequested == null && inspectEchoRequested.GetPersistentEventCount() == 0)
        {
            Say("Echo inspection is not available yet.");
            return;
        }
        inspectEchoRequested.Invoke(selectedEntry);
        EchoInspectionRequested?.Invoke(selectedEntry);
    }

    public static string EntryName(BestiaryEntry entry) => string.IsNullOrWhiteSpace(entry.displayName)
        ? entry.name : entry.displayName;

    static string DropNames(BestiaryEntry entry)
    {
        if (!entry.dropInformationKnown) return "Unknown";
        if (entry.possibleDrops == null || entry.possibleDrops.Length == 0) return "None";
        var names = new List<string>();
        foreach (var card in entry.possibleDrops)
            names.Add(card == null ? "Unknown card" : string.IsNullOrWhiteSpace(card.displayName) ? card.name : card.displayName);
        return string.Join(", ", names);
    }

    void RefreshNavigation()
    {
        selectable.Clear();
        foreach (var row in rows) if (row != null && row.Button.interactable) selectable.Add(row.Button);
        if (previousPage != null && previousPage.interactable) selectable.Add(previousPage);
        if (nextPage != null && nextPage.interactable) selectable.Add(nextPage);
        if (inspectEchoButton != null && inspectEchoButton.interactable) selectable.Add(inspectEchoButton);
        if (dropCarousel != null) selectable.AddRange(dropCarousel.Controls);
        if (bestiaryBookmark != null) selectable.Add(bestiaryBookmark);
        if (deckBookmark != null && deckBookmark.interactable) selectable.Add(deckBookmark);
        if (closeButton != null) selectable.Add(closeButton);
        for (int i = 0; i < selectable.Count; i++)
        {
            var nav = new Navigation { mode = Navigation.Mode.Explicit };
            nav.selectOnLeft = nav.selectOnUp = selectable[(i + selectable.Count - 1) % selectable.Count];
            nav.selectOnRight = nav.selectOnDown = selectable[(i + 1) % selectable.Count];
            selectable[i].navigation = nav;
        }
    }

    public static string Roman(int value)
    {
        if (value > 3999) return value.ToString();
        string result = "";
        int[] numbers = {1000,900,500,400,100,90,50,40,10,9,5,4,1};
        string[] letters = {"M","CM","D","CD","C","XC","L","XL","X","IX","V","IV","I"};
        for(int i=0;i<numbers.Length;i++) while(value>=numbers[i]){result+=letters[i];value-=numbers[i];}
        return result;
    }

    void Say(string text)
    {
        if (narration != null) narration.Speak(text);
    }

    public void NextPage() => ChangePage(1);
    public void PreviousPage() => ChangePage(-1);

    void ChangePage(int direction)
    {
        var button = direction > 0 ? nextPage : previousPage;
        if (button == null || !button.interactable || (bookView != null && bookView.IsTransitioning)) return;
        void UpdatePage() { pageIndex = (pageIndex + direction + pageCount) % pageCount; selectedEntry = null; Rebuild(); Say("Bestiary. Page " + (pageIndex + 1) + "."); }
        if (bookView != null) bookView.TurnPage(UpdatePage, direction < 0);
        else UpdatePage();
    }

    public void ReadCatalogue()
    {
        int locked = 0;
        foreach (var row in rows) if (row.Entry != null && !discovery.IsDiscovered(row.Entry)) locked++;
        Say("Open Bestiary. Browse defeated enemies. Page " + (pageIndex + 1) + ". " + locked + " locked entries on this page. Defeat enemies to unlock them.");
    }

    void LateUpdate()
    {
        var current = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (current == lastFocusedObject) return;
        lastFocusedObject = current;
        // Static controls provide their full action labels through BookControlNarration.
    }

    void OnDisable()
    {
        if (subscribed && discovery != null) discovery.Changed -= Rebuild;
        subscribed = false;
        if (dropCarousel != null) dropCarousel.NavigationChanged -= RefreshNavigation;
        if (previousPage != null) previousPage.onClick.RemoveListener(PreviousPage);
        if (nextPage != null) nextPage.onClick.RemoveListener(NextPage);
        if (inspectEchoButton != null) inspectEchoButton.onClick.RemoveListener(InspectEcho);
        StopAllCoroutines();
        pendingFocus = null;
        lastFocusedObject = null;
    }
}
