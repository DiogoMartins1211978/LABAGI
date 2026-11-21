using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class BookControlNarration : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler
{
    [SerializeField] MenuNarration narration;
    [SerializeField, TextArea] string description;
    [SerializeField] BookAudio bookAudio;
    [SerializeField] bool bookmark;
    [SerializeField] bool emphasizeInk = true;
    float lastSpoken = -10;
    Button button;
    void Awake() { button=GetComponent<Button>();button.onClick.AddListener(Activated); }
    void Activated() { if(bookmark && bookAudio!=null) bookAudio.Play(BookAudio.Cue.Bookmark); }
    public void Describe()
    {
        if(Time.unscaledTime-lastSpoken<.2f)return;
        lastSpoken=Time.unscaledTime;
        if(narration!=null)narration.Speak(description);
    }
    public void OnSelect(BaseEventData data) { Describe(); if(emphasizeInk) foreach(var ink in GetComponentsInChildren<BookInkGraphic>())ink.SetEmphasis(true); }
    public void OnDeselect(BaseEventData data) { if(emphasizeInk) foreach(var ink in GetComponentsInChildren<BookInkGraphic>())ink.SetEmphasis(false); }
    public void OnPointerEnter(PointerEventData data) => Describe();
    void OnDestroy() { if(button!=null)button.onClick.RemoveListener(Activated); }
}
