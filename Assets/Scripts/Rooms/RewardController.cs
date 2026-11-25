
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Tools.Content;

public class RewardController : MonoBehaviour
{
    private enum RewardState
    {
        Exploring,
        Focusing,
        Opening,
        Selecting,
        Returning
    }

    private enum RewardChoice
    {
        Card,
        Heart
    }

    [Header("References")]
    public CombatController combatController;
    public PlayerNodeMover player;
    [SerializeField] private PlayerHealth playerHealth;
    public CardSystem cardSystem;
    public Camera mainCamera;

    [Header("Chest Detection")]
    public float gazeRequiredDuration = 1f;
    public LayerMask detectionMask = ~0;

    [Header("Camera")]
    public float focusAimHeight = 0.65f;

    [Header("Reward Presentation")]
    [Tooltip("Maximum vertical distance above the reward spawn point.")]
    [Range(0f, 1f)]
    public float rewardHeight = 0.5f;

    public float rewardSeparation = 1f;
    public float rewardRiseDuration = 0.7f;

    [Tooltip("Small gap between the chest opening and the bottom of each reward.")]
    public float rewardClearance = 0.12f;

    [Tooltip("How far below the final position the rewards start.")]
    public float rewardRiseStartOffset = 0.35f;


    public float selectedScaleMultiplier = 1.15f;
    public float selectionAnimationSpeed = 8f;

    [Header("Selection")]
    public string previousAction = "Previous";
    public string nextAction = "Next";
    public string interactAction = "Interact";
    public float mouseSelectionThreshold = 15f;

    [Header("Highlight")]
    public Color highlightColor = Color.yellow;

    private RewardState state = RewardState.Exploring;
    public bool IsInteracting => state != RewardState.Exploring;
    private RewardChest lookedAtChest;
    private RewardChest activeChest;

    private float gazeTimer;
    private Quaternion savedCameraRotation;

    private InputAction previous;
    private InputAction next;
    private InputAction interact;

    private GameObject cardVisual;
    private GameObject heartVisual;
    private CardData selectedCardReward;

    private Vector3 cardBaseScale;
    private Vector3 heartBaseScale;

    private float mouseMovement;

    private RewardChoice selectedReward = RewardChoice.Heart;

