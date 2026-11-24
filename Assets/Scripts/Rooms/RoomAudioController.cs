
using System.Collections;
using UnityEngine;

public class RoomAudioController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private AudioSource ambienceSource;

    [Header("Unexplored Room Ambience (5 clips)")]
    [SerializeField] private AudioClip[] unexploredClips = new AudioClip[5];

    [Header("Explored Room Ambience (2 clips)")]
    [SerializeField] private AudioClip[] exploredClips = new AudioClip[2];

    [Header("Audio Volumes")]
    [Range(0f, 1f)]
    [SerializeField] private float currentRoomVolume = 0.15f;

    [Range(0f, 1f)]
    [SerializeField] private float adjacentRoomVolume = 0.50f;

    [SerializeField] private float fadeDuration = 1.5f;

    private Coroutine fadeRoutine;

    private void Awake()
    {
        if (ambienceSource == null)
            ambienceSource = GetComponent<AudioSource>();

        if (ambienceSource == null)
        {
            Debug.LogWarning($"[RoomAudio] No AudioSource on {name}.");
            return;
        }

        ambienceSource.playOnAwake = false;
        ambienceSource.loop = true;
        ambienceSource.volume = 0f;
    }

    public void EnterRoom(bool previouslyVisited)
    {
        AudioClip[] choices = previouslyVisited
            ? exploredClips
            : unexploredClips;

        AudioClip selectedClip = GetClipForState(choices);

        if (selectedClip == null || ambienceSource == null)
            return;

        PlayAmbience(selectedClip, currentRoomVolume);
    }

    public void EnterAdjacentRoom(bool previouslyVisited)
    {
        if (ambienceSource == null)
            return;

        AudioClip[] choices = previouslyVisited
            ? exploredClips
            : unexploredClips;

        AudioClip selectedClip = GetClipForState(choices);

        if (selectedClip == null)
            return;

        PlayAmbience(selectedClip, adjacentRoomVolume);
    }

    public void LeaveRoom()
    {
        if (ambienceSource == null)
            return;

        StopFade();

        fadeRoutine = StartCoroutine(FadeOut());
    }

    private AudioClip GetClipForState(AudioClip[] choices)
    {
        if (ambienceSource != null &&
            ambienceSource.isPlaying &&
            ContainsClip(choices, ambienceSource.clip))
        {
            return ambienceSource.clip;
        }

        return GetRandomClip(choices);
    }

    private bool ContainsClip(AudioClip[] clips, AudioClip target)
    {
        if (clips == null || target == null)
            return false;

        foreach (AudioClip clip in clips)
        {
            if (clip == target)
                return true;
        }

        return false;
    }

    private AudioClip GetRandomClip(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0)
            return null;

        int validCount = 0;

        foreach (AudioClip clip in clips)
            if (clip != null)
                validCount++;

        if (validCount == 0)
            return null;

        int selected = Random.Range(0, validCount);

        foreach (AudioClip clip in clips)
        {
            if (clip == null) continue;

            if (selected == 0)
                return clip;

            selected--;
        }

        return null;
    }

    private void PlayAmbience(AudioClip newClip, float targetVolume)
    {
        StopFade();

        if (ambienceSource.clip == newClip && ambienceSource.isPlaying)
        {
            fadeRoutine = StartCoroutine(FadeToVolume(targetVolume));
        }
        else
        {
            fadeRoutine = StartCoroutine(ChangeAmbience(newClip, targetVolume));
        }
    }

    private void StopFade()
    {
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }
    }

    private IEnumerator ChangeAmbience(AudioClip newClip, float targetVolume)
    {
        yield return FadeTo(0f);

        ambienceSource.Stop();
        ambienceSource.clip = newClip;
        ambienceSource.Play();

        yield return FadeTo(targetVolume);

        fadeRoutine = null;
    }

    private IEnumerator FadeOut()
    {
        yield return FadeTo(0f);

        ambienceSource.Stop();
        fadeRoutine = null;
    }

    private IEnumerator FadeToVolume(float target)
    {
        yield return FadeTo(target);
        fadeRoutine = null;
    }

    private IEnumerator FadeTo(float target)
    {
        float start = ambienceSource.volume;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(
                elapsed / Mathf.Max(0.01f, fadeDuration)
            );

            t = Mathf.SmoothStep(0f, 1f, t);

            ambienceSource.volume = Mathf.Lerp(start, target, t);

            yield return null;
        }

        ambienceSource.volume = target;
    }
}
