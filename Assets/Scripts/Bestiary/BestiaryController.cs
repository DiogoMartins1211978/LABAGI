using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Attach to an always-active BestiaryManager, outside BestiaryPanel.
[DefaultExecutionOrder(-9000)]
public class BestiaryController : MonoBehaviour
{
    [SerializeField] PauseMenuController pauseController;
    [SerializeField] GameObject bestiaryPanel;
    [SerializeField] Camera previewCamera;
    [SerializeField] Selectable firstSelected;
    [SerializeField] MenuNarration narration;
    [SerializeField] BestiaryBookView bookView;
    [Header("Book sections")]
    [SerializeField] CardSystem cards;
    [SerializeField] BestiaryUI bestiaryUI;
    [SerializeField] BookDeckUI deckUI;
    [SerializeField] GameObject bestiaryLeft, bestiaryRight, deckLeft, deckRight;
    [SerializeField] Button bestiaryTab, deckTab;
    [SerializeField] BookTypography typography;
    public enum BookSection { Bestiary, Deck }
    public BookSection Section { get; private set; }

    InputAction toggleAction;
    InputAction deckAction;
    bool ownsActionEnable;
    bool ownsDeckActionEnable;
    bool deckVisited;
    bool closing;
    [SerializeField, Min(0)] float toggleCooldown = .4f;
    float nextToggleTime;

    public bool IsOpen => pauseController != null && pauseController.IsShowingOverlay(bestiaryPanel);

    void Awake()
    {
        toggleAction = InputSystem.actions?.FindAction("Player/ToggleBestiary");
        deckAction = InputSystem.actions?.FindAction("Player/ToggleDeck");
    }

    void OnEnable()
    {
        if (toggleAction != null && !toggleAction.enabled)
        {
            toggleAction.Enable();
            ownsActionEnable = true;
        }
        if (deckAction != null && !deckAction.enabled) { deckAction.Enable(); ownsDeckActionEnable = true; }
    }

    void Start()
    {
        if (pauseController == null || bestiaryPanel == null || previewCamera == null)
        {
            Debug.LogError("Bestiary Controller: assign Pause Controller, Bestiary Panel and Preview Camera.", this);
            enabled = false;
            return;
        }
        if (transform.IsChildOf(bestiaryPanel.transform) || previewCamera == Camera.main)
        {
            Debug.LogError("Keep BestiaryManager outside BestiaryPanel and assign the separate book preview camera.", this);
            enabled = false;
            return;
        }
        bestiaryPanel.SetActive(false);
        previewCamera.enabled = false;
        SelectSection(BookSection.Bestiary);
        if (bestiaryTab != null) bestiaryTab.onClick.AddListener(OpenBestiary);
        if (deckTab != null) deckTab.onClick.AddListener(OpenDeck);
    }

    void Update()
    {
        if (ControlRemapping.BlocksMenuBack) return;
        bool togglePressed = toggleAction != null ? toggleAction.WasPressedThisFrame()
            : Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame;
        bool deckPressed = deckAction != null && deckAction.WasPressedThisFrame();
        bool cancelPressed = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            || (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);

        if (deckPressed && !IsEditingText())
        {
            if (IsOpen && Section == BookSection.Deck) ToggleClose(); else OpenDeck();
        }
        else if (togglePressed && !IsEditingText()) Toggle();
        else if (cancelPressed && IsOpen) Close();
    }

    void LateUpdate()
    {
        // Stop rendering the book when hidden, including an externally closed overlay.
        if (previewCamera != null && previewCamera != Camera.main)
            previewCamera.enabled = IsOpen;
        if (!IsOpen && bookView != null && bookView.IsVisible) { closing = false; bookView.HideImmediately(); }
        if (deckTab != null) deckTab.interactable = cards != null && cards.CurrentMode != CardSystem.Mode.Combat;
    }

    public void Toggle()
    {
        if (Time.unscaledTime < nextToggleTime || closing || (bookView != null && bookView.IsTransitioning)) return;
        if (IsOpen && Section == BookSection.Bestiary) Close(); else OpenBestiary();
    }

    void ToggleClose()
    {
        if (Time.unscaledTime < nextToggleTime || closing || (bookView != null && bookView.IsTransitioning)) return;
        Close();
    }

    public void Open() => OpenBestiary();
    public void OpenBestiary()
    {
        if (IsOpen)
        {
            if (Section == BookSection.Bestiary) { if (bestiaryUI != null) bestiaryUI.ReadCatalogue(); }
            else SwitchSection(BookSection.Bestiary);
        }
        else OpenBook(BookSection.Bestiary);
    }