    private readonly Dictionary<Renderer, MaterialPropertyBlock> originalBlocks = new Dictionary<Renderer, MaterialPropertyBlock>();

    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        previous = FindAction(previousAction);
        next = FindAction(nextAction);
        interact = FindAction(interactAction);
    }

    private void Start()
    {
        if (combatController == null)
            combatController = FindAnyObjectByType<CombatController>();

        if (combatController == null)
        {
            Debug.LogError("[RewardController] No CombatController found.");
            enabled = false;
            return;
        }

        if (player == null)
            player = combatController.player;

        if (cardSystem == null)
            cardSystem = combatController.cardSystem;

        if (mainCamera == null)
        {
            if (combatController.cameraTransform != null)
                mainCamera = combatController.cameraTransform.GetComponent<Camera>();

            if (mainCamera == null)
                mainCamera = Camera.main;
        }

        if (player == null || mainCamera == null)
        {
            Debug.LogError("[RewardController] Player or Camera reference missing.");
            enabled = false;
        }
    }

    private void Update()
    {
        if (PauseMenuController.BlocksGameplayInput)
            return;

        switch (state)
        {
            case RewardState.Exploring:
                DetectChest();
                break;

            case RewardState.Selecting:
                HandleSelection();
                break;
        }
    }

    private InputAction FindAction(string actionName)
    {
        if (string.IsNullOrEmpty(actionName))
            return null;

        InputAction action = InputSystem.actions != null 
            ? InputSystem.actions.FindAction(actionName)
            : null;

        if (action == null)
            Debug.LogWarning($"[RewardController] Input Action '{actionName}' not found.");

        return action;
    }

    private void DetectChest()
    {
        if (player.IsMoving || player.InputLocked)
        {
            ResetGaze();
            return;
        }

        if (combatController.CurrentState != CombatController.State.Exploring)
        {
            ResetGaze();
            return;
        }

        Ray ray = new Ray(mainCamera.transform.position, mainCamera.transform.forward);

        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, detectionMask))
        {
            ResetGaze();
            return;
        }

        RewardChest chest = hit.collider.GetComponentInParent<RewardChest>();

        if (chest == null || chest.IsClaimed)
        {
            ResetGaze();
            return;
        }

        if (lookedAtChest != chest)
        {
            lookedAtChest = chest;
            gazeTimer = 0f;
        }

        gazeTimer += Time.deltaTime;

        if (gazeTimer >= gazeRequiredDuration)
        {
            ResetGaze();
            StartCoroutine(BeginRewardInteraction(chest));
        }
    }

    private void ResetGaze()
    {
        lookedAtChest = null;
        gazeTimer = 0f;
    }

    private IEnumerator BeginRewardInteraction(RewardChest chest)
    {
        if (chest == null || chest.IsClaimed) yield break;

        if (combatController.CurrentState != CombatController.State.Exploring) yield break;

        activeChest = chest;
        state = RewardState.Focusing;

        savedCameraRotation = combatController.cameraTransform.rotation;

        player.SetInputLocked(true);
        combatController.SetCameraControlEnabled(false);
        combatController.StopCombatSuppressedThings();

        Vector3 aimPosition = activeChest.focusPoint.position + Vector3.up * focusAimHeight;

        Vector3 direction = aimPosition - combatController.cameraTransform.position;

        Quaternion targetRotation = direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction, Vector3.up) : combatController.cameraTransform.rotation;

        yield return combatController.TurnCameraTo(targetRotation, combatController.focusTurnDuration);

        state = RewardState.Opening;

        yield return activeChest.Open();

        selectedCardReward = GetRandomCardReward();

        yield return PresentRewards();

        selectedReward = RewardChoice.Heart;
        activeChest.PlayRewardSound(false);
        UpdateHighlight();

        state = RewardState.Selecting;
    }

    private IEnumerator PresentRewards()
    {
        activeChest.PlayRewardSpawnSound();
        Transform spawnPoint = activeChest.rewardSpawnPoint;

        Vector3 cardTarget = new Vector3(-rewardSeparation * 0.5f, rewardHeight, 0f);

        Vector3 heartTarget = new Vector3(rewardSeparation * 0.5f, rewardHeight, 0f);

        Vector3 startPosition = Vector3.zero;

        if (selectedCardReward != null)
        {
            int rewardLevel = Mathf.Clamp(cardSystem.GetOwnedCardCount(selectedCardReward) + 1, 1, 5);

            CardView rewardCard = selectedCardReward.CreateView(spawnPoint, rewardLevel);

            if (rewardCard != null)
            {
                cardVisual = rewardCard.gameObject;

                cardVisual.transform.localPosition = startPosition;
                cardVisual.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); ;

                cardVisual.transform.localScale = Vector3.one * 1.7f;
                cardBaseScale = cardVisual.transform.localScale;

                DisableRewardFloat(cardVisual);
            }
        }

        if (activeChest.heartVisualPrefab != null)
        {
            heartVisual = Instantiate(
                activeChest.heartVisualPrefab,
                spawnPoint,
                false
            );

            heartVisual.transform.localPosition = startPosition;
            heartVisual.transform.localRotation = Quaternion.identity;

            heartVisual.transform.localScale = Vector3.one * 0.3f;
            heartBaseScale = heartVisual.transform.localScale;
            DisableRewardFloat(heartVisual);
        }

        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, rewardRiseDuration);

        Vector3 cardStart = startPosition;
        Vector3 heartStart = startPosition;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / duration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            if (cardVisual != null)
            {
                cardVisual.transform.localPosition = Vector3.Lerp(
                    cardStart,
                    cardTarget,
                    smoothT
                );
            }

            if (heartVisual != null)
            {
                heartVisual.transform.localPosition = Vector3.Lerp(
                    heartStart,
                    heartTarget,
                    smoothT
                );
            }

            yield return null;
        }

        if (cardVisual != null)
        {
            cardVisual.transform.localPosition = cardTarget;
            EnableRewardFloat(cardVisual);
        }

        if (heartVisual != null)
        {
            heartVisual.transform.localPosition = heartTarget;
            EnableRewardFloat(heartVisual);
        }
    }

    private void HandleSelection()
    {
        if (Pressed(previous))
            SelectReward(RewardChoice.Heart);

        if (Pressed(next))
            SelectReward(RewardChoice.Card);

        if (Mouse.current != null)
        {
            mouseMovement += Mouse.current.delta.ReadValue().x;

            if (mouseMovement >= mouseSelectionThreshold)
            {
                SelectReward(RewardChoice.Card);
                mouseMovement = 0f;
            }
            else if (mouseMovement <= -mouseSelectionThreshold)
            {
                SelectReward(RewardChoice.Heart);
                mouseMovement = 0f;
            }
        }

        AnimateSelection();

        if (Pressed(interact))
            StartCoroutine(CollectSelectedReward());
    }

    private bool Pressed(InputAction action)
    {
        return action != null && action.WasPressedThisFrame();
    }

    private void SelectReward(RewardChoice choice)
    {
        selectedReward = choice;

        if (choice == RewardChoice.Card)
        {
            if (selectedCardReward != null && cardVisual != null)
            {
                CardView rewardCard = cardVisual.GetComponent<CardView>();
                if (rewardCard != null) selectedCardReward.PlayCardFeedback(rewardCard.transform, rewardCard.Level);
            }
        }
        else
        {
            activeChest.PlayRewardSound(false);
        }

        UpdateHighlight();
    }

    private void UpdateHighlight()
    {
        SetHighlight(cardVisual, selectedReward == RewardChoice.Card);
        SetHighlight(heartVisual, selectedReward == RewardChoice.Heart);
    }

    private void SetHighlight(GameObject target, bool highlighted)
    {
        if (target == null)
            return;

        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>())
        {
            if (highlighted)
            {
                if (!originalBlocks.ContainsKey(renderer))
                {
                    MaterialPropertyBlock original = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(original);
                    originalBlocks.Add(renderer, original);
                }

                MaterialPropertyBlock block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetColor(EmissionColor, highlightColor * 1.5f);
                renderer.SetPropertyBlock(block);
            }
            else if (originalBlocks.TryGetValue(renderer, out MaterialPropertyBlock original))
            {
                renderer.SetPropertyBlock(original);
                originalBlocks.Remove(renderer);
            }
        }
    }

    private void AnimateSelection()
    {
        if (cardVisual != null)
        {
            Vector3 target = cardBaseScale *
                (selectedReward == RewardChoice.Card ? selectedScaleMultiplier : 1f);

            cardVisual.transform.localScale = Vector3.Lerp(
                cardVisual.transform.localScale,
                target,
                selectionAnimationSpeed * Time.deltaTime
            );
        }

        if (heartVisual != null)
        {
            Vector3 target = heartBaseScale *
                (selectedReward == RewardChoice.Heart ? selectedScaleMultiplier : 1f);

            heartVisual.transform.localScale = Vector3.Lerp(
                heartVisual.transform.localScale,
                target,
                selectionAnimationSpeed * Time.deltaTime
            );
        }
    }

    private IEnumerator CollectSelectedReward()
    {
        state = RewardState.Returning;

        if (selectedReward == RewardChoice.Card)
        {
            if (selectedCardReward != null && cardVisual != null)
            {
                CardView rewardCard = cardVisual.GetComponent<CardView>();
                if (rewardCard != null) selectedCardReward.PlayCardFeedback(rewardCard.transform, rewardCard.Level);
            }

            ApplyCardReward();

            if (heartVisual != null)
                Destroy(heartVisual);
        }
        else
        {
            activeChest.PlayRewardSound(false);

            ApplyHeartReward();

            if (cardVisual != null)
                Destroy(cardVisual);
        }

        activeChest.MarkClaimed();

        yield return new WaitForSeconds(0.25f);

        yield return combatController.TurnCameraTo(savedCameraRotation, combatController.releaseTurnDuration);

        combatController.SetCameraControlEnabled(true);
        combatController.RestoreCombatSuppressedThings();

        player.SetInputLocked(false);

        ClearRewardVisuals();

        activeChest = null;
        state = RewardState.Exploring;
    }

    private void ApplyCardReward()
    {
        if (selectedCardReward == null)
        {
            Debug.LogWarning("[RewardController] No eligible card reward selected.");
            return;
        }

        if (cardSystem == null)
        {
            Debug.LogWarning("[RewardController] No CardSystem available.");
            return;
        }

        cardSystem.AddCardToCollection(selectedCardReward);

        int newLevel = 0;

        foreach (CardData card in cardSystem.collection)
        {
            if (card == selectedCardReward)
                newLevel++;
        }

        Debug.Log(
            $"[RewardController] Collected {selectedCardReward.displayName} - Level {newLevel}"
        );
    }

    private void ApplyHeartReward()
    {
        if (playerHealth == null)
        {
            Debug.LogWarning("[Reward] PlayerHealth reference is missing.");
            return;
        }

        playerHealth.IncreaseMaxHealthAndFullyHeal(5);
    }

    private void ClearRewardVisuals()
    {
        if (cardVisual != null)
            Destroy(cardVisual);

        if (heartVisual != null)
            Destroy(heartVisual);

        cardVisual = null;
        heartVisual = null;
        selectedCardReward = null;

        originalBlocks.Clear();
    }

    private void DisableRewardFloat(GameObject reward)
    {
        foreach (RewardFloatAnimation animation in
                 reward.GetComponentsInChildren<RewardFloatAnimation>(true))
        {
            animation.enabled = false;
        }
    }

    private void EnableRewardFloat(GameObject reward)
    {
        foreach (RewardFloatAnimation animation in
                 reward.GetComponentsInChildren<RewardFloatAnimation>(true))
        {
            animation.enabled = true;
        }
    }

    private CardData GetRandomCardReward()
    {
        if (cardSystem == null)
        {
            Debug.LogWarning("[RewardController] No CardSystem available.");
            return null;
        }

        IReadOnlyList<CardData> availableCards = ContentAvailability.GetAvailable<CardData>("cards");

        List<CardData> eligibleCards = new List<CardData>();

        foreach (CardData card in availableCards)
        {
            if (card == null)
                continue;

            int ownedCopies = 0;

            foreach (CardData ownedCard in cardSystem.collection)
            {
                if (ownedCard == card)
                    ownedCopies++;
            }

            if (ownedCopies >= card.maxCopies)
                continue;

            eligibleCards.Add(card);
        }

        if (eligibleCards.Count == 0)
        {
            Debug.Log("[RewardController] No eligible card rewards remaining.");
            return null;
        }

        CardData selected = eligibleCards[Random.Range(0, eligibleCards.Count)];

        selected = eligibleCards[0];

        Debug.Log($"[RewardController] Selected card reward: {selected.displayName}");

        return selected;
    }
}
