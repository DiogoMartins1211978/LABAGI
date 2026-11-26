
using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;

public class SessionManager : MonoBehaviour
{
    public static SessionManager Instance { get; private set; }

    public bool IsInitialized { get; private set; }
    public bool IsAuthenticated { get; private set; }

    public string PlayerId =>
        IsAuthenticated ? AuthenticationService.Instance.PlayerId : null;

    public string CurrentJoinCode =>
        currentSession != null ? currentSession.Code : null;

    public event Action<int> PlayerCountChanged;

    [SerializeField] private string gameplayScene = "Test Scene";

    private Task initializationTask;
    private ISession currentSession;
    private bool isLoadingGameplay;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        initializationTask = InitializeServices();
    }

    private async Task InitializeServices()
    {
        try
        {
            Debug.Log("[SessionManager] Initializing Unity Services...");

            await UnityServices.InitializeAsync();
            IsInitialized = true;

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            IsAuthenticated = AuthenticationService.Instance.IsSignedIn;

            Debug.Log($"[SessionManager] Signed in! Player ID: {PlayerId}");
        }
        catch (Exception e)
        {
            IsInitialized = false;
            IsAuthenticated = false;
            Debug.LogError($"[SessionManager] Initialization failed: {e}");
        }
    }

    private async Task EnsureReady()
    {
        if (initializationTask != null)
            await initializationTask;

        if (!IsInitialized || !IsAuthenticated)
            throw new InvalidOperationException(
                "Unity Services or Authentication is not ready.");
    }

    private void PrepareNetworkManager()
    {
        if (NetworkManager.Singleton == null)
            throw new InvalidOperationException(
                "No NetworkManager found. Make sure it exists in the Main Menu scene.");

        // Keep the NetworkManager alive when NGO changes scenes.
        DontDestroyOnLoad(NetworkManager.Singleton.gameObject);
    }

    public async Task<string> CreateHostSessionAsync()
    {
        await EnsureReady();
        PrepareNetworkManager();

        if (currentSession != null)
            throw new InvalidOperationException(
                "You are already in a multiplayer session.");

        Debug.Log("[SessionManager] Creating host session...");

        var options = new SessionOptions
        {
            MaxPlayers = 2
        }.WithRelayNetwork();

        currentSession = await MultiplayerService.Instance.CreateSessionAsync(options);
        currentSession.PlayerJoined += OnPlayerJoined;
        isLoadingGameplay = false;
        PlayerCountChanged?.Invoke(currentSession.PlayerCount);
        return currentSession.Code;
    }

    public async Task JoinSessionAsync(string joinCode)
    {
        await EnsureReady();
        PrepareNetworkManager();

        if (currentSession != null)
            throw new InvalidOperationException(
                "You are already in a multiplayer session.");

        if (string.IsNullOrWhiteSpace(joinCode))
            throw new ArgumentException("Please enter a join code.");

        Debug.Log($"[SessionManager] Joining session with code: {joinCode}");

        currentSession =
            await MultiplayerService.Instance.JoinSessionByCodeAsync(
                joinCode.Trim().ToUpperInvariant());

        Debug.Log($"[SessionManager] Joined session: {currentSession.Id}");
        Debug.Log("[SessionManager] Waiting for the host to start the game...");
    }

    private void OnPlayerJoined(string playerId)
    {
        Debug.Log($"[SessionManager] Player joined: {playerId}");

        if (currentSession == null || !currentSession.IsHost)
            return;

        Debug.Log($"[SessionManager] Players in session: {currentSession.PlayerCount}");

        PlayerCountChanged?.Invoke(currentSession.PlayerCount);
    }

    //private void LoadGameplayScene()
    //{
    //    if (isLoadingGameplay)
    //        return;

    //    if (NetworkManager.Singleton == null)
    //    {
    //        Debug.LogError("[SessionManager] NetworkManager is missing.");
    //        return;
    //    }

    //    if (NetworkManager.Singleton.SceneManager == null)
    //    {
    //        Debug.LogError(
    //            "[SessionManager] NGO Scene Management is unavailable. Check NetworkManager settings.");
    //        return;
    //    }

    //    isLoadingGameplay = true;

    //    Debug.Log($"[SessionManager] Both players connected. Loading {gameplayScene}...");

    //    var status = NetworkManager.Singleton.SceneManager.LoadScene(
    //        gameplayScene,
    //        LoadSceneMode.Single);

    //    if (status != SceneEventProgressStatus.Started)
    //    {
    //        isLoadingGameplay = false;
    //        Debug.LogError($"[SessionManager] Scene load failed to start: {status}");
    //    }
    //}

    public void StartGame()
    {
        if (currentSession == null || !currentSession.IsHost)
        {
            Debug.LogWarning("[SessionManager] Only the host can start the game.");
            return;
        }

        if (currentSession.PlayerCount < 2)
        {
            Debug.LogWarning("[SessionManager] Waiting for another player.");
            return;
        }

        if (isLoadingGameplay)
            return;

        if (NetworkManager.Singleton == null ||
            NetworkManager.Singleton.SceneManager == null)
        {
            Debug.LogError("[SessionManager] Network scene management is unavailable.");
            return;
        }

        isLoadingGameplay = true;

        var status = NetworkManager.Singleton.SceneManager.LoadScene(
            gameplayScene,
            LoadSceneMode.Single);

        if (status != SceneEventProgressStatus.Started)
        {
            isLoadingGameplay = false;
            Debug.LogError($"[SessionManager] Scene load failed: {status}");
        }
    }
}
