using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public class AudioSettings : MonoBehaviour
{
    [SerializeField] AudioMixer mixer;

    [SerializeField] Slider masterSlider;
    [SerializeField] Slider playerVoiceSlider;
    [SerializeField] Slider ambientSlider;
    [SerializeField] Slider enemiesSlider;
    [SerializeField] Slider cardsSlider;
    [SerializeField] Slider menuSfxSlider;

    void Start()
    {
        Connect(masterSlider, "MasterVolume");
        Connect(playerVoiceSlider, "PlayerVoiceVolume");
        Connect(ambientSlider, "AmbientVolume");
        Connect(enemiesSlider, "EnemiesVolume");
        Connect(cardsSlider, "CardsVolume");
        if (menuSfxSlider != null)
        {
            menuSfxSlider.SetValueWithoutNotify(Mathf.Clamp01(PlayerPrefs.GetFloat("Audio.MenuSfxVolume", 1f)));
            menuSfxSlider.onValueChanged.AddListener(MenuSfx.SetVolume);
        }
    }

    void Connect(Slider slider, string parameter)
    {
        string key = "Audio." + parameter;
        float value = Mathf.Clamp01(PlayerPrefs.GetFloat(key, 1f));

        slider.SetValueWithoutNotify(value);
        ApplyVolume(parameter, value);

        slider.onValueChanged.AddListener(newValue =>
        {
            ApplyVolume(parameter, newValue);
            PlayerPrefs.SetFloat(key, newValue);
        });
    }

    void ApplyVolume(string parameter, float value)
    {
        float decibels = value <= 0f
            ? -80f
            : Mathf.Log10(value) * 20f;

        mixer.SetFloat(parameter, decibels);
    }

    void OnApplicationPause(bool paused)
    {
        if (paused)
            PlayerPrefs.Save();
    }

    void OnDisable()
    {
        PlayerPrefs.Save();
    }

    void OnDestroy()
    {
        if (menuSfxSlider != null) menuSfxSlider.onValueChanged.RemoveListener(MenuSfx.SetVolume);
    }
}
