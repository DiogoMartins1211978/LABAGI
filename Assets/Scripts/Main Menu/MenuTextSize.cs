using TMPro;
using UnityEngine;

public class MenuTextSize : MonoBehaviour
{
    [SerializeField] TMP_Dropdown textSizeDropdown;
    [SerializeField] TMP_Text[] textsToResize;

    const string SaveKey = "Accessibility.TextSize";

    readonly float[] scales = { 1f, 1.2f, 1.4f };
    float[] originalSizes;

    void Start()
    {
        originalSizes = new float[textsToResize.Length];

        for (int i = 0; i < textsToResize.Length; i++)
        {
            if (textsToResize[i] != null)
                originalSizes[i] = textsToResize[i].fontSize;
        }

        int saved = Mathf.Clamp(
            PlayerPrefs.GetInt(SaveKey, 0),
            0,
            scales.Length - 1
        );

        textSizeDropdown.SetValueWithoutNotify(saved);
        ApplySize(saved);

        textSizeDropdown.onValueChanged.AddListener(ChangeSize);
    }

    void ChangeSize(int index)
    {
        index = Mathf.Clamp(index, 0, scales.Length - 1);
        ApplySize(index);

        PlayerPrefs.SetInt(SaveKey, index);
        PlayerPrefs.Save();
    }

    void ApplySize(int index)
    {
        for (int i = 0; i < textsToResize.Length; i++)
        {
            TMP_Text text = textsToResize[i];

            if (text == null)
                continue;

            text.enableAutoSizing = false;
            text.fontSize = originalSizes[i] * scales[index];
        }
    }

    void OnDestroy()
    {
        if (textSizeDropdown != null)
            textSizeDropdown.onValueChanged.RemoveListener(ChangeSize);
    }
}