using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Attach to the Scroll View. Mouse scrolling stays free until focus changes.
[RequireComponent(typeof(ScrollRect))]
public class ScrollToSelection : MonoBehaviour
{
    [SerializeField, Min(0f)] float padding = 12f;
    ScrollRect scroll;
    GameObject lastSelection;
    Vector2 lastViewportSize;
    Vector2 lastContentSize;

    void Awake() => scroll = GetComponent<ScrollRect>();

    void OnEnable()
    {
        lastSelection = null;
        lastViewportSize = lastContentSize = Vector2.zero;
    }

    void LateUpdate()
    {
        if (scroll.content == null || scroll.viewport == null || EventSystem.current == null)
            return;

        GameObject selected = EventSystem.current.currentSelectedGameObject;
        Vector2 viewportSize = scroll.viewport.rect.size;
        Vector2 contentSize = scroll.content.rect.size;
        bool changed = selected != lastSelection || viewportSize != lastViewportSize
            || contentSize != lastContentSize;
        lastSelection = selected;
        lastViewportSize = viewportSize;
        lastContentSize = contentSize;

        if (!changed || selected == null || !scroll.vertical) return;
        var target = selected.transform as RectTransform;
        if (target == null || target == scroll.content || !target.IsChildOf(scroll.content)) return;

        Canvas.ForceUpdateCanvases();
        Rect viewport = scroll.viewport.rect;
        Bounds contentBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
            scroll.viewport, scroll.content);
        if (contentBounds.size.y <= viewport.height) return;

        Bounds targetBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
            scroll.viewport, target);
        float margin = Mathf.Min(padding, Mathf.Max(0f, (viewport.height - targetBounds.size.y) * 0.5f));
        float delta = 0f;
        if (targetBounds.max.y > viewport.yMax - margin)
            delta = viewport.yMax - margin - targetBounds.max.y;
        else if (targetBounds.min.y < viewport.yMin + margin)
            delta = viewport.yMin + margin - targetBounds.min.y;

        // Clamp the shift so selecting the first/last row cannot overscroll.
        delta = Mathf.Clamp(delta, viewport.yMax - contentBounds.max.y,
            viewport.yMin - contentBounds.min.y);
        Vector3 worldShift = scroll.viewport.TransformVector(new Vector3(0f, delta, 0f));
        Vector3 localShift = scroll.content.parent.InverseTransformVector(worldShift);
        scroll.StopMovement();
        scroll.content.anchoredPosition += new Vector2(localShift.x, localShift.y);
    }
}
