using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Keep this component on an always-active object, not on PausePanel.
[DefaultExecutionOrder(-10000)]
public class PauseMenuController : MonoBehaviour
{
    [SerializeField] GameObject pausePanel;
    [SerializeField] Button resumeButton;
    [SerializeField] string mainMenuScene = "Main Menu";

    public static bool IsPaused { get; private set; }
    public static bool OpenedThisFrame => IsPaused && Time.frameCount == openedFrame;
    public static bool BlocksGameplayInput => IsPaused || waitingForRelease
        || Time.frameCount <= blockThroughFrame;

    static bool waitingForRelease;
    static int openedFrame = -1;
    static int blockThroughFrame = -1;

    bool ownsPause;
    bool changingScene;
    float previousTimeScale;
    CursorLockMode previousCursorLock;
    bool previousCursorVisible;
    GameObject previousSelection;
    GameObject activeOverlay;
    bool selectOnOpen = true;

    public bool IsShowingOverlay(GameObject overlay) => ownsPause && overlay != null && activeOverlay == overlay;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession()
    {
        IsPaused = waitingForRelease = false;
        openedFrame = blockThroughFrame = -1;
    }

    public void ConfigurePanel(GameObject panel, Button resume, bool focusFirstButton = true)
    {
        pausePanel = panel; resumeButton = resume;
        selectOnOpen = focusFirstButton;
    }

    void Start()
    {
        if (pausePanel == null || resumeButton == null)
        {
            Debug.LogError("Pause Menu Controller: assign Pause Panel and Resume Button.", this);
            enabled = false;
            return;
        }
        pausePanel.SetActive(false);
    }

    void Update()
    {
        if (changingScene) return;
        if (waitingForRelease && !AnyGameplayControlHeld())
        {
            waitingForRelease = false;
            blockThroughFrame = Time.frameCount;
        }

        if (ControlRemapping.BlocksMenuBack) return;
        bool escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        bool start = Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;
        bool back = Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;

        if (!IsPaused)
        {
            // Do not reopen while the Resume press is still being released.
            if (!BlocksGameplayInput && (escape || start)) Pause();
        }
        else if (ownsPause && activeOverlay == pausePanel && pausePanel.activeInHierarchy && (escape || start || back))
        {
            // Submenus handle Back themselves while PausePanel is hidden.
            Resume();
        }
    }

    public void Pause()
    {
        if (IsPaused || changingScene || pausePanel == null || resumeButton == null) return;
        TryOpenOverlay(pausePanel, selectOnOpen ? resumeButton : null);
    }

    // A book or other gameplay overlay can borrow the same pause state without
    // showing PausePanel or changing combat/deckbuilding modes.
    public bool TryOpenOverlay(GameObject overlay, Selectable firstSelected = null)
    {
        if (!isActiveAndEnabled || BlocksGameplayInput || changingScene || overlay == null) return false;
        if (transform.IsChildOf(overlay.transform))
        {
            Debug.LogError("The pause controller must remain outside the overlay it controls.", this);
            return false;
        }
        // The deckbuilder hides its entire Canvas outside deckbuilding mode.
        // A pause panel under that Canvas would stop the game without appearing.
        Canvas canvas = overlay.GetComponentInParent<Canvas>(true);
        Transform parent = overlay.transform.parent;
        if (canvas == null || !canvas.isActiveAndEnabled ||
            (parent != null && !parent.gameObject.activeInHierarchy))
        {
            Debug.LogError("The overlay must be under an enabled, always-active Canvas. "
                + "Hide its child panel, not the Canvas, and keep it outside the deckbuilder UI.", this);
            return false;
        }
        previousTimeScale = Time.timeScale;
        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        previousSelection = EventSystem.current != null
            ? EventSystem.current.currentSelectedGameObject : null;

        ownsPause = IsPaused = true;
        activeOverlay = overlay;
        openedFrame = Time.frameCount;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        overlay.transform.SetAsLastSibling();
        overlay.SetActive(true);
        StartCoroutine(SelectOverlayNextFrame(overlay, firstSelected));
        return true;
    }

