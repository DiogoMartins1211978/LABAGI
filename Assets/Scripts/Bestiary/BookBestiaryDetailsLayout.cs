using UnityEngine;
using TMPro;

[ExecuteAlways, RequireComponent(typeof(RectTransform))]
public class BookBestiaryDetailsLayout : MonoBehaviour
{
    [SerializeField] RectTransform portrait, description, drops, carousel, inspection;
    string previousDescription;
    float previousFontSize;
    void OnEnable() => Apply();
    void OnRectTransformDimensionsChange() => Apply();
    void LateUpdate()
    {
        var text = description != null ? description.GetComponent<TMP_Text>() : null;
        if (text != null && (text.text != previousDescription || text.fontSize != previousFontSize)) Apply();
    }
    void Apply()
    {
        if (portrait == null || description == null || drops == null || carousel == null) return;
        var size = ((RectTransform)transform).rect.size;
        void Bottom(RectTransform item, float y, float height)
        {
            item.anchorMin = item.anchorMax = new Vector2(.5f, 0);
            item.pivot = new Vector2(.5f, 0);
            item.anchoredPosition = new Vector2(0, y);
            item.sizeDelta = new Vector2(size.x, height);
        }
        Bottom(carousel, 0, 126);
        Bottom(drops, 134, 32);
        float portraitBottom = 238;
        if (inspection != null)
        {
            inspection.pivot = new Vector2(.5f, 0.5f);
            inspection.anchorMin = inspection.anchorMax = new Vector2(.5f, 0);
            inspection.anchoredPosition = new Vector2(0, 206);
            inspection.sizeDelta = new Vector2(Mathf.Min(320, size.x), 44);
            inspection.localRotation = Quaternion.identity;
            var text = description.GetComponent<TMP_Text>();
            float descriptionHeight = text != null && !string.IsNullOrWhiteSpace(text.text)
                ? Mathf.Min(64, text.GetPreferredValues(text.text, size.x, Mathf.Infinity).y) : 0;
            Bottom(description, 252, descriptionHeight);
            portraitBottom = 252 + descriptionHeight + (descriptionHeight > 0 ? 12 : 0);
            previousDescription = text != null ? text.text : null;
            previousFontSize = text != null ? text.fontSize : 0;
        }
        else Bottom(description, 178, 48);
        portrait.anchorMin = portrait.anchorMax = new Vector2(.5f, 1);
        portrait.pivot = new Vector2(.5f, 1);
        portrait.anchoredPosition = Vector2.zero;
        portrait.sizeDelta = new Vector2(size.x * .96f, Mathf.Max(80, size.y - portraitBottom));
    }
}
