using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(TMP_Dropdown))]
public class NarratedDropdown : MonoBehaviour, ISelectHandler
{
    [SerializeField] MenuNarration narration;
    [SerializeField] string spokenName = "Text size";

    TMP_Dropdown dropdown;

    void Awake()
    {
        dropdown = GetComponent<TMP_Dropdown>();
    }

    void OnEnable()
    {
        dropdown.onValueChanged.AddListener(OnValueChanged);
    }

    public void OnSelect(BaseEventData eventData)
    {
        Announce();
    }

    void OnValueChanged(int value)
    {
        Announce();
    }

    void Announce()
    {
        if (dropdown.options.Count == 0)
            return;

        string option = dropdown.options[dropdown.value].text;
        narration.Speak($"{spokenName}, {option}.");
    }

    void OnDisable()
    {
        dropdown.onValueChanged.RemoveListener(OnValueChanged);
    }
}