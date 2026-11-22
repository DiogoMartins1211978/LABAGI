using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Put on PausePanel. Saves without resuming or changing the active save slot.
public sealed class PauseSaveMenu : MonoBehaviour
{
    [SerializeField] Button saveButton;
    [SerializeField] TMP_Text statusLabel;
    [SerializeField] SaveGameSession session;
    [SerializeField] MenuNarration narration;

    void Awake()
    {
        if (saveButton == null || statusLabel == null)
        {
            Debug.LogError("Pause Save Menu: assign Save Button and Status Label.", this);
            enabled = false;
            return;
        }
        if (session == null) session = FindAnyObjectByType<SaveGameSession>();
        if (narration == null) narration = FindAnyObjectByType<MenuNarration>();
        saveButton.onClick.AddListener(SaveGame);
    }

    void OnEnable()
    {
        if (statusLabel == null) return;
        if (session == null) statusLabel.text = "The save system is unavailable.";
        else if (!session.CanSave(out string reason)) statusLabel.text = reason;
        else statusLabel.text = "Active save: slot " + SaveGames.ActiveSlot + ".";
    }

    public void SaveGame()
    {
        // Keep this button available so keyboard/screen-reader users can hear
        // why a save is unavailable rather than encountering an unexplained gap.
        if (!isActiveAndEnabled || !PauseMenuController.IsPaused) return;
        if (session == null) { Report("The save system is unavailable."); return; }
        if (!session.CanSave(out string reason)) { Report(reason); return; }
        if (session.TrySave(out string error))
            Report("Game saved to slot " + SaveGames.ActiveSlot + ".");
        else Report("Could not save the game: " + error);
    }

    void Report(string message)
    {
        statusLabel.text = message;
        if (narration != null) narration.Speak(message);
    }

    void OnDestroy()
    {
        if (saveButton != null) saveButton.onClick.RemoveListener(SaveGame);
    }
}
