using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Put on a separate root GameObject in each scene that can be started directly.
public class MenuSfx : MonoBehaviour
{
    [SerializeField] AudioClip focusClip;
    [SerializeField] AudioClip confirmClip;
    [SerializeField] AudioClip backClip;
    [SerializeField] AudioMixerGroup outputGroup;
    [SerializeField, Range(0f, 1f)] float volume = 0.35f;

    static MenuSfx instance;
    static float customSoundUntil;
    static int customSoundFrame = -1;
    static AudioSource customSoundSource;
    AudioSource source;
    GameObject lastSelection;
    Selectable lastFocus;
    float lastFocusTime = -1f;
    float nextScan;
    int actionFrame = -1;
    float userVolume;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCustomSoundPriority()
    {
        customSoundUntil = 0;
        customSoundFrame = -1;
        customSoundSource = null;
    }

    static bool CustomSoundHasPriority => customSoundFrame == Time.frameCount
        || (customSoundSource != null && customSoundSource.isActiveAndEnabled
            && (customSoundSource.isPlaying || Time.unscaledTime < customSoundUntil));

    // Custom presentation audio replaces menu ticks, including ticks that may
    // already have run earlier in the same button's callback list.
    public static void OverrideWithCustomSound(AudioSource customSource, float duration)
    {
        customSoundSource = customSource;
        customSoundUntil = Mathf.Max(customSoundUntil, Time.unscaledTime + Mathf.Max(0, duration));
        customSoundFrame = Time.frameCount;
        if (instance != null && instance.source != null) instance.source.Stop();
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        userVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("Audio.MenuSfxVolume", 1f));
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.ignoreListenerPause = true;
        source.outputAudioMixerGroup = outputGroup;
        SceneManager.sceneLoaded += OnSceneLoaded;
        ScanControls();
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        lastSelection = null;
        lastFocus = null;
        ScanControls();
    }

    void LateUpdate()
    {
        // Includes controls created when dropdowns or the bestiary open.
        if (Time.unscaledTime >= nextScan)
        {
            ScanControls();
            nextScan = Time.unscaledTime + 0.25f;
        }
        var selected = EventSystem.current != null
            ? EventSystem.current.currentSelectedGameObject : null;
        if (selected == lastSelection) return;
        lastSelection = selected;
        if (selected != null && selected.GetComponent<MenuSfxControl>() != null)
            Focus(selected.GetComponent<Selectable>());
    }

    void ScanControls()
    {
        foreach (var control in FindObjectsByType<Selectable>(FindObjectsInactive.Include))
            if (control.GetComponentInParent<Canvas>(true) != null
                && control.GetComponent<MenuSfxControl>() == null)
                control.gameObject.AddComponent<MenuSfxControl>();
    }

    public static void Focus(Selectable control)
    {
        if (instance == null || control == null || !control.IsActive() || !control.IsInteractable()
            || instance.actionFrame == Time.frameCount || CustomSoundHasPriority) return;
        if (control.TryGetComponent<MenuSfxControl>(out var sound) && sound.IsSilent && !sound.AllowFocusWhenSilent) return;
        if (instance.lastFocus == control && Time.unscaledTime - instance.lastFocusTime < 0.1f) return;
        instance.lastFocus = control;
        instance.lastFocusTime = Time.unscaledTime;
        instance.Play(instance.focusClip);
    }

    public static void Confirm() => Action(false);
    public static void Back() => Action(true);

    public static void SetVolume(float value)
    {
        value = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat("Audio.MenuSfxVolume", value);
        if (instance != null) instance.userVolume = value;
    }

    static void Action(bool back)
    {
        if (instance == null || instance.actionFrame == Time.frameCount || CustomSoundHasPriority) return;
        instance.actionFrame = Time.frameCount;
        // Replace a focus tick rather than layering both sounds on one click.
        instance.source.Stop();
        instance.Play(back ? instance.backClip : instance.confirmClip);
    }

    void Play(AudioClip clip)
    {
        if (clip != null) source.PlayOneShot(clip, volume * userVolume);
    }

    void OnDestroy()
    {
        if (instance != this) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        instance = null;
    }
}
