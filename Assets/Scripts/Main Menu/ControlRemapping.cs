using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Attach to the always-active MenuManager. Connect the existing UI in the Inspector.
public class ControlRemapping : MonoBehaviour
{
    [Serializable]
    public class BindingRow
    {
        public string displayName;
        public string actionPath;
        public string originalBindingPath;
        public Button button;
        [NonSerialized] public InputAction action;
        [NonSerialized] public int bindingIndex;
        [NonSerialized] public TMP_Text label;
        [NonSerialized] public UnityAction click;

        public BindingRow(string name, string action, string path)
        {
            displayName = name;
            actionPath = action;
            originalBindingPath = path;
        }
    }

    [SerializeField] BindingRow[] bindings =
    {
        new BindingRow("Forward", "Player/Move", "<Keyboard>/upArrow"),
        new BindingRow("Backward", "Player/Move", "<Keyboard>/downArrow"),
        new BindingRow("Left", "Player/Move", "<Keyboard>/leftArrow"),
        new BindingRow("Right", "Player/Move", "<Keyboard>/rightArrow")
    };
    [SerializeField] Button restoreDefaultsButton;
    [SerializeField] TMP_Text statusLabel;
    [SerializeField] MenuNarration narration;

    const string SaveKey = "Controls.BindingOverrides";
    public static bool IsCapturing { get; private set; }
    static int blockBackThroughFrame = -1;
    public static bool BlocksMenuBack => IsCapturing || Time.frameCount <= blockBackThroughFrame;

    BindingRow capturing;
    BaseInputModule inputModule;
    bool moduleWasEnabled;
    bool actionWasEnabled;
    bool waitingForRelease;
    bool restoring;
    GameObject lastNarratedSelection;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession()
    {
        IsCapturing = false;
        blockBackThroughFrame = -1;
    }

    // Also applies saved bindings when starting directly in the gameplay scene.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void LoadSavedBindings()
    {
        var actions = InputSystem.actions;
        if (actions == null || !PlayerPrefs.HasKey(SaveKey)) return;
        try
        {
            actions.LoadBindingOverridesFromJson(PlayerPrefs.GetString(SaveKey));
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Could not load saved controls: " + exception.Message);
        }
    }

    void Start()
    {
        foreach (BindingRow row in bindings)
        {
            row.action = InputSystem.actions?.FindAction(row.actionPath);
            row.bindingIndex = -1;
            if (row.action != null)
                for (int i = 0; i < row.action.bindings.Count; i++)
                    if (row.action.bindings[i].path == row.originalBindingPath)
                    {
                        row.bindingIndex = i;
                        break;
                    }

            if (row.button == null || row.bindingIndex < 0)
            {
                Debug.LogError("Control remapping: check the button and binding for " + row.displayName, this);
                continue;
            }

            row.label = row.button.GetComponentInChildren<TMP_Text>(true);
            row.click = () => BeginCapture(row);
            row.button.onClick.AddListener(row.click);

        }
        if (restoreDefaultsButton != null)
            restoreDefaultsButton.onClick.AddListener(RestoreDefaults);
        RefreshLabels();
    }

    void BeginCapture(BindingRow row)
    {
        if (IsCapturing) return;
        if (Keyboard.current == null)
        {
            SetStatus("Connect a keyboard to change this binding.");
            return;
        }

        capturing = row;
        IsCapturing = true;
        waitingForRelease = true;
        actionWasEnabled = row.action.enabled;
        row.action.Disable();

        inputModule = EventSystem.current != null ? EventSystem.current.currentInputModule : null;
        moduleWasEnabled = inputModule != null && inputModule.enabled;
        if (inputModule != null) inputModule.enabled = false;

        if (row.label != null) row.label.text = row.displayName + ": Press a key...";
        SetStatus("Choose a key for " + row.displayName + ". Escape cancels.");
    }

    void LateUpdate()
    {
        // Observe selection without adding EventTrigger, which also intercepts
        // scroll/drag events and prevents them from reaching the ScrollRect.
        GameObject selected = EventSystem.current != null
            ? EventSystem.current.currentSelectedGameObject : null;
        if (selected == lastNarratedSelection) return;
        lastNarratedSelection = selected;
        if (selected == null || IsCapturing) return;

        foreach (var row in bindings)
            if (row.button != null && row.button.gameObject == selected && row.bindingIndex >= 0)
            {
                Say(CurrentLabel(row));
                break;
            }
    }

