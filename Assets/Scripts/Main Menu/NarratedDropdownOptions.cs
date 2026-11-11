using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class NarratedDropdownOption : MonoBehaviour, ISelectHandler
{
    [SerializeField] MenuNarration narration;
    [SerializeField] TMP_Text optionLabel;

    public void OnSelect(BaseEventData eventData)
    {
        if (narration != null && optionLabel != null)
            narration.Speak(optionLabel.text);
    }
}