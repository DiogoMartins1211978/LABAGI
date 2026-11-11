using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Slider))]
public class NarratedSlider : MonoBehaviour,
    ISelectHandler, IDeselectHandler
{
    [SerializeField] MenuNarration narration;
    [SerializeField] string spokenName = "Master volume";

    Slider slider;
    Coroutine pendingAnnouncement;
    bool selected;

    void Awake()
    {
        slider = GetComponent<Slider>();
    }

    void OnEnable()
    {
        slider.onValueChanged.AddListener(OnValueChanged);
    }

    public void OnSelect(BaseEventData eventData)
    {
        selected = true;
        Announce();
    }

    public void OnDeselect(BaseEventData eventData)
    {
        selected = false;
        CancelPending();
    }

    void OnValueChanged(float value)
    {
        if (!selected)
            return;

        CancelPending();
        pendingAnnouncement = StartCoroutine(AnnounceAfterPause());
    }

    IEnumerator AnnounceAfterPause()
    {
        yield return new WaitForSecondsRealtime(0.3f);

        pendingAnnouncement = null;
        Announce();
    }

    void Announce()
    {
        int percentage = Mathf.RoundToInt(slider.normalizedValue * 100);
        narration.Speak($"{spokenName}, {percentage} percent.");
    }

    void CancelPending()
    {
        if (pendingAnnouncement != null)
        {
            StopCoroutine(pendingAnnouncement);
            pendingAnnouncement = null;
        }
    }

    void OnDisable()
    {
        slider.onValueChanged.RemoveListener(OnValueChanged);
        selected = false;
        CancelPending();
    }
}