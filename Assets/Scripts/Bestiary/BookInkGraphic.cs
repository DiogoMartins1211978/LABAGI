using UnityEngine;
using UnityEngine.UI;

// White ink strokes, without a rectangular button panel on the paper.
[RequireComponent(typeof(CanvasRenderer))]
public class BookInkGraphic : MaskableGraphic
{
    public enum Shape { Frame, PreviousLeaf, NextLeaf, LeftArrow, RightArrow, Rule, PreviousDropLeaf, NextDropLeaf }
    [SerializeField] Shape shape;
    [SerializeField] float thickness = 1.5f;
    public void SetEmphasis(bool emphasized) { thickness = emphasized ? 3 : 1.5f; SetVerticesDirty(); }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();var r=rectTransform.rect;
        void Stroke(Vector2 a,Vector2 b)
        {
            a = new Vector2(r.xMin + a.x*r.width,r.yMin+a.y*r.height);
            b = new Vector2(r.xMin + b.x*r.width,r.yMin+b.y*r.height);
            var n = new Vector2(-(b-a).y,(b-a).x).normalized*thickness/2;
            int index=vh.currentVertCount;
            vh.AddVert(a-n,color,Vector2.zero);vh.AddVert(a+n,color,Vector2.zero);
            vh.AddVert(b+n,color,Vector2.zero);vh.AddVert(b-n,color,Vector2.zero);
            vh.AddTriangle(index,index+1,index+2);vh.AddTriangle(index,index+2,index+3);
        }
        if(shape==Shape.Frame){Stroke(new Vector2(0,0),new Vector2(1,0));Stroke(new Vector2(1,0),new Vector2(1,1));Stroke(new Vector2(1,1),new Vector2(0,1));Stroke(new Vector2(0,1),new Vector2(0,0));}
        else if(shape==Shape.Rule)Stroke(new Vector2(0,.5f),new Vector2(1,.5f));
        else if(shape==Shape.PreviousDropLeaf||shape==Shape.NextDropLeaf)
        {
            bool left=shape==Shape.PreviousDropLeaf;
            Vector2 P(float x,float y)
            {
                var p=(Vector2)(Quaternion.Euler(0,0,45)*new Vector3(x-.5f,y-.5f,0));
                float nx=.2f+(p.x+.14142136f)/.56568542f*.6f;
                float ny=.2f+(p.y+.42426407f)/.84852814f*.6f;
                return new Vector2(left?1-nx:nx,ny);
            }
            Stroke(P(.2f,.2f),P(.8f,.2f));Stroke(P(.8f,.2f),P(.8f,.8f));Stroke(P(.8f,.8f),P(.2f,.2f));Stroke(P(.2f,.2f),P(.35f,.55f));Stroke(P(.35f,.55f),P(.8f,.8f));
        }
        else if(shape==Shape.LeftArrow||shape==Shape.RightArrow){float x=shape==Shape.LeftArrow?.25f:.75f;Stroke(new Vector2(1-x,.2f),new Vector2(x,.5f));Stroke(new Vector2(x,.5f),new Vector2(1-x,.8f));}
        else {bool left=shape==Shape.PreviousLeaf;Vector2 P(float x,float y)=>new Vector2(left?1-x:x,y);Stroke(P(.2f,.2f),P(.8f,.2f));Stroke(P(.8f,.2f),P(.8f,.8f));Stroke(P(.8f,.8f),P(.2f,.2f));Stroke(P(.2f,.2f),P(.35f,.55f));Stroke(P(.35f,.55f),P(.8f,.8f));}
    }
}
