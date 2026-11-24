using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Tabs emerge from under the page only after the opening animation finishes.
public class BookBookmark : MonoBehaviour
{
    [SerializeField] Vector2 openPosition;
    [SerializeField] Vector2 hiddenOffset;
    [SerializeField] bool sectionTab;
    [SerializeField] Image background;
    [SerializeField] TMP_Text label;
    [SerializeField, Min(0)] float currentSectionOutset = 12;
    [SerializeField, Min(.01f)] float sectionTransitionDuration = .22f;
    float revealAmount;
    float sectionAmount, transitionFrom, transitionStart;
    bool transitioning;
    public bool IsCurrentSection { get; private set; }

    public void SetCurrentSection(bool current)
    {
        current = sectionTab && current;
        // if (TryGetComponent<MenuSfxControl>(out var sound)) sound.AllowFocusWhenSilent = !current; // Disallow sound when the bookmark is of the current active section
        if (IsCurrentSection == current)
        {
            ApplyAppearance();
            return;
        }
        IsCurrentSection = current;
        transitionFrom = sectionAmount;
        transitionStart = Time.unscaledTime;
        transitioning = Application.isPlaying && isActiveAndEnabled && revealAmount >= .99f;
        if (!transitioning) sectionAmount = current ? 1 : 0;
        ApplyAppearance();
    }

    void LateUpdate()
    {
        if (!transitioning) return;
        float t = Mathf.Clamp01((Time.unscaledTime - transitionStart) / sectionTransitionDuration);
        sectionAmount = Mathf.Lerp(transitionFrom, IsCurrentSection ? 1 : 0, Mathf.SmoothStep(0, 1, t));
        transitioning = t < 1;
        ApplyAppearance();
    }

    void ApplyAppearance()
    {
        if (background != null) background.color = Color.Lerp(new Color(.23f, .29f, .35f), new Color(.08f, .09f, .1f), sectionAmount);
        if (label != null)
        {
            label.color = Color.Lerp(Color.white, new Color(.58f, .6f, .62f), sectionAmount);
            label.fontStyle = FontStyles.Normal;
        }
        Reveal(revealAmount);
    }

    public void Reveal(float amount)
    {
        revealAmount = amount;
        float progress = Mathf.SmoothStep(0, 1, amount);
        ((RectTransform)transform).anchoredPosition = openPosition + hiddenOffset * (1 - progress)
            + Vector2.right * (sectionAmount * currentSectionOutset * progress);
    }
}
