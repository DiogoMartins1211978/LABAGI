using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class VRMenuPointerFocus : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public VRMenuNavigation Navigation { get; set; }

    public void OnPointerEnter(PointerEventData data)
    {
        var control = GetComponent<Selectable>();
        if (!control.IsActive() || !control.IsInteractable()) return;
        // Text editing should still require an intentional click.
        if (control is TMPro.TMP_InputField || control is InputField) return;
        Navigation?.UsePointer();
        EventSystem.current?.SetSelectedGameObject(gameObject);
    }

    public void OnPointerExit(PointerEventData data)
    {
        var events = EventSystem.current;
        if (events != null && events.currentSelectedGameObject == gameObject
            && (Navigation == null || !Navigation.UsingJoystick))
            events.SetSelectedGameObject(null);
    }
}
