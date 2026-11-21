using TMPro;
using UnityEngine;

// Shared page contract: content never occupies the title or the folio area.
[ExecuteAlways, RequireComponent(typeof(RectTransform))]
public class BookPageLayout : MonoBehaviour
{
    [SerializeField] RectTransform header, content, footer;
    [SerializeField] TMP_Text title;
    [SerializeField] float margin = 20, headerHeight = 72, footerHeight = 56, gap = 12;
    bool applying;
    Vector2 previousSize;
    float previousFontSize;
    string previousTitle;

    void OnEnable() => Apply();
    void OnRectTransformDimensionsChange() => Apply();
    void LateUpdate()
    {
        var size = ((RectTransform)transform).rect.size;
        if (size != previousSize || (title != null && (title.fontSize != previousFontSize || title.text != previousTitle))) Apply();
    }

    public void Apply()
    {
        if (applying || header == null || content == null) return;
        applying = true;
        var size = ((RectTransform)transform).rect.size;
        float width = Mathf.Max(1, size.x - margin * 2);
        float reserve = title != null ? Mathf.Max(headerHeight, title.GetPreferredValues(title.text, width, Mathf.Infinity).y + 8) : headerHeight;
        header.anchorMin = header.anchorMax = new Vector2(.5f, 1);
        header.pivot = new Vector2(.5f, 1);
        header.anchoredPosition = new Vector2(0, -margin);
        header.sizeDelta = new Vector2(width, reserve);
        content.anchorMin = Vector2.zero;
        content.anchorMax = Vector2.one;
        content.offsetMin = new Vector2(margin, margin + (footer != null ? footerHeight + gap : 0));
        content.offsetMax = new Vector2(-margin, -margin - reserve - gap);
        if (footer != null)
        {
            footer.anchorMin = footer.anchorMax = new Vector2(.5f, 0);
            footer.pivot = new Vector2(.5f, 0);
            footer.anchoredPosition = new Vector2(0, margin);
            footer.sizeDelta = new Vector2(width, footerHeight);
        }
        previousSize = size;
        previousFontSize = title != null ? title.fontSize : 0;
        previousTitle = title != null ? title.text : null;
        applying = false;
    }
}
