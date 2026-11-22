using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

// Read live card counts rather than sending the visual Roman numerals to TTS.
public class BookDeckCountNarration : MonoBehaviour, IPointerEnterHandler, IPointerDownHandler
{
    [SerializeField] CardSystem cards;
    [SerializeField] MenuNarration narration;
    float lastSpoken = -10;
    public string Description => cards != null ? "Deck. " + cards.deck.Count + " of " + cards.deckSize + " cards." : string.Empty;

    public void Describe()
    {
        if (!isActiveAndEnabled || cards == null || narration == null || Time.unscaledTime - lastSpoken < .35f) return;
        lastSpoken = Time.unscaledTime;
        narration.Speak(Description);
    }
    public void OnPointerEnter(PointerEventData data)
    {
        // Touch reads on contact; avoid speaking once on Enter and again on Down.
        if (data is ExtendedPointerEventData pointer && pointer.pointerType == UIPointerType.Touch) return;
        Describe();
    }
    public void OnPointerDown(PointerEventData data) => Describe();
}
