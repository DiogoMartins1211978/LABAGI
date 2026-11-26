
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class NetworkCombatManager : NetworkBehaviour
{
    public static NetworkCombatManager Instance { get; private set; }

    // Server-side record of each player's current room.
    private readonly Dictionary<ulong, FixedString64Bytes> playerRooms =
        new Dictionary<ulong, FixedString64Bytes>();

    // Prevents multiple combat-start messages for the same room.
    private readonly HashSet<FixedString64Bytes> activeCombatRooms =
        new HashSet<FixedString64Bytes>();

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

    // Called locally whenever this player enters a room.
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

    // Called locally when this player discovers an enemy
    // or attempts to leave the room.
    public void RequestCombatStart(string roomId)
    {
        if (!IsSpawned || string.IsNullOrEmpty(roomId))
            return;

        FixedString64Bytes id = new FixedString64Bytes(roomId);

        if (IsServer)
        {
            RegisterPlayerRoom(NetworkManager.LocalClientId, id);
            TryStartCombat(id);
        }
        else
        {
            RequestCombatStartRpc(id);
        }
    }

    [Rpc(SendTo.Server)]
    private void ReportRoomRpc(
        FixedString64Bytes roomId,
        RpcParams rpcParams = default)
    {
        RegisterPlayerRoom(rpcParams.Receive.SenderClientId, roomId);
    }

    [Rpc(SendTo.Server)]
    private void RequestCombatStartRpc(
        FixedString64Bytes roomId,
        RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        RegisterPlayerRoom(senderId, roomId);
        TryStartCombat(roomId);
    }

    private void RegisterPlayerRoom(
        ulong clientId,
        FixedString64Bytes roomId)
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

    private void OnClientDisconnected(ulong clientId)
    {
        playerRooms.Remove(clientId);
    }
}
