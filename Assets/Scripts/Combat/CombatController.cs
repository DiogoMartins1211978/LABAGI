using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Audio;
using Unity.Netcode;

public enum CombatStart { PlayerSpotted, Ambushed }

[DefaultExecutionOrder(1000)]
public class CombatController : MonoBehaviour
{
    public static CombatController Instance { get; private set; }

    public enum State { Exploring, Combat, Returning }

    [Header("References (auto-found if empty)")]
    public PlayerNodeMover player;
    public Transform cameraTransform;
    [Tooltip("Your card system. Combat mode (hand, cards) is started/stopped through it.")]
    public CardSystem cardSystem;
    [Tooltip("ONLY your mouse-look script(s), e.g. 'Camera Controller'. NOT the Camera component itself. They get disabled during combat so the camera stays locked on the enemy.")]
    public MonoBehaviour[] cameraControlScripts;

    [Header("Stop during combat (LiDAR etc.)")]
    [Tooltip("Scripts to disable while in combat, e.g. your LiDAR scanner script on ScannerOrigin. Turned back on afterwards.")]
    public MonoBehaviour[] disableDuringCombat;
    [Tooltip("Objects to deactivate while in combat, e.g. the LiDAR points / VFX object. Use this if the effect keeps running after the script is disabled, because it also stops coroutines and particles. Reactivated afterwards.")]
    public GameObject[] deactivateDuringCombat;

    [Header("Encounter rules")]
    [Tooltip("Spot enemies automatically when they're inside the camera's view cone. Turn off if your LiDAR scan calls NotifyEnemySpotted() instead.")]
    public bool useViewConeDetection = true;
    [Tooltip("If the player tries to leave a room with an unresolved enemy, the move is blocked and the enemy ambushes them immediately.")]
    private bool lockMovementInEnemyRooms = true;
    public float focusTurnDuration = 0.6f;
    public float releaseTurnDuration = 0.5f;

    [Header("Combat Camera")]
    [SerializeField] private Vector3 focusOffset = new Vector3(0f, 0.3f, 0f);
    [SerializeField] private float combatCameraTurnSpeed = 8f;

    [Header("Combat Audio")]
    [SerializeField] private AudioClip ambushStartSound;
    [SerializeField] private AudioMixerGroup combatAudioGroup;
    [SerializeField, Range(0f, 1f)] private float ambushStartVolume = 1f;

    private AudioSource combatAudioSource;

    public State CurrentState { get; private set; } = State.Exploring;
    public bool InCombat => CurrentState != State.Exploring;
    public Enemy ActiveEnemy => activeEnemy;

    public event Action<Enemy, CombatStart> CombatStarted;
    public event Action<Enemy, bool> CombatEnded;

    private Enemy activeEnemy;
    private bool trackEnemy;
    private Quaternion savedCameraRotation;

    [SerializeField] private PlayerHealth playerHealth;
    bool trackedHeadset;

    private NetworkCombatManager networkCombatManager;

    private bool IsMultiplayerSession => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    public void ConfigureTrackedHeadset(PlayerNodeMover mover, Transform head, CardSystem cards, PlayerHealth health)
    {
        player = mover;
        cameraTransform = head;
        cardSystem = cards;
        playerHealth = health;
        trackedHeadset = true;
        cameraControlScripts = Array.Empty<MonoBehaviour>();
        // Keep the room's LiDAR visible so looking around remains meaningful.
        disableDuringCombat = Array.Empty<MonoBehaviour>();
        deactivateDuringCombat = Array.Empty<GameObject>();
    }

    void Awake()
    {
        Instance = this;

        combatAudioSource = gameObject.AddComponent<AudioSource>();
        combatAudioSource.playOnAwake = false;
        combatAudioSource.spatialBlend = 0f;
        combatAudioSource.outputAudioMixerGroup = combatAudioGroup;
    }