    IEnumerator SelectOverlayNextFrame(GameObject overlay, Selectable firstSelected)
    {
        // yield null still progresses while timeScale is zero.
        yield return null;
        if (!IsShowingOverlay(overlay) || !overlay.activeInHierarchy || EventSystem.current == null) yield break;
        EventSystem.current.SetSelectedGameObject(null);
        if (firstSelected != null && firstSelected.IsActive() && firstSelected.IsInteractable())
            firstSelected.Select();
    }

    public void CloseOverlay(GameObject overlay)
    {
        CloseOverlay(overlay, true);
    }

    public void CloseOverlay(GameObject overlay, bool playBackSound)
    {
        // Only the owner can release this pause. Closing the book must never
        // accidentally resume a pause/options menu that is open instead.
        if (IsShowingOverlay(overlay)) Resume(playBackSound);
    }

    public void Resume()
    {
        Resume(true);
    }

    void Resume(bool playBackSound)
    {
        if (!ownsPause || changingScene) return;
        if (playBackSound) MenuSfx.Back();
        StopAllCoroutines();
        // Keep gameplay blocked until the UI's activating press is released.
        waitingForRelease = true;
        blockThroughFrame = Time.frameCount + 1;
        RestoreState();
        HideActiveOverlay();
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
            if (previousSelection != null && previousSelection.activeInHierarchy)
                EventSystem.current.SetSelectedGameObject(previousSelection);
        }
    }

    // Wire this only when the Main Menu button/confirmation flow is ready.
    public void ReturnToMainMenu()
    {
        if (changingScene) return;
        if (!Application.CanStreamedLevelBeLoaded(mainMenuScene))
        {
            Debug.LogError("Main Menu scene is missing from the build scene list.", this);
            return;
        }

        changingScene = true;
        StopAllCoroutines();
        RestoreState();
        HideActiveOverlay();
        waitingForRelease = false;
        blockThroughFrame = -1;
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SceneManager.LoadSceneAsync(mainMenuScene);
    }

    void RestoreState()
    {
        if (!ownsPause) return;
        Time.timeScale = previousTimeScale;
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;
        ownsPause = IsPaused = false;
    }

    void HideActiveOverlay()
    {
        GameObject overlay = activeOverlay;
        activeOverlay = null;
        if (overlay != null) overlay.SetActive(false);
    }

    static bool AnyGameplayControlHeld()
    {
        foreach (var device in InputSystem.devices)
            if (device is UnityEngine.InputSystem.XR.XRController)
                foreach (var control in device.allControls)
                {
                    if (control is ButtonControl xrButton && xrButton.isPressed
                        && (control.name == "triggerPressed" || control.name == "gripPressed"
                            || control.name == "primaryButton" || control.name == "secondaryButton"
                            || control.name == "menu" || control.name == "menuButton"
                            || control.name == "primary2DAxisClick" || control.name == "secondary2DAxisClick")) return true;
                    if (control is Vector2Control axis && axis.ReadValue().sqrMagnitude > 0.04f) return true;
                }
        if (Keyboard.current != null)
            foreach (var key in Keyboard.current.allKeys)
                if (key.isPressed) return true;
        if (Mouse.current != null && (Mouse.current.leftButton.isPressed
            || Mouse.current.rightButton.isPressed || Mouse.current.middleButton.isPressed)) return true;
        if (Gamepad.current != null)
        {
            foreach (var control in Gamepad.current.allControls)
                if (control is ButtonControl button && button.isPressed) return true;
            if (Gamepad.current.leftStick.ReadValue().sqrMagnitude > 0.04f
                || Gamepad.current.rightStick.ReadValue().sqrMagnitude > 0.04f) return true;
        }
        return false;
    }

    void OnDisable()
    {
        StopAllCoroutines();
        if (!ownsPause) return;
        RestoreState();
        waitingForRelease = false;
        blockThroughFrame = Time.frameCount;
        HideActiveOverlay();
    }
}
