using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

#region Interfaces
public interface IMatchmakingService
{
    Task CreateSessionAsync(string sessionName);
    Task JoinSessionAsync(string lobbyId);
    Task LeaveSessionAsync();
    Task<List<Lobby>> FetchAvailableSessionsAsync();
}
#endregion

#region Lobby Kinds
// W projekcie UGS sa dwa rodzaje publicznych lobby: gry Relay (aplikacja) i pady z przegladarki.
// Rodzaj siedzi w indeksowanym polu S1, zeby kazda wyszukiwarka widziala tylko swoje.
public static class LobbyKind
{
    public const string Key = "kind";
    public const string Game = "game";
    public const string Pad = "pad";  // ta sama wartosc w docs/pad/index.html (lista gier)

    public static DataObject Create(string kind)
    {
        return new DataObject(DataObject.VisibilityOptions.Public, kind, DataObject.IndexOptions.S1);
    }

    public static QueryFilter Filter(string kind)
    {
        return new QueryFilter(QueryFilter.FieldOptions.S1, kind, QueryFilter.OpOptions.EQ);
    }
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
                { JoinCodeKey, new DataObject(DataObject.VisibilityOptions.Public, joinCode) },
                { LobbyKind.Key, LobbyKind.Create(LobbyKind.Game) }
            }
        };

        _currentLobby = await LobbyService.Instance.CreateLobbyAsync(sessionName, MaxPlayers, lobbyOptions);

        RelayServerData relayServerData = new RelayServerData(allocation, ConnectionProtocol);
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

        NetworkManager.Singleton.StartHost();
    }

    // Host bez Relay i Lobby (brak internetu) - gra na jednym komputerze; pady potrzebuja internetu do signalingu.
    public void StartLocalHost()
    {
        if (!NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.StartHost();
        }
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

    public async Task LeaveSessionAsync()
    {
        if (_currentLobby == null)
        {
            return;
        }

        string lobbyId = _currentLobby.Id;
        bool isLobbyHost = _currentLobby.HostId == AuthenticationService.Instance.PlayerId;
        _currentLobby = null;

        try
        {
            if (isLobbyHost)
            {
                await LobbyService.Instance.DeleteLobbyAsync(lobbyId);
            }
            else
            {
                await LobbyService.Instance.RemovePlayerAsync(lobbyId, AuthenticationService.Instance.PlayerId);
            }
        }
        catch (LobbyServiceException exception)
        {
            // Lobby moglo juz zniknac (np. host wyszedl pierwszy) - nic wiecej do sprzatania.
            Debug.LogWarning(exception.Message);
        }
    }

    public async Task<List<Lobby>> FetchAvailableSessionsAsync()
    {
        QueryLobbiesOptions queryOptions = new QueryLobbiesOptions
        {
            Filters = new List<QueryFilter>
            {
                new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, AvailableSlotsFilter, QueryFilter.OpOptions.GT),
                // Lobby padow nie ma kodu Relay - dolaczenie do niego z aplikacji skonczyloby sie bledem.
                LobbyKind.Filter(LobbyKind.Game)
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