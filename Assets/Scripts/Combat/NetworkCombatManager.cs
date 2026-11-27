
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Tools.Content;

[RequireComponent(typeof(NetworkObject))]
public class NetworkCombatManager : NetworkBehaviour
{
    public static NetworkCombatManager Instance { get; private set; }

    private readonly Dictionary<ulong, FixedString64Bytes> playerRooms = new Dictionary<ulong, FixedString64Bytes>();

    private readonly HashSet<FixedString64Bytes> activeCombatRooms = new HashSet<FixedString64Bytes>();
    private readonly HashSet<FixedString64Bytes> lockedRooms = new();
    private readonly Dictionary<FixedString64Bytes, HashSet<ulong>> combatParticipants = new Dictionary<FixedString64Bytes, HashSet<ulong>>();
    private class CardSubmission
    {
        public FixedString64Bytes CardId;
        public int Level;
    }
    private readonly Dictionary<FixedString64Bytes, Dictionary<ulong, CardSubmission>> submittedCards = new();
    private readonly HashSet<FixedString64Bytes> resolvingRooms = new();
    private FixedString64Bytes localCombatRoom;
    private bool hasLocalCombatRoom;

    private readonly Dictionary<FixedString64Bytes, int> pendingEnemyAttackDamage = new();
    public event Action<string, bool> MultiplayerCombatEnded;

    public ulong LocalCombatTargetClientId { get; private set; }
    public bool IsLocalPlayerInMultiplayerCombat => IsSpawned && hasLocalCombatRoom;

    public bool IsRoomLocked(string roomId)
    {
        if (string.IsNullOrEmpty(roomId)) return false;

        return lockedRooms.Contains(new FixedString64Bytes(roomId));
    }

    private class PlayerVitals
    {
        public int Health;
        public int Shield;
        public int MaxHealth;
    }

    private readonly Dictionary<ulong, PlayerVitals> playerVitals = new Dictionary<ulong, PlayerVitals>();

    private PlayerHealth localPlayerHealth;

    public void RegisterLocalPlayerHealth(PlayerHealth health)
    {
        if (!IsSpawned || health == null)
            return;

        localPlayerHealth = health;

        int currentHealth = health.CurrentHealth;
        int currentShield = health.CurrentShield;
        int maxHealth = health.maxHealth;

        if (IsServer)
        {
            RegisterPlayerVitals(
                NetworkManager.LocalClientId,
                currentHealth,
                currentShield,
                maxHealth
            );
        }
        else
        {
            RegisterPlayerVitalsRpc(
                currentHealth,
                currentShield,
                maxHealth
            );
        }
    }

    public void ReportLocalPlayerVitals(PlayerHealth health)
    {
        if (!IsSpawned || health == null || health != localPlayerHealth)
            return;

        int currentHealth = health.CurrentHealth;
        int currentShield = health.CurrentShield;
        int maxHealth = health.maxHealth;

        if (IsServer)
        {
            UpdatePlayerVitals(
                NetworkManager.LocalClientId,
                currentHealth,
                currentShield,
                maxHealth
            );
        }
        else
        {
            UpdatePlayerVitalsRpc(
                currentHealth,
                currentShield,
                maxHealth
            );
        }
    }

    [Rpc(SendTo.Server)]
    private void UpdatePlayerVitalsRpc(int health, int shield, int maxHealth, RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        if (!playerVitals.ContainsKey(senderId))
        {
            Debug.LogWarning(
                $"[NetworkCombatManager] Ignored vitals update from unregistered player {senderId}."
            );
            return;
        }

        UpdatePlayerVitals(senderId, health, shield, maxHealth);
    }

