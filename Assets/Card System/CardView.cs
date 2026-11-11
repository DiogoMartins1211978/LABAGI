using System;
using TMPro;
using UnityEngine;

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
    MaterialPropertyBlock block;

    public void Setup(CardData data)
    {
        Data = data;

        if (iconRenderer != null && data.frontArt != null)
        {
            iconRenderer.material.mainTexture = data.frontArt;

            float texAspect = (float)data.frontArt.width / data.frontArt.height;

            Vector3 currentScale = iconRenderer.transform.localScale;
            currentScale.x = currentScale.y * texAspect;
            iconRenderer.transform.localScale = currentScale;
        }
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
}