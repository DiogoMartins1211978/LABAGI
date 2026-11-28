using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Keep native InputSystemUIInputModule mouse/touch events; only convert the
// RenderTexture coordinates before ScrollRect projects them onto the page.
public class BookScrollRect : ScrollRect
{
    BookPageRaycaster page;
    [SerializeField] bool discretePaging;
    Vector2 dragStart;
    public event Action<int> PageRequested;
    void Mapped(PointerEventData data, Action<PointerEventData> action)
    {
        if (page == null) page = GetComponentInParent<BookPageRaycaster>();
        var position = data.position;
        var press = data.pressPosition;
        var delta = data.delta;
        if (page != null && page.isActiveAndEnabled && page.MapScreenPoint(position, out var mapped))
        {
            page.MapScreenPoint(press, out var mappedPress);
            page.MapScreenPoint(position - delta, out var previous);
            data.position = mapped;
            data.pressPosition = mappedPress;
            data.delta = mapped - previous;
        }
        try { action(data); }
        finally { data.position = position; data.pressPosition = press; data.delta = delta; }
    }
    public override void OnInitializePotentialDrag(PointerEventData data) => Mapped(data, base.OnInitializePotentialDrag);
    public override void OnBeginDrag(PointerEventData data)
    {
        if(discretePaging){dragStart=data.position;return;}
        Mapped(data,base.OnBeginDrag);
    }
    public override void OnDrag(PointerEventData data)
    {
        if(!discretePaging){Mapped(data,base.OnDrag);return;}
        float delta=data.position.x-dragStart.x;
        if(Mathf.Abs(delta)<Mathf.Max(20,Screen.width*.045f))return;
        PageRequested?.Invoke(delta<0?1:-1);
        dragStart=data.position;
    }
    public override void OnEndDrag(PointerEventData data)
    {
        if(!discretePaging)Mapped(data,base.OnEndDrag);
    }
    public override void OnScroll(PointerEventData data)
    {
        if(!discretePaging){base.OnScroll(data);return;}
        float delta=Mathf.Abs(data.scrollDelta.x)>.01f?data.scrollDelta.x:-data.scrollDelta.y;
        if(Mathf.Abs(delta)>.01f)PageRequested?.Invoke(delta>0?1:-1);
    }
}
