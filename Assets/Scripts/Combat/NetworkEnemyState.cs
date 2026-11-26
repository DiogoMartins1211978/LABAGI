
using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Enemy))]
public class NetworkEnemyState : NetworkBehaviour
{
    private readonly NetworkVariable<FixedString64Bytes> roomId =
        new NetworkVariable<FixedString64Bytes>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private readonly NetworkVariable<int> enemyHealth =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private Enemy enemy;

    public string RoomId => roomId.Value.ToString();
    public int CurrentHealth => enemyHealth.Value;

    private void Awake()
    {
        enemy = GetComponent<Enemy>();
    }

    // Called by the host before NetworkObject.Spawn().
    public void SetRoomIdBeforeSpawn(string id)
    {
        if (NetworkObject.IsSpawned)
        {
            Debug.LogError(
                "[NetworkEnemyState] Set the room ID before spawning."
            );
            return;
        }

        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsServer)
        {
            Debug.LogError(
                "[NetworkEnemyState] Only the host can set the room ID."
            );
            return;
        }

        roomId.Value = new FixedString64Bytes(id);
    }

    public override void OnNetworkSpawn()
    {
        enemy = GetComponent<Enemy>();

        enemyHealth.OnValueChanged += OnHealthChanged;

        // The host initializes the authoritative health.
        if (IsServer)
        {
            enemyHealth.Value = enemy.maxHealth;
        }

        // Apply the current value locally, including on joining clients.
        ApplyHealth(enemyHealth.Value);

        StartCoroutine(BindToRoomWhenReady());
    }

    public override void OnNetworkDespawn()
    {
        enemyHealth.OnValueChanged -= OnHealthChanged;
    }

    private void OnHealthChanged(int previousValue, int newValue)
    {
        ApplyHealth(newValue);
    }

    private void ApplyHealth(int health)
    {
        if (enemy == null)
            enemy = GetComponent<Enemy>();

        if (enemy != null)
            enemy.SetHealthFromNetwork(health);
    }

    // Multiplayer damage entry point.
    // Only the host can change the authoritative health.
    public bool ApplyDamageServer(int damage)
    {
        if (!IsServer || !IsSpawned)
            return false;

        if (damage <= 0 || enemyHealth.Value <= 0)
            return false;

        enemyHealth.Value = Mathf.Max(
            0,
            enemyHealth.Value - damage
        );

        ApplyHealth(enemyHealth.Value);

        Debug.Log(
            $"[NetworkEnemyState] {name} received {damage} damage. " +
            $"Health: {enemyHealth.Value}/{enemy.maxHealth}"
        );

        return true;
    }

    private IEnumerator BindToRoomWhenReady()
    {
        const int maxAttempts = 120;

        for (int i = 0; i < maxAttempts; i++)
        {
            DungeonGenerator generator =
                FindAnyObjectByType<DungeonGenerator>();

            if (generator != null &&
                generator.TryGetRoom(RoomId, out RoomNode room))
            {
                if (enemy == null)
                    enemy = GetComponent<Enemy>();

                enemy.Room = room;
                room.enemy = enemy;

                Debug.Log(
                    $"[NetworkEnemyState] {name} bound to room '{RoomId}'."
                );

                yield break;
            }

            yield return null;
        }

        Debug.LogError(
            $"[NetworkEnemyState] Could not find room '{RoomId}' for {name}."
        );
    }
}
