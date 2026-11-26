using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Attach to the always-active MenuManager. Panels and buttons stay editable in Unity.
public sealed class SinglePlayerMenu : MonoBehaviour
{
    [SerializeField] GameObject mainMenuPanel;
    [SerializeField] GameObject slotsPanel;
    [SerializeField] GameObject overwritePanel;
    [Tooltip("Exactly three buttons, in slot order 1, 2, 3.")]
    [SerializeField] Button[] slotButtons = new Button[3];
    [SerializeField] Button confirmButton;
    [SerializeField] Button newGameButton;
    [Tooltip("Optional if the panel's MenuPanelFocus already has its Back Button assigned.")]
    [SerializeField] Button backButton;
    [SerializeField] Button overwriteButton;
    [SerializeField] Button cancelOverwriteButton;
    [SerializeField] TMP_Text statusLabel;
    [SerializeField] TMP_Text overwriteLabel;
    [SerializeField] MenuNarration narration;
    [SerializeField] string gameplayScene = "Test Scene";
    [Tooltip("Persistent tint on the chosen slot when keyboard focus moves to an action button.")]
    [SerializeField] Color selectedSlotTint = new Color(0.35f, 0.85f, 0.75f, 1f);

    readonly SaveGameData[] summaries = new SaveGameData[3];
    readonly bool[] occupied = new bool[3];
    readonly bool[] recovered = new bool[3];
    readonly string[] errors = new string[3];
    readonly UnityAction[] slotClicks = new UnityAction[3];
    readonly ColorBlock[] originalSlotColors = new ColorBlock[3];
    int selectedSlot = 1;
    int pendingOverwrite;
    bool wired;
    bool announceOverwrite;
    GameObject lastNarrated;
    Action launchPreview;

    // Runtime-only preview mode: use the existing UI without writing a slot
    // before VR gameplay has a working save/load integration.
    public void ConfigureUnsavedPreview(Action launch) => launchPreview = launch;

    void Awake()
    {
        if (mainMenuPanel == null || slotsPanel == null || overwritePanel == null
            || confirmButton == null || newGameButton == null || statusLabel == null
            || overwriteLabel == null || slotButtons.Length != 3
            || Array.Exists(slotButtons, button => button == null))
        {
            Debug.LogError("Single Player Menu: assign the panels, three slot buttons, Confirm, New Game and both text fields.", this);
            enabled = false;
            return;
        }
        for (int i = 0; i < 3; i++)
        {
            originalSlotColors[i] = slotButtons[i].colors;
            int slot = i + 1;
            slotClicks[i] = () => SelectSlot(slot);
            slotButtons[i].onClick.AddListener(slotClicks[i]);
        }
        confirmButton.onClick.AddListener(ConfirmSelection);
        newGameButton.onClick.AddListener(NewGame);
        ResolveNavigationButtons();
        wired = true;
        slotsPanel.SetActive(false);
        overwritePanel.SetActive(false);
    }

    public void Open()
    {
        if (!wired) return;
        pendingOverwrite = 0;
        overwritePanel.SetActive(false);
        mainMenuPanel.SetActive(false);
        RefreshSlots();
        slotsPanel.SetActive(true);
        lastNarrated = null;
        SelectSlot(selectedSlot);
        ConfigurePanelFocus(slotsPanel, slotButtons[selectedSlot - 1], true);
    }

    public void Back()
    {
        pendingOverwrite = 0;
        overwritePanel.SetActive(false);
        slotsPanel.SetActive(false);
        mainMenuPanel.SetActive(true);
    }

    void RefreshSlots()
    {
        for (int i = 0; i < 3; i++)
        {
            occupied[i] = SaveGames.Files.Exists(i + 1);
            summaries[i] = null;
            recovered[i] = false;
            errors[i] = null;
            if (occupied[i])
                SaveGames.TryReadSlot(i + 1, out summaries[i], out recovered[i], out errors[i]);
            var label = slotButtons[i].GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = Summary(i);
        }
    }

