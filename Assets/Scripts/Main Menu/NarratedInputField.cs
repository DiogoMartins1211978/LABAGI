using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(TMP_InputField))]
public class NarratedInputField : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    [SerializeField] MenuNarration narration;
    [SerializeField] string spokenName = "Session code";
    TMP_InputField input;
    Coroutine pending;
    bool selected;

    void Awake() => input = GetComponent<TMP_InputField>();

    void OnEnable() => input.onValueChanged.AddListener(OnValueChanged);

    public void OnSelect(BaseEventData eventData)
    {
        selected = true;
        Say(spokenName + ", " + SpokenValue() + ". Type a code. Tab moves to the next control.");
    }

    public void OnDeselect(BaseEventData eventData)
    {
        selected = false;
        CancelPending();
    }

    void OnValueChanged(string value)
    {
        CancelPending();
        if (selected) pending = StartCoroutine(AnnounceAfterPause());
    }

    IEnumerator AnnounceAfterPause()
    {
        yield return new WaitForSecondsRealtime(0.6f);
        pending = null;
        Say(spokenName + ", " + SpokenValue() + ".");
    }

    string SpokenValue() => string.IsNullOrEmpty(input.text) ? "empty"
        : string.Join(" ", System.Array.ConvertAll(input.text.ToCharArray(), c => c.ToString()));

    void Say(string message)
    {
        if (narration != null) narration.Speak(message);
    }

    void CancelPending()
    {
        if (pending != null) StopCoroutine(pending);
        pending = null;
    }

    void OnDisable()
    {
        input.onValueChanged.RemoveListener(OnValueChanged);
        selected = false;
        CancelPending();
    }
}
