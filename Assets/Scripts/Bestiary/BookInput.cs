using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Navigation and primary Submit belong to the EventSystem. The game's
// remappable Interact action also activates the currently focused book item.
public static class BookInput
{
    static int submittedFrame = -1;
    public static void HandleInteract(Transform page)
    {
        var events = EventSystem.current;
        var interact = InputSystem.actions?.FindAction("Player/Interact");
        if (events == null || interact == null || !interact.WasPressedThisFrame()
            || submittedFrame == Time.frameCount || ControlRemapping.BlocksMenuBack
            || PauseMenuController.OpenedThisFrame) return;
        if (events.currentInputModule is InputSystemUIInputModule module
            && module.submit.action != null && module.submit.action.WasPressedThisFrame()) return;
        var selected = events.currentSelectedGameObject;
        if (selected == null || !selected.transform.IsChildOf(page)) return;
        var button = selected.GetComponent<Button>();
        if (button == null || !button.IsActive() || !button.IsInteractable()) return;
        submittedFrame = Time.frameCount;
        ExecuteEvents.Execute(selected, new BaseEventData(events), ExecuteEvents.submitHandler);
    }
}