    void Start()
    {
        if (player == null) player = FindAnyObjectByType<PlayerNodeMover>();
        if (player == null)
        {
            Debug.LogWarning("[CombatController] No PlayerNodeMover found.");
            enabled = false;
            return;
        }

        if (cameraTransform == null) cameraTransform = FindCameraTransform();
        Debug.Log($"[CombatController] Using '{cameraTransform.name}' as the combat camera.");

        if (cardSystem == null) cardSystem = FindAnyObjectByType<CardSystem>();
        if (cardSystem == null)
            Debug.LogWarning("[CombatController] No CardSystem found, so no cards will show in combat.");

        if (cardSystem != null)
            cardSystem.TurnEnded += HandlePlayerTurnEnded;

        if (IsMultiplayerSession)
        {
            networkCombatManager = FindAnyObjectByType<NetworkCombatManager>();

            if (networkCombatManager != null)
            {
                networkCombatManager.MultiplayerCombatStarted += HandleMultiplayerCombatStarted;
                StartCoroutine(ReportInitialRoomWhenReady());
                Debug.Log("[CombatController] Connected to NetworkCombatManager.");
            }
            else
                Debug.LogError("[CombatController] NetworkCombatManager not found!");

        }

        player.RoomEntered += HandleRoomEntered;
        player.LeaveBlocked += HandleLeaveBlocked;

        if (!trackedHeadset && !HasUsableLookScript())
        {
            cameraControlScripts = AutoDetectLookScripts();
            if (cameraControlScripts.Length > 0)
                Debug.Log($"[CombatController] No camera control scripts assigned, auto-detected {cameraControlScripts.Length} on '{cameraTransform.name}' (e.g. '{cameraControlScripts[0].GetType().Name}').");
            else
                Debug.LogWarning("[CombatController] No camera control scripts assigned or found, so a look script may fight the combat camera.");
        }

        if (player.CurrentRoom != null) HandleRoomEntered(player.CurrentRoom);
    }

    void OnDestroy()
    {
        if (player != null)
        {
            player.RoomEntered -= HandleRoomEntered;
            player.LeaveBlocked -= HandleLeaveBlocked;
        }

        if (networkCombatManager != null)
            networkCombatManager.MultiplayerCombatStarted -= HandleMultiplayerCombatStarted;

        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (PauseMenuController.BlocksGameplayInput)
            return;

        if (CurrentState != State.Exploring || activeEnemy == null)
            return;

        if (player.IsMoving)
            return;
    }

    void LateUpdate()
    {
        if (trackedHeadset) return;
        if (CurrentState != State.Combat || !trackEnemy || activeEnemy == null)
            return;

        if (PauseMenuController.BlocksGameplayInput)
            return;

        Quaternion targetRotation = FocusRotation(activeEnemy);

        cameraTransform.rotation = Quaternion.Slerp(
            cameraTransform.rotation,
            targetRotation,
            combatCameraTurnSpeed * Time.deltaTime
        );
    }

    void HandleRoomEntered(RoomNode room)
    {
        if (IsMultiplayerSession && networkCombatManager != null)
        {
            DungeonGenerator generator = FindAnyObjectByType<DungeonGenerator>();

            if (generator != null)
            {
                string roomId = generator.GetRoomId(room);
                if (!string.IsNullOrEmpty(roomId)) networkCombatManager.ReportCurrentRoom(roomId);
            }
        }

        player.BlockLeaving = false;
        if (CurrentState != State.Exploring) return;

        activeEnemy = null;

        Enemy enemy = room != null ? room.enemy : null;
        if (enemy == null || enemy.Resolved || !enemy.isActiveAndEnabled) return;
        activeEnemy = enemy;
        enemy.StartPresenceAudio();
        activeEnemy.Room = room;

        player.BlockLeaving = lockMovementInEnemyRooms;
        Debug.Log($"[CombatController] Enemy room detected. " + $"Enemy: {activeEnemy.name} | " + $"BlockLeaving: {player.BlockLeaving}");
    }

    void HandleLeaveBlocked(RoomNode attemptedTarget)
    {
        Debug.Log($"[CombatController] LeaveBlocked received. Active enemy: {(activeEnemy != null ? activeEnemy.name : "NULL")}");
        if (CurrentState != State.Exploring || activeEnemy == null) return;

        if (IsMultiplayerSession)
        {
            RequestMultiplayerCombatStart();
            return;
        }

        BeginCombat(CombatStart.Ambushed);
    }

