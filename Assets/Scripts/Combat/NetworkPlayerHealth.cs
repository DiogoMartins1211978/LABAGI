
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class NetworkPlayerHealth : NetworkBehaviour
{
    private NetworkVariable<int> health = new(
        20,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<int> shield = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private PlayerHealth localHealth;

    public int CurrentHealth => health.Value;
    public int CurrentShield => shield.Value;

    private void Awake()
    {
        localHealth = GetComponent<PlayerHealth>();
    }

    public override void OnNetworkSpawn()
    {
        health.OnValueChanged += OnHealthChanged;
        shield.OnValueChanged += OnShieldChanged;

        if (IsServer)
        {
            if (localHealth != null)
            {
                health.Value = localHealth.CurrentHealth;
                shield.Value = localHealth.CurrentShield;
            }
        }

        ApplyToLocalPlayer();
    }

    public override void OnNetworkDespawn()
    {
        health.OnValueChanged -= OnHealthChanged;
        shield.OnValueChanged -= OnShieldChanged;
    }

    private void OnHealthChanged(int previous, int current)
    {
        ApplyToLocalPlayer();
    }

    private void OnShieldChanged(int previous, int current)
    {
        ApplyToLocalPlayer();
    }

    private void ApplyToLocalPlayer()
    {
        if (localHealth == null)
            localHealth = GetComponent<PlayerHealth>();

        if (localHealth == null)
            return;

        localHealth.SetHealthFromNetwork(health.Value, shield.Value);
    }

    public void SetHealthServer(int newHealth, int newShield)
    {
        if (!IsServer || !IsSpawned)
            return;

        health.Value = Mathf.Clamp(newHealth, 0, localHealth != null ? localHealth.maxHealth : 20);
        shield.Value = Mathf.Max(0, newShield);
    }
}
