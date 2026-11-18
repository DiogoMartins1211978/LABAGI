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

    InputAction toggleAction;
    bool ownsActionEnable;

    public bool IsOpen => pauseController != null && pauseController.IsShowingOverlay(bestiaryPanel);

    void Awake()
    {
        toggleAction = InputSystem.actions?.FindAction("Player/ToggleBestiary");
    }

    void OnEnable()
    {
        if (toggleAction != null && !toggleAction.enabled)
        {
            toggleAction.Enable();
            ownsActionEnable = true;
        }
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
    }

    void Update()
    {
        if (ControlRemapping.BlocksMenuBack) return;
        bool togglePressed = toggleAction != null ? toggleAction.WasPressedThisFrame()
            : Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame;
        bool cancelPressed = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            || (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);

        if (IsOpen)
        {
            if (togglePressed || cancelPressed) Close();
        }
        else if (togglePressed && !IsEditingText()) Open();
    }

    void LateUpdate()
    {
        // Stop rendering the book when hidden, including an externally closed overlay.
        if (previewCamera != null && previewCamera != Camera.main)
            previewCamera.enabled = IsOpen;
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (!isActiveAndEnabled || pauseController == null || bestiaryPanel == null || previewCamera == null
            || PauseMenuController.BlocksGameplayInput || ControlRemapping.BlocksMenuBack) return;
        if (!pauseController.TryOpenOverlay(bestiaryPanel, firstSelected)) return;
        previewCamera.enabled = true;
        if (narration != null) narration.Speak("Bestiary opened. Press Escape to close.");
    }

    public void Close()
    {
        if (!IsOpen) return;
        pauseController.CloseOverlay(bestiaryPanel);
        if (previewCamera != null) previewCamera.enabled = false;
        if (narration != null) narration.Speak("Bestiary closed.");
    }

    static bool IsEditingText()
    {
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        var field = selected != null ? selected.GetComponent<TMP_InputField>() : null;
        return field != null && field.isFocused;
    }

    void OnDisable()
    {
        if (pauseController != null) pauseController.CloseOverlay(bestiaryPanel);
        if (previewCamera != null && previewCamera != Camera.main) previewCamera.enabled = false;
        if (ownsActionEnable && toggleAction != null) toggleAction.Disable();
        ownsActionEnable = false;
    }
}