    public void NotifyEnemySpotted(Enemy enemy)
    {
        Debug.Log(
            $"[CombatController] NotifyEnemySpotted received. " +
            $"Enemy: {(enemy != null ? enemy.name : "NULL")} | " +
            $"ActiveEnemy: {(activeEnemy != null ? activeEnemy.name : "NULL")} | " +
            $"State: {CurrentState} | " +
            $"Paused: {PauseMenuController.BlocksGameplayInput}"
        );

        if (PauseMenuController.BlocksGameplayInput)
        {
            Debug.LogWarning("[CombatController] Cannot start combat: gameplay input is blocked.");
            return;
        }

        if (CurrentState != State.Exploring)
        {
            if (!IsMultiplayerSession) Debug.LogWarning("[CombatController] Cannot start combat: not exploring.");
            return;
        }

        if (enemy == null || enemy != activeEnemy)
        {
            Debug.LogWarning("[CombatController] Cannot start combat: discovered enemy is not the active enemy.");
            return;
        }

        if (!enemy.Discovered)
        {
            Debug.LogWarning("[CombatController] Cannot start combat: enemy is not discovered.");
            return;
        }

        if (player.CurrentRoom != activeEnemy.Room)
        {
            Debug.LogWarning(
                $"[CombatController] Cannot start combat: room mismatch. " +
                $"Player: {player.CurrentRoom?.name ?? "NULL"} | " +
                $"Enemy: {activeEnemy.Room?.name ?? "NULL"}"
            );
            return;
        }

        Debug.Log($"[CombatController] Starting combat with discovered enemy: {enemy.name}");

        if (IsMultiplayerSession)
        {
            RequestMultiplayerCombatStart();
            return;
        }

        BeginCombat(CombatStart.PlayerSpotted);
    }

    private void RequestMultiplayerCombatStart()
    {
        if (networkCombatManager == null || player.CurrentRoom == null)
            return;

        DungeonGenerator generator = FindAnyObjectByType<DungeonGenerator>();

        if (generator == null)
            return;

        string roomId = generator.GetRoomId(player.CurrentRoom);

        if (string.IsNullOrEmpty(roomId))
            return;

        Debug.Log(
            $"[CombatController] Requesting multiplayer combat in room '{roomId}'."
        );

        networkCombatManager.RequestCombatStart(roomId);
    }

    private void HandleMultiplayerCombatStarted(string roomId)
    {
        if (!IsMultiplayerSession || CurrentState != State.Exploring)
            return;

        DungeonGenerator generator = FindAnyObjectByType<DungeonGenerator>();

        if (generator == null || player.CurrentRoom == null)
            return;

        string currentRoomId = generator.GetRoomId(player.CurrentRoom);

        if (currentRoomId != roomId)
            return;

        // Get the locally replicated enemy in this room.
        activeEnemy = player.CurrentRoom.enemy;

        if (activeEnemy == null || activeEnemy.Resolved)
        {
            Debug.LogError(
                $"[CombatController] Cannot start multiplayer combat: " +
                $"no valid enemy in room '{roomId}'."
            );
            return;
        }

        BeginMultiplayerCombat();
    }

    private void BeginMultiplayerCombat()
    {
        Debug.Log(
            $"[CombatController] Starting synchronised multiplayer combat " +
            $"with {activeEnemy.name}."
        );

        CurrentState = State.Combat;
        trackEnemy = false;

        player.BlockLeaving = false;
        player.SetInputLocked(true);

        SetCameraControlEnabled(false);
        StopCombatSuppressedThings();

        savedCameraRotation = cameraTransform.rotation;

        CombatStarted?.Invoke(activeEnemy, CombatStart.PlayerSpotted);

        cardSystem?.SetActiveEnemy(activeEnemy);

        StartCoroutine(MultiplayerCombatIntro(activeEnemy));
    }

    private IEnumerator MultiplayerCombatIntro(Enemy enemy)
    {
        yield return TurnCameraTo(FocusRotation(enemy), focusTurnDuration);

        trackEnemy = true;
    }

