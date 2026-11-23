using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class BookDeckSlot : MonoBehaviour, ISelectHandler, IPointerEnterHandler
{
    [SerializeField] Image frame, artwork;
    [SerializeField] TMP_Text cardName, quantity;
    [SerializeField] Sprite filledFrame, emptyFrame;
    BookDeckUI owner;
    CardData card;
    bool inDeck;
    Sprite generated;
    float lastDescription = -10;
    public Button Button { get; private set; }
    public CardData Card => card;
    void Awake() { Button = GetComponent<Button>(); Button.onClick.AddListener(Activate); }

    public void Bind(BookDeckUI ui, CardData data, bool deck, int owned, float scale)
    {
        owner = ui; card = data; inDeck = deck;
        Button.interactable = card != null && ui.CanEdit;
        frame.sprite = card != null ? filledFrame : emptyFrame;
        frame.color = card != null ? Color.white : new Color(.32f,.32f,.32f);
        cardName.text = card != null ? card.displayName : string.Empty;
        cardName.fontSize = 14 * scale;
        quantity.text = card != null && !deck && owned > 1 ? "×" + owned : string.Empty;
        quantity.fontSize = 16 * scale;
        if (generated != null) Destroy(generated);
        generated = null; artwork.sprite = null;
        if (card != null && card.frontArt != null)
        {
            generated = Sprite.Create(card.frontArt, new Rect(0,0,card.frontArt.width,card.frontArt.height), new Vector2(.5f,.5f));
            artwork.sprite = generated;
        }
        artwork.enabled = generated != null;
    }
    void Activate() { if (card != null && owner != null) owner.Activate(card, inDeck); }
    void Describe()
    {
        if (owner == null || card == null || Time.unscaledTime - lastDescription < .2f) return;
        lastDescription = Time.unscaledTime; owner.Describe(card, inDeck);
    }
    public void OnSelect(BaseEventData data) => Describe();
    public void OnPointerEnter(PointerEventData data)
    {
        if (data is UnityEngine.InputSystem.UI.ExtendedPointerEventData pointer && pointer.pointerType == UnityEngine.InputSystem.UI.UIPointerType.Touch) return;
        Describe();
    }
    void OnDestroy() { if (generated != null) Destroy(generated); }
}
