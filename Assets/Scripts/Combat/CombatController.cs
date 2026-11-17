using System;
using System.Collections;
using UnityEngine;

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
    public bool lockMovementInEnemyRooms = true;
    public float focusTurnDuration = 0.6f;
    public float releaseTurnDuration = 0.5f;

    public State CurrentState { get; private set; } = State.Exploring;
    public bool InCombat => CurrentState != State.Exploring;
    public Enemy ActiveEnemy => activeEnemy;

    public event Action<Enemy, CombatStart> CombatStarted;
    public event Action<Enemy, bool> CombatEnded;

    private Enemy activeEnemy;
    private bool trackEnemy;
    private Quaternion savedCameraRotation;

    void Awake()
    {
        Instance = this;
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

        player.RoomEntered += HandleRoomEntered;
        player.LeaveBlocked += HandleLeaveBlocked;

        if (!HasUsableLookScript())
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
        if (Instance == this) Instance = null;
    }

    void HandleRoomEntered(RoomNode room)
    {
        player.BlockLeaving = false;
        if (CurrentState != State.Exploring) return;

        activeEnemy = null;

        Enemy enemy = room != null ? room.enemy : null;
        if (enemy == null || enemy.Resolved || !enemy.isActiveAndEnabled) return;

        activeEnemy = enemy;
        player.BlockLeaving = lockMovementInEnemyRooms;
    }

    void HandleLeaveBlocked(RoomNode attemptedTarget)
    {
        if (CurrentState == State.Exploring && activeEnemy != null)
            BeginCombat(CombatStart.Ambushed);
    }

    void Update()
    {
        if (PauseMenuController.BlocksGameplayInput) return;
        if (CurrentState != State.Exploring || activeEnemy == null) return;
        if (player.IsMoving || player.CurrentRoom != activeEnemy.Room) return;

        if (useViewConeDetection && activeEnemy.IsSeenBy(cameraTransform))
            BeginCombat(CombatStart.PlayerSpotted);
    }

    void LateUpdate()
    {
        if (PauseMenuController.BlocksGameplayInput) return;
        if (CurrentState == State.Combat && trackEnemy && activeEnemy != null)
            cameraTransform.rotation = FocusRotation(activeEnemy);
    }

    public void NotifyEnemySpotted(Enemy enemy)
    {
        if (PauseMenuController.BlocksGameplayInput) return;
        if (CurrentState == State.Exploring && enemy != null && enemy == activeEnemy)
            BeginCombat(CombatStart.PlayerSpotted);
    }

    void BeginCombat(CombatStart how)
    {
        CurrentState = State.Combat;
        trackEnemy = false;

        player.BlockLeaving = false;
        player.SetInputLocked(true);
        SetCameraControlEnabled(false);
        StopCombatSuppressedThings();

        savedCameraRotation = cameraTransform.rotation;

        CombatStarted?.Invoke(activeEnemy, how);
        StartCoroutine(CombatIntro(activeEnemy, how));
    }

    IEnumerator CombatIntro(Enemy enemy, CombatStart how)
    {
        yield return TurnCameraTo(FocusRotation(enemy), focusTurnDuration);
        trackEnemy = true;

        if (how == CombatStart.Ambushed) enemy.OnSurpriseAttack();
        else enemy.OnSpotted();

        if (cardSystem != null) cardSystem.EnterCombat();
    }

    private void HandlePlayerTurnEnded()
    {
        if (CurrentState != State.Combat)
            return;

        StartCoroutine(EnemyTurnRoutine());
    }

    private IEnumerator EnemyTurnRoutine()
    {
        Debug.Log("[Combat] Enemy turn started.");

        yield return new WaitForSeconds(0.5f);

        if (activeEnemy == null || activeEnemy.Resolved)
            yield break;

        activeEnemy.Attack(OnEnemyAttackFinished);
    }

    private void OnEnemyAttackFinished()
    {
        Debug.Log("[Combat] Enemy attack finished. Starting player turn.");

        if (CurrentState != State.Combat)
            return;

        cardSystem.StartPlayerTurn();
    }

    public void EndCombat(bool enemyDefeated = true)
    {
        if (CurrentState != State.Combat) return;
        StartCoroutine(EndRoutine(enemyDefeated));
    }

    [ContextMenu("Debug: End Combat")]
    void DebugEndCombat() => EndCombat(true);

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

        activeEnemy = null; 
        player.SetInputLocked(false);
        CurrentState = State.Exploring;

        CombatEnded?.Invoke(enemy, defeated);
    }

    private readonly System.Collections.Generic.List<MonoBehaviour> stoppedScripts = new System.Collections.Generic.List<MonoBehaviour>();
    private readonly System.Collections.Generic.List<GameObject> stoppedObjects = new System.Collections.Generic.List<GameObject>();

    void StopCombatSuppressedThings()
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

    void RestoreCombatSuppressedThings()
    {
        foreach (MonoBehaviour script in stoppedScripts)
            if (script != null) script.enabled = true;

        foreach (GameObject go in stoppedObjects)
            if (go != null) go.SetActive(true);

        stoppedScripts.Clear();
        stoppedObjects.Clear();
    }

    void SetCameraControlEnabled(bool value)
    {
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
        Vector3 dir = enemy.FocusPosition - cameraTransform.position;
        return dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir) : cameraTransform.rotation;
    }

    IEnumerator TurnCameraTo(Quaternion target, float duration)
    {
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
}
