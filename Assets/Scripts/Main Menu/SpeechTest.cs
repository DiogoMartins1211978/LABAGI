using UnityEngine;
using PonyuDev.SherpaOnnx.Tts;

public class SpeechTest : MonoBehaviour
{
    [SerializeField] TtsOrchestrator speech;

    void Start()
    {
        if (speech == null)
        {
            Debug.LogError("Assign the Tts Orchestrator.");
            return;
        }

        if (speech.IsInitialized)
            Speak();
        else
            speech.Initialized += Speak;
    }

    void Speak()
    {
        speech.Initialized -= Speak;
        speech.GenerateAndPlay(
            "You a lil' bitch"
        );
    }

    void OnDestroy()
    {
        if (speech != null)
            speech.Initialized -= Speak;
    }
}