    void BeginCombat(CombatStart how)
    {
        activeEnemy.SetCombatAmbience(true);

        CurrentState = State.Combat;

        if (how == CombatStart.Ambushed)
            PlayAmbushStartSound();

        trackEnemy = false;

        player.BlockLeaving = false;
        player.SetInputLocked(true);
        SetCameraControlEnabled(false);
        StopCombatSuppressedThings();

        savedCameraRotation = cameraTransform.rotation;

        CombatStarted?.Invoke(activeEnemy, how);
        StartCoroutine(CombatIntro(activeEnemy, how));
        cardSystem.SetActiveEnemy(activeEnemy);
    }

    IEnumerator CombatIntro(Enemy enemy, CombatStart how)
    {
        yield return TurnCameraTo(FocusRotation(enemy), focusTurnDuration);
        trackEnemy = true;

        if (how == CombatStart.Ambushed)
        {
            enemy.OnSurpriseAttack();

            enemy.AnnounceNextAttack();

            bool attackFinished = false;

            enemy.Attack(() =>
            {
                attackFinished = true;
            });

            yield return new WaitUntil(() => attackFinished);

            enemy.AnnounceNextAttack();
        }
        else
        {
            enemy.OnSpotted();

            enemy.AnnounceNextAttack();
        }

        if (cardSystem != null)
            cardSystem.EnterCombat();
    }

    private void HandlePlayerTurnEnded()
    {
        if (CurrentState != State.Combat)
            return;

        StartCoroutine(EnemyTurnRoutine());
    }

    private IEnumerator EnemyTurnRoutine()
    {
        yield return new WaitForSeconds(0.5f);

        if (activeEnemy == null || activeEnemy.Resolved)
        {
            EndCombat(activeEnemy != null && activeEnemy.Resolved);
            yield break;
        }
        activeEnemy.ProcessDamageOverTime();
        if (activeEnemy.Resolved) { EndCombat(); yield break; }

        CardData currentCue = activeEnemy.CurrentAttackCue;
        activeEnemy.Attack(OnEnemyAttackFinished);
        if (currentCue != null && currentCue.type == CardType.Attack)
            playerHealth.TakeDamage(currentCue.effectValue);
    }

    private void OnEnemyAttackFinished()
    {
        Debug.Log("[Combat] Enemy attack finished.");

        if (CurrentState != State.Combat)
            return;

        if (activeEnemy == null || activeEnemy.Resolved)
        {
            EndCombat(activeEnemy != null && activeEnemy.Resolved);
            return;
        }

        activeEnemy.AnnounceNextAttack();

        Debug.Log("[Combat] Starting player turn.");

        cardSystem.StartPlayerTurn();
    }

    public void EndCombat(bool enemyDefeated = true)
    {
        if (CurrentState != State.Combat) return;
        StartCoroutine(EndRoutine(enemyDefeated));
    }

    IEnumerator EndRoutine(bool defeated)
    {
        CurrentState = State.Returning;
        trackEnemy = false;

        Enemy enemy = activeEnemy;
        if (defeated && enemy != null) enemy.Resolve();

        yield return TurnCameraTo(savedCameraRotation, releaseTurnDuration);

        SetCameraControlEnabled(true);
        RestoreCombatSuppressedThings();

        if (cardSystem != null && cardSystem.CurrentMode == CardSystem.Mode.Combat)
            cardSystem.EnterExploration();

        if (enemy != null)
            enemy.SetCombatAmbience(false);

        activeEnemy = null;
        player.SetInputLocked(false);
        CurrentState = State.Exploring;

        CombatEnded?.Invoke(enemy, defeated);
    }

    private readonly System.Collections.Generic.List<MonoBehaviour> stoppedScripts = new System.Collections.Generic.List<MonoBehaviour>();
    private readonly System.Collections.Generic.List<GameObject> stoppedObjects = new System.Collections.Generic.List<GameObject>();

    public void StopCombatSuppressedThings()
    {
        stoppedScripts.Clear();
        stoppedObjects.Clear();

        if (disableDuringCombat != null)
        {
            foreach (MonoBehaviour script in disableDuringCombat)
            {
                if (script == null || script == this) continue;
                if (script.GetType().Name == "UniversalAdditionalCameraData") continue;
                if (!script.enabled) continue;

                script.enabled = false;
                stoppedScripts.Add(script);
            }
        }

        if (deactivateDuringCombat != null)
        {
            foreach (GameObject go in deactivateDuringCombat)
            {
                if (go == null || !go.activeSelf) continue;
                if (go == player.gameObject || cameraTransform.IsChildOf(go.transform)) continue;

                go.SetActive(false);
                stoppedObjects.Add(go);
            }
        }
    }

