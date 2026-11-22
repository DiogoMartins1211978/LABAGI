using System;
using System.Collections;
using UnityEngine;

// Inspector-owned presentation: the book uses unscaled time while its overlay pauses gameplay.
public class BestiaryBookView : MonoBehaviour
{
    [SerializeField] GameObject bookRoot;
    [SerializeField] GameObject model;
    [SerializeField] AnimationClip openClip;
    [SerializeField] AnimationClip pageTurnClip;
    [SerializeField] AnimationClip closeClip;
    [SerializeField] CanvasGroup[] pageContent;
    [SerializeField] CanvasGroup bookmarkContent;
    [Tooltip("Loose leaves are visible during animations; reading uses the fixed page blocks.")]
    [SerializeField] SkinnedMeshRenderer[] turningLeaves;
    [Tooltip("Only leaves animated by the page-turn clip. Resting loose leaves overlap the fixed page blocks.")]
    [SerializeField] SkinnedMeshRenderer[] pageTurnLeaves;
    [SerializeField] BookAudio bookAudio;
    [SerializeField] float[] openingLeafCues = { 25f / 75, 42f / 75, 59f / 75 };
    [SerializeField, Min(.05f)] float openDuration = .65f;
    [SerializeField, Min(.05f)] float closeDuration = .35f;
    [SerializeField, Min(.05f)] float turnDuration = .45f;
    [SerializeField, Min(.01f)] float revealDuration = .18f;

    Coroutine transition;
    BookEdgeOutline[] outlines;
    BookBookmark[] bookmarks;
    SkinnedMeshRenderer[] surfaces;
    AnimationClip sampledClip;
    public bool IsTransitioning => transition != null;
    public bool IsVisible => bookRoot != null && bookRoot.activeSelf;

    void Awake()
    {
        outlines = model.GetComponentsInChildren<BookEdgeOutline>(true);
        bookmarks = bookRoot.GetComponentsInChildren<BookBookmark>(true);
        surfaces = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        ClearPose();
        openClip.SampleAnimation(model, openClip.length);
        sampledClip = openClip;
        foreach (var outline in outlines) outline.PrepareTopology();
        HideImmediately();
    }

    public void Open()
    {
        StopTransition();
        bookRoot.SetActive(true);
        Content(0);
        Bookmarks(0);
        SetTurningLeaves(true);
        if (bookAudio != null) { bookAudio.Play(BookAudio.Cue.Appear); bookAudio.Play(BookAudio.Cue.Open); }
        transition = StartCoroutine(AnimateOpen());
    }

    IEnumerator AnimateOpen()
    {
        yield return Animate(openClip, openDuration, false, true);
        SetTurningLeaves(false);
        yield return Reveal(true);
        transition = null;
    }

    public void Close(Action completed)
    {
        StopTransition();
        Content(0);
        Bookmarks(0);
        if (bookAudio != null) bookAudio.Play(BookAudio.Cue.Close);
        transition = StartCoroutine(AnimateClose(completed));
    }

    IEnumerator AnimateClose(Action completed)
    {
        yield return Animate(closeClip, closeDuration);
        bookRoot.SetActive(false);
        if (bookAudio != null) bookAudio.Play(BookAudio.Cue.Disappear);
        transition = null;
        completed?.Invoke();
    }

    public void TurnPage(Action updateContent, bool backwards = false)
    {
        if (!IsVisible || IsTransitioning) return;
        transition = StartCoroutine(AnimateTurn(updateContent, backwards));
    }

    IEnumerator AnimateTurn(Action updateContent, bool backwards)
    {
        Content(0);
        SetTurningLeaves(false);
        foreach (var leaf in pageTurnLeaves) if (leaf != null) leaf.enabled = true;
        if (bookAudio != null) bookAudio.Play(BookAudio.Cue.Leaf);
        yield return Animate(pageTurnClip, turnDuration, backwards);
        updateContent?.Invoke();
        SetTurningLeaves(false);
        Sample(openClip, openClip.length);
        yield return Reveal();
        transition = null;
    }

    IEnumerator Animate(AnimationClip clip, float duration, bool backwards = false, bool opening = false)
    {
        if (clip == null) yield break;
        float start = Time.realtimeSinceStartup;
        float time = 0;
        int nextCue = 0;
        while (time < duration)
        {
            float progress = Mathf.SmoothStep(0, 1, time / duration);
            if (opening) while (nextCue < openingLeafCues.Length && progress >= openingLeafCues[nextCue])
            {
                if (bookAudio != null) bookAudio.Play(BookAudio.Cue.Leaf);
                nextCue++;
            }
            Sample(clip, (backwards ? 1 - progress : progress) * clip.length);
            yield return null;
            time = Time.realtimeSinceStartup - start;
        }
        Sample(clip, backwards ? 0 : clip.length);
    }

    IEnumerator Reveal(bool includeBookmarks = false)
    {
        float time = 0;
        while (time < revealDuration)
        {
            Content(time / revealDuration);
            if (includeBookmarks) Bookmarks(time / revealDuration);
            yield return null;
            time += Time.unscaledDeltaTime;
        }
        Content(1);
        if (includeBookmarks) Bookmarks(1);
    }

    void Sample(AnimationClip clip, float time)
    {
        // Imported clips omit constant zero curves. Clear the previous clip's
        // pose keys so opening/turning/closing cannot add their deformations.
        if (sampledClip != clip) { ClearPose(); sampledClip = clip; }
        clip.SampleAnimation(model, time);
        if (outlines == null) outlines = model.GetComponentsInChildren<BookEdgeOutline>(true);
        foreach (var outline in outlines) if (outline != null && outline.isActiveAndEnabled) outline.Refresh();
    }

    void ClearPose()
    {
        if (surfaces == null) surfaces = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var surface in surfaces)
            for (int i = 0; i < surface.sharedMesh.blendShapeCount; i++) surface.SetBlendShapeWeight(i, 0);
    }

    void SetTurningLeaves(bool visible)
    {
        if (turningLeaves == null) return;
        foreach (var leaf in turningLeaves) if (leaf != null) leaf.enabled = visible;
    }

    void Content(float alpha)
    {
        foreach (var page in pageContent)
        {
            if (page == null) continue;
            page.alpha = alpha;
            page.interactable = page.blocksRaycasts = alpha >= .999f;
        }
    }

    void Bookmarks(float alpha)
    {
        if (bookmarkContent != null)
        {
            bookmarkContent.alpha = alpha;
            bookmarkContent.interactable = bookmarkContent.blocksRaycasts = alpha >= .999f;
        }
        if (bookmarks != null) foreach (var bookmark in bookmarks) if (bookmark != null) bookmark.Reveal(alpha);
    }

    public void HideImmediately()
    {
        StopTransition();
        Content(0);
        Bookmarks(0);
        SetTurningLeaves(false);
        if (bookRoot != null) bookRoot.SetActive(false);
    }

    void StopTransition()
    {
        if (transition != null) StopCoroutine(transition);
        transition = null;
    }

    void OnDisable() => HideImmediately();
}
