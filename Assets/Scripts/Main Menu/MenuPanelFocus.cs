using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class MenuPanelFocus : MonoBehaviour
{
    [SerializeField] Selectable firstSelected;
    [SerializeField] Button backButton;

    Selectable lastSelected;
    static int lastBackFrame = -1;

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

        bool backPressed =
            (Keyboard.current != null &&
             Keyboard.current.escapeKey.wasPressedThisFrame) ||
            (Gamepad.current != null &&
             Gamepad.current.buttonEast.wasPressedThisFrame);

        if (!backPressed ||
            lastBackFrame == Time.frameCount ||
            !CanSelect(backButton))
            return;

        // Prevent one press from closing multiple panels.
        lastBackFrame = Time.frameCount;
        backButton.onClick.Invoke();
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