using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Keep decorative arrows outside the text's clickable rectangle at every text size.
[ExecuteAlways, RequireComponent(typeof(RectTransform))]
public class BookTextLink : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    [SerializeField] TMP_Text label;
    [SerializeField] RectTransform leftArrow, rightArrow;
    [SerializeField] float arrowSize = 18, gap = 12;
    float previousFontSize;
    string previousText;
    bool hovered, focused, previousInteractable;
    Button button;

    void OnEnable() { button = GetComponent<Button>(); UpdateEmphasis(); }
    void OnDisable() { hovered = focused = false; }
    void OnRectTransformDimensionsChange() => Apply();
    void LateUpdate()
    {
        if (button != null && button.IsInteractable() != previousInteractable) UpdateEmphasis();
        if (label != null && (label.fontSize != previousFontSize || label.text != previousText)) Apply();
    }
    public void OnPointerEnter(PointerEventData data) { hovered = true; UpdateEmphasis(); }
    public void OnPointerExit(PointerEventData data) { hovered = false; UpdateEmphasis(); }
    public void OnSelect(BaseEventData data) { focused = true; UpdateEmphasis(); }
    public void OnDeselect(BaseEventData data) { focused = false; UpdateEmphasis(); }
    void UpdateEmphasis()
    {
        if (label == null) return;
        previousInteractable = button == null || button.IsInteractable();
        bool emphasized = previousInteractable && (hovered || focused);
        label.fontStyle = FontStyles.Underline;
        var ink = !previousInteractable ? new Color(.4f, .4f, .4f, .65f)
            : emphasized ? new Color(.02f, .02f, .02f) : new Color(.2f, .2f, .2f);
        label.color = ink;
        foreach (var arrow in new[] { leftArrow, rightArrow })
        {
            if (arrow == null) continue;
            if (arrow.TryGetComponent<Graphic>(out var graphic)) graphic.color = ink;
            if (arrow.TryGetComponent<BookInkGraphic>(out var strokes)) strokes.SetEmphasis(emphasized);
        }
    }
    public void Apply()
    {
        if (label == null || leftArrow == null || rightArrow == null) return;
        var bounds = ((RectTransform)transform).rect;
        float width = Mathf.Max(1, Mathf.Min(label.preferredWidth + 8, bounds.width - 2 * (gap + arrowSize)));
        var rect = label.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(width, bounds.height);
        foreach (var arrow in new[] { leftArrow, rightArrow })
        {
            arrow.anchorMin = arrow.anchorMax = arrow.pivot = new Vector2(.5f, .5f);
            arrow.sizeDelta = Vector2.one * arrowSize;
            arrow.anchoredPosition = Vector2.right * (arrow == leftArrow ? -1 : 1) * (width / 2 + gap + arrowSize / 2);
        }
        previousFontSize = label.fontSize;
        previousText = label.text;
    }
}
