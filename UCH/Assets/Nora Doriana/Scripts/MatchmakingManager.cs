using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Lobby;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

#region Interfaces
public interface IMatchmakingService
{
    Task CreateSessionAsync(string sessionName);
    Task JoinSessionAsync(string lobbyId);
    Task<List<Lobby>> FetchAvailableSessionsAsync();
}
#endregion

#region Matchmaking System
public class MatchmakingManager : MonoBehaviour, IMatchmakingService
{
    private const int MaxPlayers = 4;
    private const string ConnectionProtocol = "dtls";
    private const string JoinCodeKey = "RelayJoinCode";
    private const float HeartbeatInterval = 15f;
    private const string AvailableSlotsFilter = "0";

    private Lobby _currentLobby;
    private float _heartbeatTimer;

    private void Update()
    {
        HandleLobbyHeartbeat();
    }

    public async Task CreateSessionAsync(string sessionName)
    {
        Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MaxPlayers);
        string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

        CreateLobbyOptions lobbyOptions = new CreateLobbyOptions
        {
            IsPrivate = false,
            Data = new Dictionary<string, DataObject>
            {
                { JoinCodeKey, new DataObject(DataObject.VisibilityOptions.Public, joinCode) }
            }
        };

        _currentLobby = await LobbyService.Instance.CreateLobbyAsync(sessionName, MaxPlayers, lobbyOptions);

        RelayServerData relayServerData = new RelayServerData(allocation, ConnectionProtocol);
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

        NetworkManager.Singleton.StartHost();
    }

    public async Task JoinSessionAsync(string lobbyId)
    {
        _currentLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId);
        string joinCode = _currentLobby.Data[JoinCodeKey].Value;

        JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

        RelayServerData relayServerData = new RelayServerData(joinAllocation, ConnectionProtocol);
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

        NetworkManager.Singleton.StartClient();
    }

    public async Task<List<Lobby>> FetchAvailableSessionsAsync()
    {
        QueryLobbiesOptions queryOptions = new QueryLobbiesOptions
        {
            Filters = new List<QueryFilter>
            {
                new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, AvailableSlotsFilter, QueryFilter.OpOptions.GT)
            }
        };

        QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(queryOptions);
        return response.Results;
    }

    private async void HandleLobbyHeartbeat()
    {
        if (_currentLobby != null && _currentLobby.HostId == AuthenticationService.Instance.PlayerId)
        {
            _heartbeatTimer += Time.deltaTime;

            if (_heartbeatTimer >= HeartbeatInterval)
            {
                _heartbeatTimer -= HeartbeatInterval;
                await LobbyService.Instance.SendHeartbeatPingAsync(_currentLobby.Id);
            }
        }
    }
}
#endregion