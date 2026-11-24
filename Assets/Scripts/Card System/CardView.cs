
using System;
using UnityEngine;
using static CardData;

public class CardView : MonoBehaviour
{
    [Header("Faces")]
    public Renderer iconRenderer;

    public CardData Data { get; private set; }
    public bool FaceUp { get; private set; } = true;

    public event Action<CardView> CardClicked;
    public event Action<CardView> CardHovered;

    static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    static readonly int MainTexId = Shader.PropertyToID("_MainTex");

    [Header("Rarity Visuals")]
    public Renderer rarityFrameRenderer;

    public Texture2D commonFrame;
    public Texture2D rareFrame;
    public Texture2D epicFrame;
    public Texture2D legendaryFrame;
    public Texture2D mythicFrame;

    MaterialPropertyBlock block;

    public int Level { get; private set; }

    public void Setup(CardData data, int level)
    {
        Level = level;
        Data = data;

        if (iconRenderer != null && data.frontArt != null)
        {
            iconRenderer.material.mainTexture = data.frontArt;

            float texAspect = (float)data.frontArt.width / data.frontArt.height;

            Vector3 currentScale = iconRenderer.transform.localScale;
            currentScale.x = currentScale.y * texAspect;
            iconRenderer.transform.localScale = currentScale;
        }

        ApplyLevelAppearance(level);
    }

    private void ApplyLevelAppearance(int level)
    {
        if (rarityFrameRenderer == null)
            return;

        Texture2D texture = level switch
        {
            1 => commonFrame,
            2 => rareFrame,
            3 => epicFrame,
            4 => legendaryFrame,
            5 => mythicFrame,
            _ => commonFrame
        };

        if (texture != null)
            rarityFrameRenderer.material.mainTexture = texture;
    }

    public void SetFaceUp(bool faceUp)
    {
        FaceUp = faceUp;
        transform.localRotation = Quaternion.Euler(0f, faceUp ? 0f : 180f, 0f);
    }

    void OnMouseEnter()
    {
        CardHovered?.Invoke(this);
    }

    void OnMouseDown()
    {
        CardClicked?.Invoke(this);
    }

    public void SetLevel(int level)
    {
        Level = level;
        ApplyLevelAppearance(level);
    }
}