    public void RestoreCombatSuppressedThings()
    {
        foreach (MonoBehaviour script in stoppedScripts)
            if (script != null) script.enabled = true;

        foreach (GameObject go in stoppedObjects)
            if (go != null) go.SetActive(true);

        stoppedScripts.Clear();
        stoppedObjects.Clear();
    }

    public void SetCameraControlEnabled(bool value)
    {
        if (trackedHeadset) return;
        if (cameraControlScripts == null) return;

        var names = new System.Text.StringBuilder();
        foreach (MonoBehaviour script in cameraControlScripts)
        {
            if (script == null) continue;
            if (script.GetType().Name == "UniversalAdditionalCameraData") continue;
            script.enabled = value;
            names.Append(script.GetType().Name).Append(' ');
        }

        if (!value)
            Debug.Log($"[CombatController] Look scripts disabled for combat: {(names.Length > 0 ? names.ToString() : "NONE")}");
    }

    bool HasUsableLookScript()
    {
        if (cameraControlScripts == null) return false;

        foreach (MonoBehaviour script in cameraControlScripts)
        {
            if (script == null) continue;
            if (script.GetType().Name == "UniversalAdditionalCameraData") continue;
            return true;
        }
        return false;
    }

    Transform FindCameraTransform()
    {
        if (Camera.main != null) return Camera.main.transform;

        Debug.LogWarning("[CombatController] No camera tagged 'MainCamera'. Searching for one (tag it MainCamera to be safe).");

        Camera cam = player.GetComponentInChildren<Camera>(true);
        if (cam == null) cam = FindAnyObjectByType<Camera>();
        return cam != null ? cam.transform : player.transform;
    }

    MonoBehaviour[] AutoDetectLookScripts()
    {
        var found = new System.Collections.Generic.List<MonoBehaviour>();

        foreach (MonoBehaviour mb in cameraTransform.GetComponents<MonoBehaviour>())
        {
            if (mb == null || mb == this) continue;

            string typeName = mb.GetType().Name;
            if (typeName == "UniversalAdditionalCameraData") continue;

            string lower = typeName.ToLower();
            if (lower.Contains("look") || lower.Contains("controller")) found.Add(mb);
        }

        return found.ToArray();
    }

    Quaternion FocusRotation(Enemy enemy)
    {
        Vector3 targetPosition = enemy.FocusPosition + focusOffset;
        Vector3 direction = targetPosition - cameraTransform.position;

        if (direction.sqrMagnitude < 0.0001f)
            return cameraTransform.rotation;

        return Quaternion.LookRotation(direction, Vector3.up);
    }

    public IEnumerator TurnCameraTo(Quaternion target, float duration)
    {
        // XR owns the tracked pose throughout combat, including intro/exit.
        if (trackedHeadset) yield break;
        Quaternion start = cameraTransform.rotation;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, duration);
            cameraTransform.rotation = Quaternion.Slerp(start, target, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        cameraTransform.rotation = target;
    }

    private void PlayAmbushStartSound()
    {
        if (ambushStartSound == null) return;

        combatAudioSource.PlayOneShot(ambushStartSound, ambushStartVolume);
    }

    private IEnumerator ReportInitialRoomWhenReady()
    {
        while (networkCombatManager == null || !networkCombatManager.IsSpawned)
        {
            yield return null;
            if (networkCombatManager == null) networkCombatManager = FindAnyObjectByType<NetworkCombatManager>();
        }

        yield return null;

        if (player != null && player.CurrentRoom != null)
        {
            DungeonGenerator generator = FindAnyObjectByType<DungeonGenerator>();

            if (generator != null)
            {
                string roomId = generator.GetRoomId(player.CurrentRoom);
                networkCombatManager.ReportCurrentRoom(roomId);
                Debug.Log($"[CombatController] Initial network room reported: {roomId}");
            }
        }
    }
}
