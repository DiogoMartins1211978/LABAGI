using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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
    [SerializeField] TMP_Text descriptionText;
    [SerializeField] MenuNarration narration;
    [Header("Book navigation")]
    [Tooltip("Optional visible Close button. Its On Click should call BestiaryController.Close.")]
    [SerializeField] Button closeButton;

    readonly List<BestiaryEntryButton> rows = new List<BestiaryEntryButton>();
    BestiaryEntry selectedEntry;
    TMP_Text[] staticTexts;
    float[] originalFontSizes;
    Coroutine pendingFocus;
    bool subscribed;
    GameObject lastFocusedObject;

    void Awake()
    {
        // Row labels are instantiated later and scale separately from their prefab baseline.
        staticTexts = GetComponentsInChildren<TMP_Text>(true);
        originalFontSizes = new float[staticTexts.Length];
        for (int i = 0; i < staticTexts.Length; i++) originalFontSizes[i] = staticTexts[i].fontSize;
    }

    void Update()
    {
        MenuPanelFocus.HandleAlternateSubmit(transform);
    }

    void OnEnable()
    {
        if (discovery == null || enemyList == null || entryButtonPrefab == null || enemyName == null
            || dropsText == null || regionText == null || descriptionText == null)
        {
            Debug.LogError("Bestiary UI: assign Discovery, Enemy List, Entry Button Prefab and the right-page text fields.", this);
            return;
        }
        discovery.Changed += Rebuild;
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
        for (int i = 0; i < staticTexts.Length; i++)
            if (staticTexts[i] != null)
            {
                staticTexts[i].enableAutoSizing = false;
                staticTexts[i].fontSize = originalFontSizes[i] * scale;
            }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in discovery.Entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.enemyId)) continue;
            if (!ids.Add(entry.enemyId)) continue;
            var row = Instantiate(entryButtonPrefab, enemyList);
            row.gameObject.SetActive(true);
            row.Bind(this, entry, discovery.IsDiscovered(entry), scale);
            rows.Add(row);
        }

        for (int i = 0; i < rows.Count; i++)
        {
            var navigation = new Navigation { mode = Navigation.Mode.Explicit };
            navigation.selectOnUp = i == 0 && CanSelectClose()
                ? closeButton : rows[(i + rows.Count - 1) % rows.Count].Button;
            navigation.selectOnDown = i == rows.Count - 1 && CanSelectClose()
                ? closeButton : rows[(i + 1) % rows.Count].Button;
            rows[i].Button.navigation = navigation;
        }
        if (closeButton != null)
        {
            var navigation = new Navigation { mode = Navigation.Mode.Explicit };
            if (rows.Count > 0)
            {
                navigation.selectOnUp = rows[rows.Count - 1].Button;
                navigation.selectOnDown = rows[0].Button;
            }
            closeButton.navigation = navigation;
        }

        BestiaryEntryButton selectedRow = rows.Find(row => row.Entry == selectedEntry);
        if (selectedRow == null && rows.Count > 0) selectedRow = rows[0];
        if (selectedRow == null)
        {
            ShowEntry(null);
            if (CanSelectClose()) pendingFocus = StartCoroutine(FocusAfterOpening(closeButton));
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
            : "Bestiary. Unknown enemy. Encounter this enemy to reveal its entry.");
    }

    public void ShowEntry(BestiaryEntry entry)
    {
        selectedEntry = entry;
        bool known = entry != null && discovery.IsDiscovered(entry);
        enemyName.text = entry == null ? "No entries available" : known ? EntryName(entry) : "Unknown enemy";
        if (enemyPortrait != null)
        {
            enemyPortrait.sprite = known ? entry.portrait : null;
            enemyPortrait.enabled = enemyPortrait.sprite != null;
            enemyPortrait.color = Color.white;
            enemyPortrait.preserveAspect = true;
        }
        dropsText.text = "Drops: " + (known ? DropNames(entry) : "Unknown");
        regionText.text = "Region: " + (known ? RegionNames(entry) : "Unknown");
        descriptionText.text = entry == null ? "Add entries to the bestiary catalogue."
            : !known ? "Encounter this enemy to reveal its entry."
            : string.IsNullOrWhiteSpace(entry.description) ? "Description unavailable." : entry.description;
    }

    public void ReadSelectedEntry()
    {
        Say(enemyName.text + ". " + dropsText.text + ". " + regionText.text + ". " + descriptionText.text);
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

    static string RegionNames(BestiaryEntry entry)
    {
        var names = new List<string>();
        if (entry.regions != null)
            foreach (var region in entry.regions)
                if (region != null) names.Add(string.IsNullOrWhiteSpace(region.displayName) ? region.name : region.displayName);
        return names.Count == 0 ? "Unknown" : string.Join(", ", names);
    }

    void Say(string text)
    {
        if (narration != null) narration.Speak(text);
    }

    bool CanSelectClose() => closeButton != null && closeButton.IsActive() && closeButton.IsInteractable();

    void LateUpdate()
    {
        var current = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (current == lastFocusedObject) return;
        lastFocusedObject = current;
        if (closeButton != null && current == closeButton.gameObject) Say("Close bestiary.");
    }

    void OnDisable()
    {
        if (subscribed && discovery != null) discovery.Changed -= Rebuild;
        subscribed = false;
        StopAllCoroutines();
        pendingFocus = null;
        lastFocusedObject = null;
    }
}