    public void OpenDeck()
    {
        if (cards == null || deckUI == null || cards.CurrentMode == CardSystem.Mode.Combat)
        {
            if (narration != null) narration.Speak("Deck editing is unavailable during combat.");
            return;
        }
        if (IsOpen)
        {
            if (Section != BookSection.Deck) SwitchSection(BookSection.Deck);
            else if (narration != null) narration.Speak("Deck. Current section. Collection on the left. Current deck on the right.");
        }
        else OpenBook(BookSection.Deck);
    }

    void SwitchSection(BookSection section)
    {
        if (closing || bookView.IsTransitioning) return;
        UpdateSectionTabs(section);
        bookView.TurnPage(() => SelectSection(section));
    }

    void UpdateSectionTabs(BookSection section)
    {
        if (bestiaryTab != null) bestiaryTab.GetComponent<BookBookmark>()?.SetCurrentSection(section == BookSection.Bestiary);
        if (deckTab != null) deckTab.GetComponent<BookBookmark>()?.SetCurrentSection(section == BookSection.Deck);
    }

    void SelectSection(BookSection section)
    {
        if (typography != null) typography.Apply();
        Section = section;
        bool deck = section == BookSection.Deck;
        UpdateSectionTabs(section);
        if (bestiaryLeft != null) bestiaryLeft.SetActive(!deck);
        if (bestiaryRight != null) bestiaryRight.SetActive(!deck);
        if (deckLeft != null) deckLeft.SetActive(deck);
        if (deckRight != null) deckRight.SetActive(deck);
        if (bestiaryUI != null) bestiaryUI.enabled = !deck;
        if (deckUI != null) deckUI.enabled = deck;
        if (deck && IsOpen) deckVisited = true;
    }

    void OpenBook(BookSection section)
    {
        if (Time.unscaledTime < nextToggleTime || closing || (bookView != null && bookView.IsTransitioning)
            || !isActiveAndEnabled || pauseController == null || bestiaryPanel == null || previewCamera == null
            || PauseMenuController.BlocksGameplayInput || ControlRemapping.BlocksMenuBack) return;
        // The list rebuilds when the panel enables; activate its physical page
        // parents first so newly instantiated buttons receive Awake immediately.
        SelectSection(section);
        if (bookView != null) bookView.Open();
        if (!pauseController.TryOpenOverlay(bestiaryPanel, firstSelected))
        {
            if (bookView != null) bookView.HideImmediately();
            return;
        }
        previewCamera.enabled = true;
        closing = false;
        deckVisited = section == BookSection.Deck;
        nextToggleTime = Time.unscaledTime + toggleCooldown;
        if (narration != null) narration.Speak(section == BookSection.Deck ? "Deck book opened. Collection on the left. Current deck on the right." : "Bestiary opened. Press Escape to close.");
    }

    public void Close()
    {
        if (!IsOpen || closing) return;
        if (deckVisited && cards != null && cards.CurrentMode != CardSystem.Mode.Combat && cards.deck.Count < cards.minDeckRequired)
        {
            if (narration != null) narration.Speak("Your deck needs at least " + cards.minDeckRequired + " cards before closing. Current deck: " + cards.deck.Count + ".");
            if (Section != BookSection.Deck) SwitchSection(BookSection.Deck);
            return;
        }
        closing = true;
        nextToggleTime = Time.unscaledTime + toggleCooldown;
        if (bookView != null) bookView.Close(FinishClose);
        else FinishClose();
    }

    void FinishClose()
    {
        pauseController.CloseOverlay(bestiaryPanel);
        if (previewCamera != null) previewCamera.enabled = false;
        closing = false;
        deckVisited = false;
        if (cards != null) cards.SetBookDeckOpen(false);
        nextToggleTime = Time.unscaledTime + toggleCooldown;
        if (narration != null) narration.Speak("Book closed.");
    }

    static bool IsEditingText()
    {
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        var field = selected != null ? selected.GetComponent<TMP_InputField>() : null;
        return field != null && field.isFocused;
    }

    void OnDisable()
    {
        closing = false;
        if (bookView != null) bookView.HideImmediately();
        if (pauseController != null) pauseController.CloseOverlay(bestiaryPanel);
        if (previewCamera != null && previewCamera != Camera.main) previewCamera.enabled = false;
        if (ownsActionEnable && toggleAction != null) toggleAction.Disable();
        if (ownsDeckActionEnable && deckAction != null) deckAction.Disable();
        if (cards != null) cards.SetBookDeckOpen(false);
        ownsActionEnable = false;
        ownsDeckActionEnable = false;
    }

    void OnDestroy()
    {
        if (bestiaryTab != null) bestiaryTab.onClick.RemoveListener(OpenBestiary);
        if (deckTab != null) deckTab.onClick.RemoveListener(OpenDeck);
    }
}
