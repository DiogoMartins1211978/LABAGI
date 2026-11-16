using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class MenuTextSize : MonoBehaviour
{
    [SerializeField] TMP_Dropdown textSizeDropdown;
    [SerializeField] TMP_Text[] textsToResize;
    [Tooltip("Optional Canvas/root containing additional menu text, including inactive panels.")]
    [SerializeField] Transform additionalTextRoot;
    [Tooltip("Large decorative titles are excluded from automatic discovery.")]
    [SerializeField] float largestAutomaticFontSize = 48f;

    const string SaveKey = "Accessibility.TextSize";

    readonly float[] scales = { 1f, 1.2f, 1.4f };
    float[] originalSizes;
    TMP_InputField[] inputFields;
    float[] originalInputSizes;

    void Start()
    {
        // Preserve explicit assignments and collect new panels without duplicate scaling.
        var texts = new List<TMP_Text>();
        var seen = new HashSet<TMP_Text>();
        foreach (var text in textsToResize)
            if (text != null && seen.Add(text)) texts.Add(text);
        if (additionalTextRoot != null)
            foreach (var text in additionalTextRoot.GetComponentsInChildren<TMP_Text>(true))
                if (text.fontSize <= largestAutomaticFontSize && seen.Add(text)) texts.Add(text);
        textsToResize = texts.ToArray();

        inputFields = additionalTextRoot != null
            ? additionalTextRoot.GetComponentsInChildren<TMP_InputField>(true)
            : new TMP_InputField[0];
        originalInputSizes = new float[inputFields.Length];
        for (int i = 0; i < inputFields.Length; i++)
            originalInputSizes[i] = inputFields[i].textComponent != null
                ? inputFields[i].textComponent.fontSize : inputFields[i].pointSize;

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
        for (int i = 0; i < inputFields.Length; i++)
            if (inputFields[i] != null)
                inputFields[i].pointSize = originalInputSizes[i] * scales[index];
    }

    void OnDestroy()
    {
        if (textSizeDropdown != null)
            textSizeDropdown.onValueChanged.RemoveListener(ChangeSize);
    }
}
