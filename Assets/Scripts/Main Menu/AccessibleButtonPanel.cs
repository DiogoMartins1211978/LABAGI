using TMPro;
using UnityEngine;
using UnityEngine.Accessibility;
using UnityEngine.UI;

public class AccessibleButtonPanel : MonoBehaviour
{
    [SerializeField] Button[] buttons;

    AccessibilityHierarchy hierarchy;

    void OnEnable()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        // Narrator may not report its enabled status to Unity.
        // This exposes the UI; it does not turn Narrator on.
        AssistiveSupport.screenReaderStatusOverride =
            AssistiveSupport.ScreenReaderStatusOverride.ForceEnabled;
#endif

        hierarchy = new AccessibilityHierarchy();

        foreach (Button button in buttons)
        {
            if (button == null)
                continue;

            Button target = button;
            TMP_Text text = target.GetComponentInChildren<TMP_Text>(true);

            AccessibilityNode node = hierarchy.AddNode(
                text != null ? text.text : target.name
            );

            node.role = AccessibilityRole.Button;

            node.state = target.IsInteractable()
                ? AccessibilityState.None
                : AccessibilityState.Disabled;

            node.frameGetter = () => GetScreenRect(target);

            node.focusChanged += (_, focused) =>
            {
                if (focused && target.IsActive())
                    target.Select();
            };

            node.invoked += () =>
            {
                if (!target.IsActive() || !target.IsInteractable())
                    return false;

                target.onClick.Invoke();
                return true;
            };
        }

        AssistiveSupport.screenReaderStatusChanged += OnReaderChanged;
        AssistiveSupport.activeHierarchy = hierarchy;
    }

    void OnReaderChanged(bool enabled)
    {
        if (enabled)
            AssistiveSupport.activeHierarchy = hierarchy;
    }

    void OnDisable()
    {
        AssistiveSupport.screenReaderStatusChanged -= OnReaderChanged;

        if (AssistiveSupport.activeHierarchy == hierarchy)
            AssistiveSupport.activeHierarchy = null;

        hierarchy = null;
    }

    Rect GetScreenRect(Button button)
    {
        RectTransform rect = button.GetComponent<RectTransform>();
        Canvas canvas = button.GetComponentInParent<Canvas>();

        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : canvas.worldCamera;

        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);

        Vector2 bottomLeft =
            RectTransformUtility.WorldToScreenPoint(camera, corners[0]);

        Vector2 topRight =
            RectTransformUtility.WorldToScreenPoint(camera, corners[2]);

        return new Rect(
            bottomLeft.x,
            Screen.height - topRight.y,
            topRight.x - bottomLeft.x,
            topRight.y - bottomLeft.y
        );
    }
}