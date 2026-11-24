using TMPro;
using UnityEngine;

[RequireComponent(typeof(TMP_Dropdown))]
public class AbilityDurationDropdown : MonoBehaviour
{
    [SerializeField] private GameObject turnsField;

    [Header("Elements to move")]
    [SerializeField] private RectTransform firstDropdown;
    [SerializeField] private RectTransform firstLabel;
    [SerializeField] private RectTransform secondLabel;

    [Header("Layout")]
    [SerializeField] private float leftOffset = 230f;

    private TMP_Dropdown abilityTypeDropdown;
    private RectTransform dropdownRect;

    private Vector2 initialPosition;
    private Vector2 firstDropdownInitialPosition;
    private Vector2 firstLabelInitialPosition;
    private Vector2 secondLabelInitialPosition;

    private void Awake()
    {
        abilityTypeDropdown = GetComponent<TMP_Dropdown>();
        dropdownRect = GetComponent<RectTransform>();

        // Guardar as posições originais apenas uma vez
        initialPosition = dropdownRect.anchoredPosition;

        if (firstDropdown != null)
            firstDropdownInitialPosition = firstDropdown.anchoredPosition;

        if (firstLabel != null)
            firstLabelInitialPosition = firstLabel.anchoredPosition;

        if (secondLabel != null)
            secondLabelInitialPosition = secondLabel.anchoredPosition;
    }

    private void OnEnable()
    {
        // Primeiro garantir que tudo começa na posição original
        ResetPositions();

        abilityTypeDropdown.onValueChanged.AddListener(UpdateTurnsVisibility);

        // Depois aplicar o estado atual
        UpdateTurnsVisibility(abilityTypeDropdown.value);
    }

    private void OnDisable()
    {
        abilityTypeDropdown.onValueChanged.RemoveListener(UpdateTurnsVisibility);

        // Garantir que ao sair da página os elementos ficam restaurados
        ResetPositions();
    }

    private void ResetPositions()
    {
        dropdownRect.anchoredPosition = initialPosition;

        if (firstDropdown != null)
            firstDropdown.anchoredPosition = firstDropdownInitialPosition;

        if (firstLabel != null)
            firstLabel.anchoredPosition = firstLabelInitialPosition;

        if (secondLabel != null)
            secondLabel.anchoredPosition = secondLabelInitialPosition;
    }

    private void UpdateTurnsVisibility(int selectedIndex)
    {
        bool showTurns = selectedIndex >= 0 &&
            selectedIndex < abilityTypeDropdown.options.Count &&
            abilityTypeDropdown.options[selectedIndex].text == "Damage Over Time";

        turnsField.SetActive(showTurns);

        Vector2 offset = showTurns
            ? Vector2.left * leftOffset
            : Vector2.zero;

        // Ability Type dropdown
        dropdownRect.anchoredPosition = initialPosition + offset;

        // First dropdown
        if (firstDropdown != null)
        {
            firstDropdown.anchoredPosition =
                firstDropdownInitialPosition + offset;
        }

        // First label
        if (firstLabel != null)
        {
            firstLabel.anchoredPosition =
                firstLabelInitialPosition + offset;
        }

        // Second label
        if (secondLabel != null)
        {
            secondLabel.anchoredPosition =
                secondLabelInitialPosition + offset;
        }
    }
}