    void Update()
    {
        if (capturing == null || restoring) return;
        if (!capturing.button.gameObject.activeInHierarchy)
        {
            FinishCapture("Binding change cancelled.");
            return;
        }
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            FinishCapture("Keyboard disconnected. Binding unchanged.");
            return;
        }

        // Do not capture the Enter/Space press that opened the prompt.
        if (waitingForRelease)
        {
            foreach (var key in keyboard.allKeys)
                if (key.isPressed) return;
            waitingForRelease = false;
            return;
        }

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            FinishCapture("Binding change cancelled.");
            return;
        }

        foreach (var key in keyboard.allKeys)
        {
            if (!key.wasPressedThisFrame) continue;
            string path = "<Keyboard>/" + key.name;
            string previous = capturing.action.bindings[capturing.bindingIndex].effectivePath;
            if (!string.Equals(path, previous, StringComparison.OrdinalIgnoreCase) && IsUsed(path))
            {
                SetStatus(key.displayName + " is already assigned. Choose another key, or Escape to cancel.");
                return;
            }

            capturing.action.ApplyBindingOverride(capturing.bindingIndex, path);
            SaveBindings();
            FinishCapture(CurrentLabel(capturing));
            return;
        }
    }

    bool IsUsed(string path)
    {
        // Check the Player map only; menu navigation is kept separate.
        foreach (var action in capturing.action.actionMap.actions)
            foreach (var binding in action.bindings)
                if (!binding.isComposite && string.Equals(binding.effectivePath, path, StringComparison.OrdinalIgnoreCase))
                    return true;
        return false;
    }

    void FinishCapture(string message)
    {
        restoring = true;
        RefreshLabels();
        SetStatus(message);
        StartCoroutine(RestoreInputNextFrame());
    }

    IEnumerator RestoreInputNextFrame()
    {
        // The captured press must not also activate a menu control.
        yield return null;
        ReleaseCapture();
    }

    void ReleaseCapture()
    {
        if (capturing != null && actionWasEnabled) capturing.action.Enable();
        if (inputModule != null && moduleWasEnabled) inputModule.enabled = true;
        Button button = capturing != null ? capturing.button : null;
        capturing = null;
        restoring = false;
        IsCapturing = false;
        blockBackThroughFrame = Time.frameCount + 1;
        if (button != null && button.IsActive() && button.IsInteractable()) button.Select();
    }

    public void RestoreDefaults()
    {
        if (IsCapturing) return;
        foreach (var row in bindings)
            if (row.action != null && row.bindingIndex >= 0)
                row.action.RemoveBindingOverride(row.bindingIndex);
        SaveBindings();
        RefreshLabels();
        SetStatus("Displayed controls restored to defaults.");
    }

    void SaveBindings()
    {
        PlayerPrefs.SetString(SaveKey, InputSystem.actions.SaveBindingOverridesAsJson());
        PlayerPrefs.Save();
    }

    string CurrentLabel(BindingRow row) => row.displayName + ": "
        + row.action.GetBindingDisplayString(row.bindingIndex);

    void RefreshLabels()
    {
        foreach (var row in bindings)
            if (row.label != null && row.action != null && row.bindingIndex >= 0)
                row.label.text = CurrentLabel(row);
    }

    void SetStatus(string message)
    {
        if (statusLabel != null) statusLabel.text = message;
        Say(message);
    }

    void Say(string message)
    {
        if (narration != null) narration.Speak(message);
    }

    void OnDisable()
    {
        StopAllCoroutines();
        if (capturing != null) ReleaseCapture();
        RefreshLabels();
    }

    void OnDestroy()
    {
        foreach (var row in bindings)
        {
            if (row.button != null && row.click != null) row.button.onClick.RemoveListener(row.click);
        }
        if (restoreDefaultsButton != null)
            restoreDefaultsButton.onClick.RemoveListener(RestoreDefaults);
    }
}
