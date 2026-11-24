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

    static Sprite commonFrame;
    static Sprite rareFrame;
    static Sprite epicFrame;
    static Sprite legendaryFrame;
    static Sprite mythicFrame;

    void Awake() { Button = GetComponent<Button>(); Button.onClick.AddListener(Activate); }

    public void Bind(BookDeckUI ui, CardData data, bool deck, int level, float scale)
    {
        owner = ui;
        card = data;
        inDeck = deck;

        Button.interactable = card != null && ui.CanEdit;

        frame.sprite = card == null
            ? emptyFrame
            : GetRarityFrame(Mathf.Clamp(level, 1, 5));

        if (card != null && frame.sprite == null)
            frame.sprite = filledFrame;

        frame.color = card != null ? Color.white : new Color(.32f, .32f, .32f);

        cardName.text = card != null ? card.displayName : string.Empty;
        cardName.fontSize = 14 * scale;

        quantity.text = string.Empty;
        quantity.fontSize = 16 * scale;
        if (generated != null) Destroy(generated);
        generated = null;
        artwork.sprite = null;
        if (card != null && card.frontArt != null)
        {
            generated = Sprite.Create(card.frontArt, new Rect(0, 0, card.frontArt.width, card.frontArt.height), new Vector2(.5f, .5f));
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

    static void LoadRarityFrames()
    {
        if (commonFrame != null) return;

        commonFrame = Resources.Load<Sprite>("CardFrames/Common");
        rareFrame = Resources.Load<Sprite>("CardFrames/Rare");
        epicFrame = Resources.Load<Sprite>("CardFrames/Epic");
        legendaryFrame = Resources.Load<Sprite>("CardFrames/Legendary");
        mythicFrame = Resources.Load<Sprite>("CardFrames/Mythic");
    }

    Sprite GetRarityFrame(int level)
    {
        LoadRarityFrames();

        return level switch
        {
            1 => commonFrame,
            2 => rareFrame,
            3 => epicFrame,
            4 => legendaryFrame,
            5 => mythicFrame,
            _ => commonFrame
        };
    }
}
