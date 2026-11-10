using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HandView : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Found automatically on a parent (the Player) if left empty")]
    public CardSystem cardSystem;

    [Header("Where the whole hand sits (relative to this object)")]
    [Tooltip("Moves the whole fan: X = left/right, Y = up/down, Z = away from the camera")]
    public Vector3 fanOffset = Vector3.zero;
    [Tooltip("Rotates the whole fan around this object (degrees)")]
    public Vector3 fanRotation = Vector3.zero;

    [Header("Fan shape")]
    [Tooltip("Total spread of the fan in degrees")]
    public float fanAngle = 30f;
    [Tooltip("Bigger = flatter fan")]
    public float radius = 0.9f;
    [Tooltip("Tiny depth step so overlapping cards don't flicker")]
    public float depthStep = 0.01f;

    [Header("Each card")]
    [Tooltip("Size of each card")]
    public float cardScale = 0.35f;
    [Tooltip("Extra rotation on every card (degrees). If cards look backwards, try Y = 180.")]
    public Vector3 cardRotationOffset = Vector3.zero;
    [Tooltip("Card size in your prefab, only used to draw the Scene view outlines")]
    public Vector2 cardSize = new Vector2(0.63f, 0.88f);

    [Header("Selected card")]
    public float selectedLift = 0.07f;
    public float selectedForward = 0.05f;
    public float selectedScale = 1.2f;

    [Header("Motion")]
    public float smooth = 12f;
    [Tooltip("Where new cards slide in from (relative to this object). Green sphere in the Scene view.")]
    public Vector3 spawnOffset = new Vector3(0.5f, -0.3f, 0f);
    [Tooltip("How far a played card flies from its slot. Red sphere in the Scene view.")]
    public Vector3 playOffset = new Vector3(0f, 0.3f, 0.3f);
    public float flyTime = 0.35f;

    readonly List<CardView> views = new List<CardView>();

    void OnEnable()
    {
        if (cardSystem == null) cardSystem = GetComponentInParent<CardSystem>();
        if (cardSystem != null) cardSystem.StateChanged += Sync;
    }

    void OnDisable()
    {
        if (cardSystem != null) cardSystem.StateChanged -= Sync;
    }

    void Start() => Sync();

    void GetSlot(int i, int count, bool selected, out Vector3 pos, out Quaternion rot, out float scale)
    {
        float t = count <= 1 ? 0f : (i / (float)(count - 1) - 0.5f) * 2f;
        float angle = t * fanAngle * 0.5f * Mathf.Deg2Rad;

        pos = new Vector3(Mathf.Sin(angle) * radius,
                          Mathf.Cos(angle) * radius - radius,
                          -i * depthStep);
        rot = Quaternion.Euler(0f, 0f, -angle * Mathf.Rad2Deg);
        scale = cardScale;

        if (selected)
        {
            pos += new Vector3(0f, selectedLift, -selectedForward);
            rot = Quaternion.identity;
            scale *= selectedScale;
        }

        Quaternion fan = Quaternion.Euler(fanRotation);
        pos = fan * pos + fanOffset;
        rot = fan * rot * Quaternion.Euler(cardRotationOffset);
    }

    void Sync()
    {
        if (cardSystem == null) return;

        if (cardSystem.CurrentMode != CardSystem.Mode.Play)
        {
            ClearAll();
            return;
        }

        IReadOnlyList<CardData> hand = cardSystem.Hand;

        while (views.Count > hand.Count)
        {
            if (views[views.Count - 1] != null) Destroy(views[views.Count - 1].gameObject);
            views.RemoveAt(views.Count - 1);
        }
        while (views.Count < hand.Count) views.Add(null);

        for (int i = 0; i < hand.Count; i++)
        {
            if (views[i] != null && views[i].Data == hand[i]) continue;

            if (views[i] != null) StartCoroutine(FlyAway(views[i]));
            views[i] = Spawn(hand[i]);
        }
    }

    CardView Spawn(CardData data)
    {
        CardView view = data.CreateView(transform);
        if (view == null) return null;

        view.SetFaceUp(true);
        view.transform.localPosition = spawnOffset;
        view.transform.localScale = Vector3.zero;

        view.CardHovered += (v) =>
        {
            int idx = views.IndexOf(v);
            if (idx != -1 && cardSystem != null)
            {
                cardSystem.SetSelection(idx);
            }
        };

        view.CardClicked += (v) =>
        {
            int idx = views.IndexOf(v);
            if (idx != -1 && cardSystem != null)
            {
                cardSystem.PlayIndexDirectly(idx);
            }
        };

        return view;
    }

    void ClearAll()
    {
        foreach (CardView v in views)
            if (v != null) Destroy(v.gameObject);
        views.Clear();
    }

    void Update()
    {
        int count = views.Count;
        float follow = 1f - Mathf.Exp(-smooth * Time.deltaTime);

        for (int i = 0; i < count; i++)
        {
            CardView v = views[i];
            if (v == null) continue;

            GetSlot(i, count, i == cardSystem.SelectedIndex, out Vector3 pos, out Quaternion rot, out float scale);

            Transform tr = v.transform;
            tr.localPosition = Vector3.Lerp(tr.localPosition, pos, follow);
            tr.localRotation = Quaternion.Slerp(tr.localRotation, rot, follow);
            tr.localScale = Vector3.Lerp(tr.localScale, Vector3.one * scale, follow);
        }
    }

    IEnumerator FlyAway(CardView v)
    {
        Transform tr = v.transform;
        Vector3 startPos = tr.localPosition;
        Vector3 startScale = tr.localScale;
        Vector3 endPos = startPos + playOffset;

        float t = 0f;
        while (t < 1f && v != null)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, flyTime);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            tr.localPosition = Vector3.Lerp(startPos, endPos, k);
            tr.localScale = Vector3.Lerp(startScale, Vector3.zero, k);
            yield return null;
        }

        if (v != null) Destroy(v.gameObject);
    }

    void OnDrawGizmos()
    {
        CardSystem cs = cardSystem != null ? cardSystem : GetComponentInParent<CardSystem>();
        int count = cs != null ? Mathf.Max(1, cs.handSize) : 4;
        int selected = (cs != null && Application.isPlaying) ? cs.SelectedIndex : 0;

        Matrix4x4 parent = transform.localToWorldMatrix;

        for (int i = 0; i < count; i++)
        {
            GetSlot(i, count, i == selected, out Vector3 pos, out Quaternion rot, out float scale);

            Gizmos.matrix = parent * Matrix4x4.TRS(pos, rot, Vector3.one * scale);
            Gizmos.color = i == selected ? Color.yellow : Color.cyan;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(cardSize.x, cardSize.y, 0.002f));

            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(Vector3.zero, Vector3.back * 0.3f);
        }

        Gizmos.matrix = parent;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(spawnOffset, 0.03f);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(playOffset, 0.03f);
    }
}