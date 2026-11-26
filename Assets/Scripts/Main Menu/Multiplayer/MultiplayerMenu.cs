
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MultiplayerMenu : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject multiplayerPanel;
    [SerializeField] private GameObject hostGamePanel;
    [SerializeField] private GameObject joinGamePanel;

    [Header("Multiplayer Panel")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button multiplayerBackButton;

    [Header("Host GamePanel")]
    [SerializeField] private Button hostBackButton;
    [SerializeField] private Button startGameButton;
    [SerializeField] private TMP_Text sessionCodeText;
    [SerializeField] private TMP_Text playerNumberText;
    [SerializeField] private TMP_Text hostStatusText;

    [Header("Join GamePanel")]
    [SerializeField] private Button joinBackButton;
    [SerializeField] private TMP_InputField sessionCodeInput;
    [SerializeField] private Button confirmJoinButton;
    [SerializeField] private TMP_Text joinStatusText;

    private SessionManager sessionManager;

    private void Awake()
    {
        hostButton.onClick.AddListener(OpenHostPanel);
        joinButton.onClick.AddListener(OpenJoinPanel);
        multiplayerBackButton.onClick.AddListener(BackToMainMenu);

        hostBackButton.onClick.AddListener(BackToMultiplayer);
        startGameButton.onClick.AddListener(StartGame);

        joinBackButton.onClick.AddListener(BackToMultiplayer);
        confirmJoinButton.onClick.AddListener(JoinGame);
        sessionCodeInput.onValueChanged.AddListener(OnSessionCodeChanged);

        startGameButton.interactable = false;
        UpdateJoinButtonState();
    }

    private void OnEnable()
    {
        if (SessionManager.Instance != null)
        {
            sessionManager = SessionManager.Instance;
            sessionManager.PlayerCountChanged += UpdatePlayerCount;
        }
    }

    private void OnDisable()
    {
        if (sessionManager != null)
            sessionManager.PlayerCountChanged -= UpdatePlayerCount;
    }

    private void Start()
    {
        sessionManager = SessionManager.Instance;

        if (sessionManager == null)
        {
            Debug.LogError(
                "[MultiplayerMenu] SessionManager was not found in Start!"
            );
            return;
        }

        sessionManager.PlayerCountChanged += UpdatePlayerCount;

        Debug.Log("[MultiplayerMenu] SessionManager successfully connected!");
    }

    public void Open()
    {
        ShowPanel(multiplayerPanel);
    }

    private void ShowPanel(GameObject panel)
    {
        mainMenuPanel.SetActive(panel == mainMenuPanel);
        multiplayerPanel.SetActive(panel == multiplayerPanel);
        hostGamePanel.SetActive(panel == hostGamePanel);
        joinGamePanel.SetActive(panel == joinGamePanel);
    }

    private void OpenHostPanel()
    {
        ShowPanel(hostGamePanel);

        sessionCodeText.text = "Creating session...";
        playerNumberText.text = "Players: 0 / 2";
        hostStatusText.text = "Preparing host session...";

        startGameButton.interactable = false;

        CreateHost();
    }

    private async void CreateHost()
    {
        sessionManager = SessionManager.Instance;

        if (sessionManager == null)
        {
            Debug.LogError("[MultiplayerMenu] SessionManager not found!");
            return;
        }

        try
        {
            string code = await sessionManager.CreateHostSessionAsync();

            sessionCodeText.text = code;
            playerNumberText.text = "Players: 1 / 2";
            hostStatusText.text = "Waiting for another player...";

            UpdatePlayerCount(1);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            hostStatusText.text = "Could not create session: " + e.Message;
        }
    }

    private void UpdatePlayerCount(int count)
    {
        if (playerNumberText != null)
            playerNumberText.text = $"Players: {count} / 2";

        if (hostStatusText != null)
        {
            hostStatusText.text = count >= 2
                ? "Player connected! You can start the game."
                : "Waiting for another player...";
        }

        if (startGameButton != null)
            startGameButton.interactable = count >= 2;
    }

    private void StartGame()
    {
        startGameButton.interactable = false;
        hostStatusText.text = "Starting game...";

        sessionManager.StartGame();
    }

    private async void JoinGame()
    {
        sessionManager = SessionManager.Instance;

        if (sessionManager == null)
        {
            Debug.LogError("[MultiplayerMenu] SessionManager not found!");
            return;
        }

        string code = sessionCodeInput.text.Trim();

        Debug.Log($"[MultiplayerMenu] Attempting to join with code: {code}");

        if (string.IsNullOrWhiteSpace(code))
        {
            joinStatusText.text = "Please enter a session code.";
            return;
        }

        confirmJoinButton.interactable = false;
        joinStatusText.text = "Joining session...";

        try
        {
            Debug.Log("[MultiplayerMenu] Calling JoinSessionAsync...");

            await sessionManager.JoinSessionAsync(code);

            Debug.Log("[MultiplayerMenu] Successfully joined session!");
            joinStatusText.text = "Joined! Waiting for the host to start...";
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            joinStatusText.text = "Could not join: " + e.Message;
        }
        finally
        {
            confirmJoinButton.interactable = true;
        }
    }

    private void BackToMainMenu()
    {
        ShowPanel(mainMenuPanel);
    }

    private void BackToMultiplayer()
    {
        ShowPanel(multiplayerPanel);
    }

    private void OnDestroy()
    {
        if (hostButton != null)
            hostButton.onClick.RemoveListener(OpenHostPanel);

        if (joinButton != null)
            joinButton.onClick.RemoveListener(OpenJoinPanel);

        if (multiplayerBackButton != null)
            multiplayerBackButton.onClick.RemoveListener(BackToMainMenu);

        if (hostBackButton != null)
            hostBackButton.onClick.RemoveListener(BackToMultiplayer);

        if (startGameButton != null)
            startGameButton.onClick.RemoveListener(StartGame);

        if (joinBackButton != null)
            joinBackButton.onClick.RemoveListener(BackToMultiplayer);

        if (confirmJoinButton != null)
            confirmJoinButton.onClick.RemoveListener(JoinGame);

        if (sessionCodeInput != null)
            sessionCodeInput.onValueChanged.RemoveListener(OnSessionCodeChanged);
    }

    private void OpenJoinPanel()
    {
        ShowPanel(joinGamePanel);
        sessionCodeInput.text = "";
        joinStatusText.text = "Enter your friend's session code.";
        UpdateJoinButtonState();
    }

    private void OnSessionCodeChanged(string _)
    {
        UpdateJoinButtonState();
    }

    private void UpdateJoinButtonState()
    {
        if (confirmJoinButton == null || sessionCodeInput == null) return;
        confirmJoinButton.interactable = !string.IsNullOrWhiteSpace(sessionCodeInput.text);
    }

}