    string Summary(int index)
    {
        string prefix = "Slot " + (index + 1);
        if (!occupied[index]) return prefix + " — Empty";
        var data = summaries[index];
        if (data == null) return prefix + " — Unable to read save";
        string date = DateTime.Parse(data.savedUtc, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime().ToString("g");
        return prefix + " — " + data.roomId + "\n" + date + (recovered[index] ? " (backup)" : "");
    }

    void SelectSlot(int slot)
    {
        selectedSlot = slot;
        for (int i = 0; i < 3; i++)
        {
            var colors = originalSlotColors[i];
            if (i == slot - 1) colors.normalColor = selectedSlotTint;
            slotButtons[i].colors = colors;
        }
        int index = slot - 1;
        bool canContinue = summaries[index] != null;
        confirmButton.interactable = !occupied[index] || canContinue;
        newGameButton.interactable = occupied[index];
        var label = confirmButton.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = occupied[index] ? "Continue" : "Start New Game";
        statusLabel.text = !occupied[index]
            ? "Slot " + slot + " is empty. Confirm to start a new game."
            : canContinue
                ? "Selected slot " + slot + ". Continue your game, or choose New Game to replace it."
                : "Could not read slot " + slot + ": " + errors[index]
                    + " Your save has not been changed. Choose New Game only if you want to replace it.";
        ConfigureSlotNavigation();
        if (launchPreview != null)
        {
            confirmButton.interactable = true;
            newGameButton.interactable = false;
            if (label != null) label.text = "Start VR Test (unsaved)";
            statusLabel.text = "Selected slot " + slot + ". VR preview: starts a new unsaved test run. Existing saves are not loaded or changed.";
        }
    }

    void ResolveNavigationButtons()
    {
        var slotsFocus = slotsPanel.GetComponent<MenuPanelFocus>();
        var overwriteFocus = overwritePanel.GetComponent<MenuPanelFocus>();
        if (backButton == null && slotsFocus != null) backButton = slotsFocus.BackControl;
        if (cancelOverwriteButton == null && overwriteFocus != null)
            cancelOverwriteButton = overwriteFocus.BackControl;
        if (overwriteButton == null && cancelOverwriteButton != null)
        {
            var buttons = overwritePanel.GetComponentsInChildren<Button>(true);
            if (buttons.Length == 2)
                overwriteButton = buttons[0] == cancelOverwriteButton ? buttons[1] : buttons[0];
        }
        if (backButton == null || overwriteButton == null || cancelOverwriteButton == null)
            Debug.LogWarning("Single Player Menu: assign Back Button, Overwrite Button and Cancel Overwrite Button for full keyboard navigation.", this);
    }

    void ConfigureSlotNavigation()
    {
        // Three horizontal slots, then a vertical action list. Explicit links
        // cannot get trapped in the slot row or retain a duplicated button's links.
        var actions = new List<Button>();
        if (confirmButton.interactable) actions.Add(confirmButton);
        if (newGameButton.interactable) actions.Add(newGameButton);
        if (backButton != null && backButton.interactable) actions.Add(backButton);
        for (int i = 0; i < 3; i++)
        {
            slotButtons[i].navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnLeft = slotButtons[(i + 2) % 3],
                selectOnRight = slotButtons[(i + 1) % 3],
                selectOnDown = actions.Count > 0 ? actions[0] : null,
                selectOnUp = actions.Count > 0 ? actions[actions.Count - 1] : null
            };
        }
        for (int i = 0; i < actions.Count; i++)
            actions[i].navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = i > 0 ? actions[i - 1] : slotButtons[selectedSlot - 1],
                selectOnDown = i + 1 < actions.Count ? actions[i + 1] : slotButtons[selectedSlot - 1]
            };
        ConfigurePanelFocus(slotsPanel, slotButtons[selectedSlot - 1], false);
    }

    void ConfigurePanelFocus(GameObject panel, Selectable first, bool reset)
    {
        var focus = panel.GetComponent<MenuPanelFocus>();
        if (focus == null) return;
        var order = new List<Selectable>();
        if (panel == slotsPanel)
        {
            order.AddRange(slotButtons);
            order.Add(confirmButton);
            order.Add(newGameButton);
            if (backButton != null) order.Add(backButton);
        }
        else
        {
            if (cancelOverwriteButton != null) order.Add(cancelOverwriteButton);
            if (overwriteButton != null) order.Add(overwriteButton);
        }
        focus.ConfigureNavigation(first, order.ToArray(), reset);
    }

    void ConfigureOverwriteNavigation()
    {
        if (overwriteButton == null || cancelOverwriteButton == null) return;
        foreach (var button in new[] { overwriteButton, cancelOverwriteButton })
        {
            var other = button == overwriteButton ? cancelOverwriteButton : overwriteButton;
            button.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = other, selectOnDown = other,
                selectOnLeft = other, selectOnRight = other
            };
        }
        // Always start on Cancel rather than remembering Replace Save.
        ConfigurePanelFocus(overwritePanel, cancelOverwriteButton, true);
    }

    void LateUpdate()
    {
        if (!wired || (!slotsPanel.activeInHierarchy && !overwritePanel.activeInHierarchy)) return;
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected == lastNarrated) return;
        lastNarrated = selected;
        if (selected == null) return;
        if (slotsPanel.activeInHierarchy)
        {
            for (int i = 0; i < 3; i++)
                if (selected == slotButtons[i].gameObject)
                {
                    SelectSlot(i + 1);
                    Say(Summary(i).Replace("\n", ". ") + ". " + statusLabel.text);
                    return;
                }
        }
        if (selected.transform.IsChildOf(slotsPanel.transform) || selected.transform.IsChildOf(overwritePanel.transform))
        {
            var label = selected.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                Say(announceOverwrite ? overwriteLabel.text + " " + label.text : label.text);
                announceOverwrite = false;
            }
        }
    }

    public void ConfirmSelection()
    {
        if (!slotsPanel.activeInHierarchy) return;
        if (launchPreview != null) { launchPreview(); return; }
        // Recheck disk state: an occupied slot must never be treated as empty
        // simply because the panel's cached summary was out of date.
        bool exists = SaveGames.Files.Exists(selectedSlot);
        string error;
        bool launched = exists ? SaveGames.Continue(selectedSlot, out error)
            : SaveGames.StartNew(selectedSlot, false, out error, gameplayScene);
        if (!launched) ShowError(error);
    }

    public void NewGame()
    {
        if (launchPreview != null) return;
        if (!slotsPanel.activeInHierarchy) return;
        pendingOverwrite = selectedSlot;
        overwriteLabel.text = "Start a new game in slot " + selectedSlot
            + "? This replaces the current progress in this slot.";
        slotsPanel.SetActive(false);
        ConfigureOverwriteNavigation();
        overwritePanel.SetActive(true);
        lastNarrated = null;
        announceOverwrite = true;
    }

    public void ConfirmOverwrite()
    {
        if (launchPreview != null) return;
        if (!overwritePanel.activeInHierarchy || pendingOverwrite == 0) return;
        if (!SaveGames.StartNew(pendingOverwrite, true, out string error, gameplayScene))
        {
            CancelOverwrite();
            ShowError(error);
        }
    }

    public void CancelOverwrite()
    {
        pendingOverwrite = 0;
        announceOverwrite = false;
        overwritePanel.SetActive(false);
        RefreshSlots();
        slotsPanel.SetActive(true);
        SelectSlot(selectedSlot);
        ConfigurePanelFocus(slotsPanel, slotButtons[selectedSlot - 1], true);
        lastNarrated = null;
    }

    void ShowError(string error)
    {
        statusLabel.text = "Unable to start game: " + error;
        Say(statusLabel.text);
        Debug.LogWarning("[Save Menu] " + error, this);
    }

    void Say(string text) { if (narration != null) narration.Speak(text); }

    void OnDestroy()
    {
        if (!wired) return;
        for (int i = 0; i < 3; i++)
            if (slotButtons[i] != null)
            {
                slotButtons[i].onClick.RemoveListener(slotClicks[i]);
                slotButtons[i].colors = originalSlotColors[i];
            }
        if (confirmButton != null) confirmButton.onClick.RemoveListener(ConfirmSelection);
        if (newGameButton != null) newGameButton.onClick.RemoveListener(NewGame);
    }
}
