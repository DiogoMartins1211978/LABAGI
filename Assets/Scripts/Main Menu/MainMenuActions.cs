using UnityEngine;

public class MainMenuActions : MonoBehaviour
{
    public void SinglePlayer()
    {
        var menu = GetComponent<SinglePlayerMenu>();
        if (menu == null) menu = FindAnyObjectByType<SinglePlayerMenu>();
        if (menu != null) menu.Open();
        else Debug.LogError("Add and configure SinglePlayerMenu on MenuManager before opening Single Player.", this);
    }

    public void OpenMultiplayer()
    {
        var menu = GetComponent<MultiplayerMenu>();
        if (menu == null) menu = FindAnyObjectByType<MultiplayerMenu>();
        if (menu != null) menu.Open();
        else Debug.LogError("Add and configure MultiplayerMenu on MenuManager.", this);
    }

    public void Exit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