    private void UpdatePlayerVitals(ulong clientId, int health, int shield, int maxHealth)
    {
        if (!IsServer) return;

        maxHealth = Mathf.Max(1, maxHealth);

        playerVitals[clientId] = new PlayerVitals
        {
            Health = Mathf.Clamp(health, 0, maxHealth),
            Shield = Mathf.Max(0, shield),
            MaxHealth = maxHealth
        };

        Debug.Log(
            $"[NetworkCombatManager] Updated player {clientId}: " +
            $"HP {playerVitals[clientId].Health}/{maxHealth} | " +
            $"Shield {playerVitals[clientId].Shield}"
        );

        SyncPlayerVitalsClientRpc(
            clientId,
            playerVitals[clientId].Health,
            playerVitals[clientId].Shield
        );
    }


    [Rpc(SendTo.Server)]
    private void RegisterPlayerVitalsRpc(int health, int shield, int maxHealth, RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        RegisterPlayerVitals(senderId, health, shield, maxHealth);
    }

    private void RegisterPlayerVitals(ulong clientId, int health, int shield, int maxHealth)
    {
        if (!IsServer) return;

        maxHealth = Mathf.Max(1, maxHealth);

        playerVitals[clientId] = new PlayerVitals
        {
            Health = Mathf.Clamp(health, 0, maxHealth),
            Shield = Mathf.Max(0, shield),
            MaxHealth = maxHealth
        };

        Debug.Log($"[NetworkCombatManager] Registered player {clientId}: HP {health}/{maxHealth} | Shield {shield}"
        );

        SyncPlayerVitalsClientRpc(clientId, playerVitals[clientId].Health, playerVitals[clientId].Shield);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SyncPlayerVitalsClientRpc(ulong playerId, int health, int shield)
    {
        if (NetworkManager.LocalClientId != playerId) return;

        if (localPlayerHealth == null)
        {
            Debug.LogWarning("[NetworkCombatManager] Received health sync before local PlayerHealth was registered.");
            return;
        }

        localPlayerHealth.SetHealthFromNetwork(health, shield);
    }

    public event Action<string> MultiplayerCombatStarted;

    public override void OnNetworkSpawn()
    {
        Instance = this;

        if (IsServer)
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;

        Debug.Log(
            $"[NetworkCombatManager] Spawned! " +
            $"Client ID: {NetworkManager.LocalClientId} | " +
            $"Server: {IsServer}"
        );
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;

        if (Instance == this)
            Instance = null;
    }

    public void ReportCurrentRoom(string roomId)
    {
        if (!IsSpawned || string.IsNullOrEmpty(roomId))
            return;

        FixedString64Bytes id = new FixedString64Bytes(roomId);

        if (IsServer)
            RegisterPlayerRoom(NetworkManager.LocalClientId, id);
        else
            ReportRoomRpc(id);
    }

    public void RequestCombatStart(string roomId)
    {
        if (!IsSpawned || string.IsNullOrEmpty(roomId))
            return;

        FixedString64Bytes id = new FixedString64Bytes(roomId);

        if (IsServer) StartPlayerCombat(id, NetworkManager.LocalClientId);
        else RequestCombatStartRpc(id);
    }

    [Rpc(SendTo.Server)]
    private void ReportRoomRpc(FixedString64Bytes roomId, RpcParams rpcParams = default)
    {
        RegisterPlayerRoom(rpcParams.Receive.SenderClientId, roomId);
    }

    [Rpc(SendTo.Server)]
    private void RequestCombatStartRpc(FixedString64Bytes roomId, RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        StartPlayerCombat(roomId, senderId);
    }

    private void RegisterPlayerRoom(ulong clientId, FixedString64Bytes roomId)
    {
        playerRooms[clientId] = roomId;

        Debug.Log(
            $"[NetworkCombatManager] Player {clientId} is in room '{roomId}'."
        );
    }

    private void TryStartCombat(FixedString64Bytes roomId)
    {
        if (!IsServer)
            return;

        if (activeCombatRooms.Contains(roomId))
            return;

        // We need both players connected.
        if (NetworkManager.ConnectedClientsIds.Count < 2)
            return;

        // Every connected player must be in this same room.
        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (!playerRooms.TryGetValue(clientId, out FixedString64Bytes playerRoom))
                return;

            if (!playerRoom.Equals(roomId))
                return;
        }

        activeCombatRooms.Add(roomId);

        Debug.Log(
            $"[NetworkCombatManager] Both players are in room '{roomId}'. Starting combat!"
        );

        StartCombatClientRpc(roomId);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void StartCombatClientRpc(FixedString64Bytes roomId)
    {
        Debug.Log(
            $"[NetworkCombatManager] Combat start received for room '{roomId}'."
        );

        MultiplayerCombatStarted?.Invoke(roomId.ToString());
    }

    public bool TryBroadcastCardAudio(CardData card, int currentLevel)
    {
        if (!IsSpawned || NetworkManager == null || !NetworkManager.IsListening || !hasLocalCombatRoom) return false;

        if (!ContentAvailability.TryGetCatalogId(card, out string cardId))
        {
            Debug.LogWarning($"[NetworkCombatManager] Cannot sync card audio: '{card?.name}' is not in the content catalogue.");
            return false;
        }

        FixedString64Bytes id = new FixedString64Bytes(cardId);
        int level = Mathf.Max(1, currentLevel);

        if (IsServer)
        {
            if (!combatParticipants.TryGetValue(localCombatRoom, out HashSet<ulong> participants) || !participants.Contains(NetworkManager.LocalClientId)) return false;

            RelayCardAudioClientRpc(localCombatRoom, id, level, NetworkManager.LocalClientId );
        }
        else RequestCardAudioRpc(localCombatRoom, id, level);

        return true;
    }

    [Rpc(SendTo.Server)]
    private void RequestCardAudioRpc(FixedString64Bytes roomId, FixedString64Bytes cardId, int currentLevel, RpcParams rpcParams = default)
    {
        ulong ownerClientId = rpcParams.Receive.SenderClientId;
        if (!combatParticipants.TryGetValue(roomId, out HashSet<ulong> participants) || !participants.Contains(ownerClientId))
        {
            Debug.LogWarning($"[NetworkCombatManager] Rejected card audio from non-participant {ownerClientId}.");
            return;
        }

        if (!ContentAvailability.TryGetCardById(cardId.ToString(), out _))
        {
            Debug.LogWarning($"[NetworkCombatManager] Rejected card audio for unknown card ID '{cardId}'.");
            return;
        }

        RelayCardAudioClientRpc(roomId, cardId, Mathf.Max(1, currentLevel), ownerClientId);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void RelayCardAudioClientRpc(FixedString64Bytes roomId, FixedString64Bytes cardId, int currentLevel, ulong ownerClientId)
    {
        ulong localClientId = NetworkManager.LocalClientId;
        
        if (localClientId == ownerClientId) return;

        if (!combatParticipants.TryGetValue(roomId, out HashSet<ulong> participants) || !participants.Contains(localClientId)) return;

        if (!ContentAvailability.TryGetCardById(cardId.ToString(), out CardData card))
        {
            Debug.LogWarning($"[NetworkCombatManager] Card audio asset '{cardId}' is missing from this client's catalogue.");
            return;
        }

        AudioListener listener = FindAnyObjectByType<AudioListener>();

        if (listener == null)
        {
            Debug.LogWarning("[NetworkCombatManager] No local AudioListener found.");
            return;
        }

        Transform listenerTransform = listener.transform;

        float side = ownerClientId == 0UL ? -1f : 1f;

        Vector3 soundPosition = listenerTransform.position + listenerTransform.right * side * 0.45f;

        GameObject originHost = new GameObject("CardAudioOrigin");
        originHost.transform.position = soundPosition;

        card.PlayCardFeedback(originHost.transform, currentLevel, 0.5f);

        Destroy(originHost);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        playerRooms.Remove(clientId);
        playerVitals.Remove(clientId);
    }

    private void StartPlayerCombat(FixedString64Bytes roomId, ulong playerId)
    {
        if (!IsServer) return;

        if (!NetworkManager.ConnectedClients.ContainsKey(playerId)) return;

        if (lockedRooms.Contains(roomId))
        {
            Debug.LogWarning($"[NetworkCombatManager] Room '{roomId}' is already locked.");
            return;
        }

        HashSet<ulong> participants = new HashSet<ulong>();

        foreach (var entry in playerRooms) 
            if (entry.Value.Equals(roomId) && NetworkManager.ConnectedClients.ContainsKey(entry.Key)) participants.Add(entry.Key);

        if (!participants.Contains(playerId))
        {
            Debug.LogWarning($"[NetworkCombatManager] Player {playerId} is not registered in room '{roomId}'.");
            return;
        }

        lockedRooms.Add(roomId);
        combatParticipants[roomId] = participants;

        ulong[] participantIds = new ulong[participants.Count];
        participants.CopyTo(participantIds);

        int targetIndex = UnityEngine.Random.Range(0, participantIds.Length);
        ulong targetClientId = participantIds[targetIndex];

        Debug.Log($"[NetworkCombatManager] Enemy target selected: Player {targetClientId}");

        Debug.Log($"[NetworkCombatManager] Combat started in '{roomId}'. Participants: {string.Join(", ", participantIds)}");

        StartPlayerCombatClientRpc(roomId, participantIds, targetClientId);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void StartPlayerCombatClientRpc(FixedString64Bytes roomId, ulong[] participantIds, ulong targetClientId)
    {
        lockedRooms.Add(roomId);

        HashSet<ulong> participants = new HashSet<ulong>(participantIds);
        combatParticipants[roomId] = participants;

        LocalCombatTargetClientId = targetClientId;

        Debug.Log($"[NetworkCombatManager] Enemy target for room '{roomId}': Player {LocalCombatTargetClientId}");

        ulong localClientId = NetworkManager.LocalClientId;
        bool isParticipant = participants.Contains(localClientId);

        Debug.Log($"[NetworkCombatManager] Room '{roomId}' locked. Local player {localClientId} is " + (isParticipant ? "a participant." : "not a participant."));

        if (!isParticipant) return;

        localCombatRoom = roomId;
        hasLocalCombatRoom = true;

        MultiplayerCombatStarted?.Invoke(roomId.ToString());
    }


    public bool TrySubmitCard(CardData card, int level)
    {
        if (!IsSpawned || !hasLocalCombatRoom || card == null)
            return false;

        if (!ContentAvailability.TryGetCatalogId(card, out string cardId))
        {
            Debug.LogWarning(
                $"[NetworkCombatManager] Cannot submit card '{card.name}': not in content catalogue."
            );
            return false;
        }

        FixedString64Bytes networkCardId = new FixedString64Bytes(cardId);
        int safeLevel = Mathf.Clamp(level, 1, 5);

        if (IsServer)
        {
            return RegisterCardSubmission(
                localCombatRoom,
                NetworkManager.LocalClientId,
                networkCardId,
                safeLevel
            );
        }

        SubmitCardRpc(localCombatRoom, networkCardId, safeLevel);
        return true;
    }

    [Rpc(SendTo.Server)]
    private void SubmitCardRpc(FixedString64Bytes roomId, FixedString64Bytes cardId, int level, RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        RegisterCardSubmission(
            roomId,
            senderId,
            cardId,
            Mathf.Clamp(level, 1, 5)
        );
    }

    private bool RegisterCardSubmission(FixedString64Bytes roomId, ulong playerId, FixedString64Bytes cardId, int level)
    {
        if (!IsServer)
            return false;

        if (resolvingRooms.Contains(roomId))
            return false;

        if (!combatParticipants.TryGetValue(
            roomId,
            out HashSet<ulong> participants) ||
            !participants.Contains(playerId))
        {
            Debug.LogWarning(
                $"[MP Combat] Rejected card submission from non-participant {playerId}."
            );
            return false;
        }

        if (!ContentAvailability.TryGetCardById(cardId.ToString(), out _))
        {
            Debug.LogWarning(
                $"[MP Combat] Rejected unknown card ID '{cardId}'."
            );
            return false;
        }

        if (!submittedCards.TryGetValue(
            roomId,
            out Dictionary<ulong, CardSubmission> roomSubmissions))
        {
            roomSubmissions = new Dictionary<ulong, CardSubmission>();
            submittedCards.Add(roomId, roomSubmissions);
        }

        if (roomSubmissions.ContainsKey(playerId))
        {
            Debug.LogWarning(
                $"[MP Combat] Player {playerId} already submitted a card this round."
            );
            return false;
        }

        roomSubmissions.Add(playerId, new CardSubmission
        {
            CardId = cardId,
            Level = level
        });

        Debug.Log(
            $"[MP Combat] Player {playerId} submitted card '{cardId}' " +
            $"(Lv. {level}) in room '{roomId}'. " +
            $"Submissions: {roomSubmissions.Count}/{participants.Count}"
        );

        if (roomSubmissions.Count == participants.Count)
        {
            Debug.Log($"[MP Combat] All players submitted their cards in room '{roomId}'.");

            ResolveSubmittedCards(roomId);
        }

        return true;
    }

    private void ResolveSubmittedCards(FixedString64Bytes roomId)
    {
        if (!IsServer)
            return;

        if (resolvingRooms.Contains(roomId))
            return;

        if (!submittedCards.TryGetValue(
            roomId,
            out Dictionary<ulong, CardSubmission> submissions))
            return;

        if (!combatParticipants.TryGetValue(
            roomId,
            out HashSet<ulong> participants))
            return;

        if (submissions.Count != participants.Count)
            return;

        resolvingRooms.Add(roomId);

        NetworkEnemyState networkEnemy = FindNetworkEnemy(roomId);

        if (networkEnemy == null)
        {
            Debug.LogError(
                $"[MP Combat] No NetworkEnemyState found for room '{roomId}'."
            );

            resolvingRooms.Remove(roomId);
            return;
        }

        Debug.Log(
            $"[MP Combat] Resolving {submissions.Count} cards in room '{roomId}'."
        );

        foreach (var submission in submissions)
        {
            ulong playerId = submission.Key;
            CardSubmission submittedCard = submission.Value;

            if (!ContentAvailability.TryGetCardById(
                submittedCard.CardId.ToString(),
                out CardData card))
            {
                Debug.LogError(
                    $"[MP Combat] Could not load card '{submittedCard.CardId}'."
                );
                continue;
            }

            Debug.Log(
                $"[MP Combat] Resolving {card.displayName} " +
                $"(Lv. {submittedCard.Level}) from player {playerId}."
            );

            switch (card.type)
            {
                case CardType.Attack:

                    networkEnemy.ApplyDamageServer(card.effectValue);

                    break;

                case CardType.Defense:

                    ApplyPlayerCardEffect(
                        playerId,
                        card,
                        isHeal: false
                    );

                    break;

                case CardType.Ability:

                    switch (card.abilityType)
                    {
                        case AbilityType.Heal:

                            ApplyPlayerCardEffect(
                                playerId,
                                card,
                                isHeal: true
                            );

                            break;

                        case AbilityType.DamageOverTime:

                            Debug.LogWarning(
                                $"[MP Combat] Damage Over Time is not yet " +
                                $"networked: {card.displayName} was not applied."
                            );

                            break;
                    }

                    break;

                case CardType.Trap:

                    Debug.LogWarning(
                        $"[MP Combat] Trap effects are not implemented yet: " +
                        $"{card.displayName}."
                    );

                    break;
            }
        }

        Debug.Log(
            $"[MP Combat] Card resolution completed in room '{roomId}'."
        );

        if (networkEnemy.CurrentHealth <= 0)
        {
            FinishMultiplayerCombat(roomId, true);
            return;
        }

        Enemy enemy = networkEnemy.GetComponent<Enemy>();

        if (enemy == null || enemy.Resolved)
        {
            Debug.LogError("[MP Combat] The network enemy has no valid Enemy component.");
            resolvingRooms.Remove(roomId);
            return;
        }

        CardData cue = enemy.CurrentAttackCue;

        int attackDamage =
            cue != null && cue.type == CardType.Attack
                ? Mathf.Max(0, cue.effectValue)
                : 0;

        pendingEnemyAttackDamage[roomId] = attackDamage;

        Debug.Log(
            $"[MP Combat] Enemy executing announced attack. " +
            $"Target: {LocalCombatTargetClientId} | Damage: {attackDamage}"
        );

        ExecuteEnemyAttackClientRpc(roomId, LocalCombatTargetClientId);
    }


    private NetworkEnemyState FindNetworkEnemy(FixedString64Bytes roomId)
    {

        NetworkEnemyState[] enemies = FindObjectsByType<NetworkEnemyState>(FindObjectsSortMode.None);

        foreach (NetworkEnemyState enemy in enemies)
        {
            if (enemy == null || !enemy.IsSpawned)
                continue;

            if (enemy.RoomId == roomId.ToString())
                return enemy;
        }

        return null;
    }


    private void ApplyPlayerCardEffect(ulong playerId, CardData card, bool isHeal)
    {
        if (!IsServer) return;

        if (!playerVitals.TryGetValue(playerId, out PlayerVitals vitals))
        {
            Debug.LogWarning($"[MP Combat] No registered vitals for player {playerId}.");
            return;
        }

        int amount = card.effectValue;

        if (amount <= 0) return;

        if (vitals.Health <= 0) return;

        if (isHeal)
        {
            vitals.Health = Mathf.Min(vitals.Health + amount, vitals.MaxHealth);
            Debug.Log($"[MP Combat] Player {playerId} healed by {amount}. HP: {vitals.Health}/{vitals.MaxHealth}");
        }
        else
        {
            vitals.Shield += amount;
            Debug.Log($"[MP Combat] Player {playerId} gained {amount} shield. Shield: {vitals.Shield}");
        }

        UpdatePlayerVitals(playerId, vitals.Health, vitals.Shield, vitals.MaxHealth);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ExecuteEnemyAttackClientRpc(FixedString64Bytes roomId, ulong targetClientId)
    {
        if (!combatParticipants.TryGetValue(roomId, out HashSet<ulong> participants) || !participants.Contains(NetworkManager.LocalClientId))
            return;

        NetworkEnemyState networkEnemy = FindNetworkEnemy(roomId);

        if (networkEnemy == null)
        {
            Debug.LogError("[MP Combat] Enemy not found when executing attack.");
            return;
        }

        Enemy enemy = networkEnemy.GetComponent<Enemy>();

        if (enemy == null || enemy.Resolved)
            return;

        bool isTarget = NetworkManager.LocalClientId == targetClientId;
        float volume = isTarget ? 1f : 0.5f;

        Action finishedCallback = null;

        // Only the host controls the next multiplayer round.
        if (IsServer)
        {
            finishedCallback = () =>
                CompleteEnemyAttackServer(roomId, targetClientId);
        }

        enemy.Attack(volume, finishedCallback);
    }

    private void CompleteEnemyAttackServer(
        FixedString64Bytes roomId,
        ulong targetClientId)
    {
        if (!IsServer)
            return;

        if (pendingEnemyAttackDamage.TryGetValue(roomId, out int damage))
        {
            ApplyEnemyAttackDamage(targetClientId, damage);
            pendingEnemyAttackDamage.Remove(roomId);
        }

        if (!combatParticipants.TryGetValue(
            roomId,
            out HashSet<ulong> participants))
        {
            resolvingRooms.Remove(roomId);
            return;
        }

        // Clear the completed round before asking for new cards.
        submittedCards.Remove(roomId);
        resolvingRooms.Remove(roomId);

        // The target stays the same for this encounter, as selected at combat start.
        StartNextCombatRoundClientRpc(roomId, targetClientId);
    }

    private void ApplyEnemyAttackDamage(ulong targetClientId, int damage)
    {
        if (!IsServer || damage <= 0)
            return;

        if (!playerVitals.TryGetValue(targetClientId, out PlayerVitals vitals))
        {
            Debug.LogWarning(
                $"[MP Combat] No registered vitals for target {targetClientId}."
            );
            return;
        }

        if (vitals.Health <= 0)
            return;

        int remainingDamage = damage;

        int absorbed = Mathf.Min(vitals.Shield, remainingDamage);
        vitals.Shield -= absorbed;
        remainingDamage -= absorbed;

        if (remainingDamage > 0)
            vitals.Health = Mathf.Max(0, vitals.Health - remainingDamage);

        Debug.Log(
            $"[MP Combat] Enemy hit player {targetClientId} for {damage}. " +
            $"HP: {vitals.Health}/{vitals.MaxHealth} | Shield: {vitals.Shield}"
        );

        UpdatePlayerVitals(
            targetClientId,
            vitals.Health,
            vitals.Shield,
            vitals.MaxHealth
        );
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void StartNextCombatRoundClientRpc(
    FixedString64Bytes roomId,
    ulong targetClientId)
    {
        if (!combatParticipants.TryGetValue(
            roomId,
            out HashSet<ulong> participants))
            return;

        if (!participants.Contains(NetworkManager.LocalClientId))
            return;

        NetworkEnemyState networkEnemy = FindNetworkEnemy(roomId);

        if (networkEnemy == null || networkEnemy.CurrentHealth <= 0)
            return;

        Enemy enemy = networkEnemy.GetComponent<Enemy>();

        if (enemy == null || enemy.Resolved)
            return;

        bool isTarget = NetworkManager.LocalClientId == targetClientId;
        enemy.AnnounceNextAttack(isTarget ? 1f : 0.5f);

        CardSystem cards = FindAnyObjectByType<CardSystem>();

        if (cards != null)
        {
            Debug.Log("[MP Combat] Starting next player card-selection round.");
            cards.StartPlayerTurn();
        }
        else
        {
            Debug.LogError("[MP Combat] No local CardSystem found.");
        }
    }

    private void FinishMultiplayerCombat(
    FixedString64Bytes roomId,
    bool enemyDefeated)
    {
        if (!IsServer)
            return;

        Debug.Log(
            $"[MP Combat] Encounter in '{roomId}' finished. " +
            $"Enemy defeated: {enemyDefeated}"
        );

        EndMultiplayerCombatClientRpc(roomId, enemyDefeated);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void EndMultiplayerCombatClientRpc(
        FixedString64Bytes roomId,
        bool enemyDefeated)
    {
        NetworkEnemyState networkEnemy = FindNetworkEnemy(roomId);

        if (enemyDefeated && networkEnemy != null)
        {
            Enemy enemy = networkEnemy.GetComponent<Enemy>();
            if (enemy != null)
                enemy.Resolve();
        }

        lockedRooms.Remove(roomId);
        activeCombatRooms.Remove(roomId);
        submittedCards.Remove(roomId);
        resolvingRooms.Remove(roomId);
        pendingEnemyAttackDamage.Remove(roomId);
        combatParticipants.Remove(roomId);

        if (hasLocalCombatRoom && localCombatRoom.Equals(roomId))
            hasLocalCombatRoom = false;

        MultiplayerCombatEnded?.Invoke(roomId.ToString(), enemyDefeated);
    }
}
