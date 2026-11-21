using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class BookAudio : MonoBehaviour
{
    public enum Cue { Appear, Open, Leaf, Close, Disappear, Bookmark }
    [SerializeField] AudioClip appear, open, leaf, close, disappear, bookmark;
    [SerializeField, Range(0, 1)] float volume = .65f;
    AudioSource source;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0;
        source.ignoreListenerPause = true;
    }

    public void Play(Cue cue)
    {
        var clip = cue == Cue.Appear ? appear : cue == Cue.Open ? open : cue == Cue.Leaf ? leaf
            : cue == Cue.Close ? close : cue == Cue.Disappear ? disappear : bookmark;
        if (clip == null || source == null) return;
        source.PlayOneShot(clip, volume * Mathf.Clamp01(PlayerPrefs.GetFloat("Audio.MenuSfxVolume", 1)));
    }
}
