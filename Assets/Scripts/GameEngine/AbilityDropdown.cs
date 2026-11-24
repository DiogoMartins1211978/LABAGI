using TMPro;
using UnityEngine;

public class AbilityDropdown : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown mainDropdown;
    [SerializeField] private TMP_Text mainLabel;
    [SerializeField] private TMP_Dropdown abilityDropdown;
    [SerializeField] private TMP_Text abilityLabel;
    [SerializeField] private float leftOffset = 230f;

    private Vector2 initialDropdownPosition;
    private Vector2 initialLabelPosition;

    private void Awake()
    {
        initialDropdownPosition = mainDropdown.GetComponent<RectTransform>().anchoredPosition;
        initialLabelPosition = mainLabel.rectTransform.anchoredPosition;
    }

    private void OnEnable()
    {
        mainDropdown.onValueChanged.AddListener(UpdateVisibility);
        UpdateVisibility(mainDropdown.value);
    }

    private void OnDisable()
    {
        mainDropdown.onValueChanged.RemoveListener(UpdateVisibility);
    }

    private void UpdateVisibility(int selectedIndex)
    {
        bool showAbilityOptions = selectedIndex >= 0 &&
            selectedIndex < mainDropdown.options.Count &&
            mainDropdown.options[selectedIndex].text == "Ability";

        Vector2 offset = showAbilityOptions ? Vector2.left * leftOffset : Vector2.zero;

        mainDropdown.GetComponent<RectTransform>().anchoredPosition =
            initialDropdownPosition + offset;
        mainLabel.rectTransform.anchoredPosition = initialLabelPosition + offset;

        abilityDropdown.gameObject.SetActive(showAbilityOptions);
        abilityLabel.gameObject.SetActive(showAbilityOptions);
    }
}