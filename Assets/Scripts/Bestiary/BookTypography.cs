using TMPro;
using UnityEngine;

// Shared editor-authored baselines prevent text-size multipliers being applied
// twice when switching between book sections.
public class BookTypography : MonoBehaviour
{
    [SerializeField] TMP_Text[] elements;
    [SerializeField] float[] baseSizes;
    public void Apply()
    {
        int setting = Mathf.Clamp(PlayerPrefs.GetInt("Accessibility.TextSize", 0), 0, 2);
        float scale = setting == 1 ? 1.2f : setting == 2 ? 1.4f : 1;
        for (int i = 0; i < elements.Length && i < baseSizes.Length; i++)
            if (elements[i] != null) { elements[i].enableAutoSizing = false; elements[i].fontSize = baseSizes[i] * scale; }
    }
}
