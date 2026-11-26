using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Joysticks supplement the rays. A/X confirms and B/Y goes back, leaving
// triggers available for pointer clicks without submitting a second time.
public sealed class VRMenuNavigation : MonoBehaviour
{
    InputAction move, submit, cancel;
    Vector2 previous;
    float nextRepeat;
    public bool UsingJoystick { get; private set; }

    void Awake()
    {
        move = new InputAction("VR Menu Move", InputActionType.Value, expectedControlType: "Vector2");
        move.AddBinding("<XRController>{LeftHand}/primary2DAxis");
        move.AddBinding("<XRController>{RightHand}/primary2DAxis");
        submit = new InputAction("VR Menu Confirm", InputActionType.Button);
        submit.AddBinding("<XRController>{LeftHand}/primaryButton");
        submit.AddBinding("<XRController>{RightHand}/primaryButton");
        cancel = new InputAction("VR Menu Back", InputActionType.Button);
        cancel.AddBinding("<XRController>{LeftHand}/secondaryButton");
        cancel.AddBinding("<XRController>{RightHand}/secondaryButton");
        move.Enable(); submit.Enable(); cancel.Enable();
    }

    public void UsePointer() => UsingJoystick = false;

    void Update()
    {
        var events = EventSystem.current;
        if (events == null) return;
        Vector2 axis = move.ReadValue<Vector2>();
        Vector2 direction = axis.sqrMagnitude < 0.36f ? Vector2.zero
            : Mathf.Abs(axis.x) > Mathf.Abs(axis.y)
                ? new Vector2(Mathf.Sign(axis.x), 0) : new Vector2(0, Mathf.Sign(axis.y));
        if (direction != Vector2.zero && (direction != previous || Time.unscaledTime >= nextRepeat))
        {
            UsingJoystick = true;
            nextRepeat = Time.unscaledTime + (direction == previous ? 0.16f : 0.4f);
            var selected = events.currentSelectedGameObject;
            if (selected == null || !selected.activeInHierarchy) SelectFirst(events);
            else
            {
                var data = new AxisEventData(events) { moveVector = direction,
                    moveDir = direction.x > 0 ? UnityEngine.EventSystems.MoveDirection.Right : direction.x < 0 ? UnityEngine.EventSystems.MoveDirection.Left
                        : direction.y > 0 ? UnityEngine.EventSystems.MoveDirection.Up : UnityEngine.EventSystems.MoveDirection.Down };
                ExecuteEvents.Execute(selected, data, ExecuteEvents.moveHandler);
            }
        }
        previous = direction;
        if (submit.WasPressedThisFrame())
        {
            UsingJoystick = true;
            if (events.currentSelectedGameObject == null) SelectFirst(events);
            var selected = events.currentSelectedGameObject;
            if (selected != null) ExecuteEvents.Execute(selected, new BaseEventData(events), ExecuteEvents.submitHandler);
        }
        if (cancel.WasPressedThisFrame())
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var focus in root.GetComponentsInChildren<MenuPanelFocus>(true))
                    if (focus.gameObject.activeInHierarchy && focus.BackControl != null
                        && focus.BackControl.IsActive() && focus.BackControl.IsInteractable())
                    {
                        MenuSfx.Back();
                        focus.BackControl.onClick.Invoke();
                        return;
                    }
    }

    void SelectFirst(EventSystem events)
    {
        foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var focus in root.GetComponentsInChildren<MenuPanelFocus>(true))
                if (focus.gameObject.activeInHierarchy && focus.InitialControl != null
                    && focus.InitialControl.IsActive() && focus.InitialControl.IsInteractable())
                {
                    events.SetSelectedGameObject(focus.InitialControl.gameObject);
                    return;
                }
        foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var control in root.GetComponentsInChildren<Selectable>())
                if (control.IsActive() && control.IsInteractable())
                {
                    events.SetSelectedGameObject(control.gameObject);
                    return;
                }
    }

    void OnDestroy()
    {
        move?.Dispose(); submit?.Dispose(); cancel?.Dispose();
    }
}
