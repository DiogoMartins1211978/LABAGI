using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BestiaryDropCarousel : MonoBehaviour
{
    [SerializeField] BookScrollRect scroll;
    [SerializeField] RectTransform content;
    [SerializeField] BestiaryDropSlot slotPrefab;
    [SerializeField] Button previous, next;
    [SerializeField] TMP_Text heading;
    [SerializeField] MenuNarration narration;
    [SerializeField] float slotWidth = 88, slotHeight = 123, spacing = 12;
    [SerializeField, Min(1)] int visibleSlots = 3;

    readonly List<BestiaryDropSlot> slots = new List<BestiaryDropSlot>();
    BestiaryEntry currentEntry;
    CombatManager[] cards = Array.Empty<CombatManager>();
    bool informationKnown;
    float textScale = 1;
    int firstVisible;
    public event Action NavigationChanged;
    public int Count => cards.Length;
    public int VisibleSlotCount => slots.Count;
    public int FirstVisibleIndex => firstVisible;
    public BookScrollRect Scroll => scroll;

    public IEnumerable<Button> Controls
    {
        get
        {
            if (previous.interactable) yield return previous;
            foreach (var slot in slots) if (slot.Button.interactable) yield return slot.Button;
            if (next.interactable) yield return next;
        }
    }

    void OnEnable()
    {
        previous.onClick.AddListener(Previous);
        next.onClick.AddListener(Next);
        scroll.PageRequested += Step;
    }

    void OnDisable()
    {
        previous.onClick.RemoveListener(Previous);
        next.onClick.RemoveListener(Next);
        scroll.PageRequested -= Step;
    }

    public void SetEntry(BestiaryEntry entry, bool known, float scale)
    {
        heading.text = "Drops";
        bool available = known && entry != null && entry.dropInformationKnown;
        if (currentEntry == entry && informationKnown == available && Mathf.Approximately(textScale, scale) && slots.Count == visibleSlots) return;
        currentEntry = entry;
        informationKnown = available;
        textScale = scale;
        cards = available && entry.possibleDrops != null ? entry.possibleDrops : Array.Empty<CombatManager>();
        firstVisible = 0;
        while (slots.Count < visibleSlots)
        {
            var slot = Instantiate(slotPrefab, content);
            slot.gameObject.SetActive(true);
            slots.Add(slot);
        }
        while (slots.Count > visibleSlots)
        {
            var last = slots[slots.Count - 1];
            slots.RemoveAt(slots.Count - 1);
            last.gameObject.SetActive(false);
            Destroy(last.gameObject);
        }
        scroll.StopMovement();
        content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, scroll.viewport.rect.width);
        content.anchoredPosition = Vector2.zero;
        RefreshSlots();
    }

    void RefreshSlots()
    {
        float width = visibleSlots * slotWidth + (visibleSlots - 1) * spacing;
        float left = (scroll.viewport.rect.width - width) / 2;
        for (int i = 0; i < slots.Count; i++)
        {
            var rect = (RectTransform)slots[i].transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, .5f);
            rect.pivot = new Vector2(0, .5f);
            rect.anchoredPosition = new Vector2(left + i * (slotWidth + spacing), 0);
            rect.sizeDelta = new Vector2(slotWidth, slotHeight);
            int index = firstVisible + i;
            slots[i].Bind(this, index < cards.Length ? cards[index] : null, index, textScale, !informationKnown);
        }
        previous.interactable = firstVisible > 0;
        next.interactable = firstVisible < Mathf.Max(0, cards.Length - visibleSlots);
        NavigationChanged?.Invoke();
    }

    public void Describe(CombatManager card, int index, bool details = false)
    {
        if (narration == null) return;
        narration.Speak(card == null ? "Drop information unavailable." : "Possible drop. " + card.displayName + ". " + (index + 1) + " of " + cards.Length + "." + (details ? " " + card.description : " Activate to read details."));
    }

    public void Reveal(int index)
    {
        int target = index < firstVisible ? index : index >= firstVisible + visibleSlots ? index - visibleSlots + 1 : firstVisible;
        target = Mathf.Clamp(target, 0, Mathf.Max(0, cards.Length - visibleSlots));
        if (target == firstVisible) return;
        firstVisible = target;
        RefreshSlots();
    }

    public void Previous() => Step(-1);
    public void Next() => Step(1);

    void Step(int direction)
    {
        int target = Mathf.Clamp(firstVisible + direction, 0, Mathf.Max(0, cards.Length - visibleSlots));
        if (target == firstVisible) return;
        firstVisible = target;
        RefreshSlots();
        if (narration != null) narration.Speak("Possible drops " + (firstVisible + 1) + " to " + Mathf.Min(cards.Length, firstVisible + visibleSlots) + " of " + cards.Length + ".");
    }
}
