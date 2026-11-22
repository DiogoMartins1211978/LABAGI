using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class MenuPanelFocus : MonoBehaviour
{
    [SerializeField] Selectable firstSelected;
    [SerializeField] Button backButton;
    [Tooltip("Optional Tab/Shift+Tab order. Inactive and disabled controls are skipped.")]
    [SerializeField] Selectable[] tabOrder;

    Selectable lastSelected;
    TMP_InputField editingInputLastFrame;
    static int lastBackFrame = -1;
    static int lastSubmitFrame = -1;

    public Button BackControl => backButton;

    public void ConfigureNavigation(Selectable first, Selectable[] order, bool resetSelection = false)
    {
        firstSelected = first;
        tabOrder = order;
        if (resetSelection) lastSelected = first;
    }

    void OnEnable()
    {
        StartCoroutine(RestoreSelection());
    }

    IEnumerator RestoreSelection()
    {
        yield return null;

        if (EventSystem.current == null)
            yield break;

        Selectable target = CanSelect(lastSelected)
            ? lastSelected
            : firstSelected;

        if (CanSelect(target))
        {
            EventSystem.current.SetSelectedGameObject(null);
            target.Select();
        }
    }

    void Update()
    {
        RememberSelection();

        if (ControlRemapping.BlocksMenuBack || PauseMenuController.OpenedThisFrame)
            return;

        if (HandleTabNavigation())
            return;

        HandleAlternateSubmit(transform);

        bool backPressed =
            (Keyboard.current != null &&
             Keyboard.current.escapeKey.wasPressedThisFrame) ||
            (Gamepad.current != null &&
             Gamepad.current.buttonEast.wasPressedThisFrame);

        if (!backPressed ||
            lastBackFrame == Time.frameCount ||
            !CanSelect(backButton))
            return;

        // First Escape ends text editing; the next Escape returns to the parent menu.
        GameObject selected = EventSystem.current != null
            ? EventSystem.current.currentSelectedGameObject : null;
        var input = selected != null ? selected.GetComponent<TMP_InputField>() : null;
        if (input != null && (input.isFocused || input == editingInputLastFrame))
        {
            input.DeactivateInputField();
            lastBackFrame = Time.frameCount;
            return;
        }

        // Prevent one press from closing multiple panels.
        lastBackFrame = Time.frameCount;
        MenuSfx.Back();
        backButton.onClick.Invoke();
    }

    void LateUpdate()
    {
        var selected = EventSystem.current != null
            ? EventSystem.current.currentSelectedGameObject : null;
        var input = selected != null ? selected.GetComponent<TMP_InputField>() : null;
        // TMP can process Escape before this script's next Update.
        editingInputLastFrame = input != null && input.isFocused ? input : null;
    }

    // E submits menus without becoming a Submit binding that interrupts typing.
    public static void HandleAlternateSubmit(Transform panel)
    {
        var keyboard = Keyboard.current;
        var events = EventSystem.current;
        if (keyboard == null || events == null || !keyboard.eKey.wasPressedThisFrame
            || keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame
            || ControlRemapping.BlocksMenuBack || PauseMenuController.OpenedThisFrame
            || lastSubmitFrame == Time.frameCount) return;
        var selected = events.currentSelectedGameObject;
        if (selected == null || !selected.transform.IsChildOf(panel)
            || selected.GetComponent<TMP_InputField>() != null
            || selected.GetComponent<InputField>() != null) return;
        var control = selected.GetComponent<Selectable>();
        if (control == null || !control.IsActive() || !control.IsInteractable()) return;
        lastSubmitFrame = Time.frameCount;
        ExecuteEvents.Execute(selected, new BaseEventData(events), ExecuteEvents.submitHandler);
    }

    bool HandleTabNavigation()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.tabKey.wasPressedThisFrame ||
            EventSystem.current == null || tabOrder == null || tabOrder.Length == 0)
            return false;

        var selected = EventSystem.current.currentSelectedGameObject;
        int current = -1;
        for (int i = 0; i < tabOrder.Length; i++)
            if (tabOrder[i] != null && tabOrder[i].gameObject == selected) current = i;
        int direction = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed ? -1 : 1;
        if (current < 0) current = direction > 0 ? -1 : 0;

        for (int step = 1; step <= tabOrder.Length; step++)
        {
            int index = (current + direction * step + tabOrder.Length * 2) % tabOrder.Length;
            if (!CanSelect(tabOrder[index])) continue;
            var input = selected != null ? selected.GetComponent<TMP_InputField>() : null;
            if (input != null) input.DeactivateInputField();
            tabOrder[index].Select();
            return true;
        }
        return true;
    }

    void RememberSelection()
    {
        if (EventSystem.current == null)
            return;

        GameObject selected =
            EventSystem.current.currentSelectedGameObject;

        if (selected != null &&
            selected.transform.IsChildOf(transform))
        {
            Selectable control = selected.GetComponent<Selectable>();

            if (control != null)
                lastSelected = control;
        }
    }

    void OnDisable()
    {
        editingInputLastFrame = null;
        RememberSelection();
        StopAllCoroutines();
    }

    bool CanSelect(Selectable control)
    {
        return control != null &&
               control.IsActive() &&
               control.IsInteractable();
    }
}
