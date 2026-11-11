using System;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using PonyuDev.SherpaOnnx.Tts;

public class MenuNarration : MonoBehaviour
{
    [SerializeField] TtsOrchestrator speech;
    [SerializeField] Toggle spokenMenusToggle;

    const string SaveKey = "Accessibility.SpokenMenus";

    CancellationTokenSource speechCancellation;
    bool narrationEnabled;

    void Start()
    {
        // Enabled on first use so players can discover the menus.
        narrationEnabled = PlayerPrefs.GetInt(SaveKey, 1) == 1;

        spokenMenusToggle.SetIsOnWithoutNotify(narrationEnabled);
        spokenMenusToggle.onValueChanged.AddListener(SetEnabled);

        speech.Initialized += OnSpeechReady;

        if (speech.IsInitialized)
            OnSpeechReady();
    }

    void OnSpeechReady()
    {
        if (narrationEnabled)
            Speak("Spoken menus enabled.");
    }

    void SetEnabled(bool enabled)
    {
        narrationEnabled = enabled;

        PlayerPrefs.SetInt(SaveKey, enabled ? 1 : 0);
        PlayerPrefs.Save();

        // Confirm the change, including when narration is turned off.
        PlaySpeech(enabled
            ? "Spoken menus enabled."
            : "Spoken menus disabled.");
    }

    public void Speak(string text)
    {
        if (narrationEnabled)
            PlaySpeech(text);
    }

    public void DescribeSpokenMenus()
    {
        PlaySpeech(narrationEnabled
            ? "Spoken menus, on. Activate to disable."
            : "Spoken menus, off. Activate to enable.");
    }

    async void PlaySpeech(string text)
    {
        if (!speech.IsInitialized || string.IsNullOrWhiteSpace(text))
            return;

        speechCancellation?.Cancel();
        speechCancellation?.Dispose();

        speechCancellation = new CancellationTokenSource();
        CancellationToken token = speechCancellation.Token;

        try
        {
            await speech.StopAll();
            token.ThrowIfCancellationRequested();

            await speech.GenerateAndPlayWithHandleAsync(text, token);
        }
        catch (OperationCanceledException)
        {
            // A newer announcement replaced this one.
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }
    }

    void OnDestroy()
    {
        speechCancellation?.Cancel();
        speechCancellation?.Dispose();

        if (speech != null)
            speech.Initialized -= OnSpeechReady;

        if (spokenMenusToggle != null)
            spokenMenusToggle.onValueChanged.RemoveListener(SetEnabled);
    }
}