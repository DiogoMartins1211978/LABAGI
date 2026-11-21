using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class BestiaryDropSlot : MonoBehaviour, ISelectHandler, IPointerEnterHandler
{
    [SerializeField] TMP_Text label;
    [SerializeField] Image artwork;
    [SerializeField] Sprite filledFrame;
    [SerializeField] Sprite emptyFrame;
    BestiaryDropCarousel owner;
    CombatManager card;
    int index;
    Sprite generatedSprite;
    public Button Button { get; private set; }
    void Awake(){Button=GetComponent<Button>();Button.onClick.AddListener(Read);}
    public void Bind(BestiaryDropCarousel carousel,CombatManager data,int slotIndex,float textScale,bool unknown = false)
    {
        owner=carousel;card=data;index=slotIndex;Button.interactable=data!=null;
        var frame=GetComponent<Image>();
        if(filledFrame!=null&&emptyFrame!=null)
        {
            frame.sprite=data!=null?filledFrame:emptyFrame;
            frame.color=data!=null?Color.white:new Color(.32f,.32f,.32f);
            label.color=data!=null?Color.white:new Color(.12f,.14f,.17f);
        }
        if(generatedSprite!=null)Destroy(generatedSprite);
        generatedSprite=null;
        artwork.sprite=null;
        label.text=data!=null ? data.displayName : unknown ? "?" : string.Empty;label.fontSize=14*textScale;
        if(data!=null&&data.frontArt!=null)
        {
            generatedSprite=Sprite.Create(data.frontArt,new Rect(0,0,data.frontArt.width,data.frontArt.height),new Vector2(.5f,.5f));
            artwork.sprite=generatedSprite;
        }
        artwork.enabled=artwork.sprite!=null;
    }
    public void OnSelect(BaseEventData data){if(owner!=null){owner.Reveal(index);owner.Describe(card,index);}}
    public void OnPointerEnter(PointerEventData data)
    {
        if(data is UnityEngine.InputSystem.UI.ExtendedPointerEventData pointer && pointer.pointerType==UnityEngine.InputSystem.UI.UIPointerType.Touch)return;
        if(owner!=null)owner.Describe(card,index);
    }
    void Read(){if(owner!=null)owner.Describe(card,index,true);}
    void OnDestroy(){if(generatedSprite!=null)Destroy(generatedSprite);}
}
