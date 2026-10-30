using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

#region Pad Signaling
// "Zapoznanie" telefonu z hostem przez Unity Lobby: prywatne lobby z kodem z QR, telefon wpisuje oferte WebRTC
// do swoich danych gracza, host odpowiada w danych lobby. Po polaczeniu Lobby nie bierze udzialu w grze.
public sealed class PadSignaling
{
    private const string LobbyName = "Pady";
    private const float HeartbeatIntervalSeconds = 15f;
    private const float MinRefreshIntervalSeconds = 1.1f;  // limit Lobby: odczyt max raz na sekunde
    private const float FallbackRefreshIntervalSeconds = 3f;  // gdyby powiadomienia z Lobby nie doszly

    private readonly Dictionary<string, string> _handledNonces = new Dictionary<string, string>();
    private readonly Dictionary<string, string> _answerKeys = new Dictionary<string, string>();
    private readonly int _maxPads;

    private Lobby _lobby;
    private ILobbyEvents _lobbyEvents;
    private float _nextHeartbeatTime;
    private float _nextRefreshTime;
    private float _lastRefreshTime = float.MinValue;
    private bool _isRefreshing;
    private bool _isRefreshRequested;
    private bool _isStopped;

    // Wolane na watku glownym: idGracza, nonce, SDP oferty.
    public event Action<string, string, string> OnOfferReceived;

    public string LobbyCode => _lobby?.LobbyCode;

    public PadSignaling(int maxPads)
    {
        _maxPads = maxPads;
    }

    public async Task StartAsync()
    {
        _lobby = await LobbyService.Instance.CreateLobbyAsync(LobbyName, _maxPads + 1, new CreateLobbyOptions { IsPrivate = true });

        var callbacks = new LobbyEventCallbacks();
        callbacks.PlayerJoined += _ => RequestRefresh();
        callbacks.PlayerDataAdded += _ => RequestRefresh();
        callbacks.PlayerDataChanged += _ => RequestRefresh();

        try
        {
            _lobbyEvents = await LobbyService.Instance.SubscribeToLobbyEventsAsync(_lobby.Id, callbacks);
        }
        catch (Exception exception)
        {
            // Bez powiadomien tez dziala, tylko telefon laczy sie kilka sekund dluzej.
            Debug.LogWarning($"Pady: powiadomienia Lobby niedostepne ({exception.Message}).");
        }
    }

    public void Tick(float now)
    {
        if (_lobby == null || _isStopped)
        {
            return;
        }

        if (now >= _nextHeartbeatTime)
        {
            _nextHeartbeatTime = now + HeartbeatIntervalSeconds;
            Fire(LobbyService.Instance.SendHeartbeatPingAsync(_lobby.Id));
        }

        bool isRefreshDue = _isRefreshRequested || now >= _nextRefreshTime;

        if (isRefreshDue && !_isRefreshing && now - _lastRefreshTime >= MinRefreshIntervalSeconds)
        {
            _isRefreshRequested = false;
            _lastRefreshTime = now;
            _nextRefreshTime = now + FallbackRefreshIntervalSeconds;
            RefreshAsync();
        }
    }

    public void PublishAnswer(string playerId, string nonce, string sdp)
    {
        if (_lobby == null || _isStopped)
        {
            return;
        }

        var data = new Dictionary<string, DataObject>
        {
            { GetAnswerKey(playerId), new DataObject(DataObject.VisibilityOptions.Member, PadSdp.FormatAnswer(playerId, nonce, sdp)) }
        };

        Fire(LobbyService.Instance.UpdateLobbyAsync(_lobby.Id, new UpdateLobbyOptions { Data = data }));
    }

    // Telefon nie wrocil - zwalniamy jego miejsce w lobby, zeby kolejny mogl zeskanowac kod.
    public void RemovePlayer(string playerId)
    {
        _handledNonces.Remove(playerId);

        if (_lobby != null && !_isStopped)
        {
            Fire(LobbyService.Instance.RemovePlayerAsync(_lobby.Id, playerId));
        }
    }

    public void Stop()
    {
        if (_isStopped)
        {
            return;
        }

        _isStopped = true;

        if (_lobbyEvents != null)
        {
            Fire(_lobbyEvents.UnsubscribeAsync());
        }

        if (_lobby != null)
        {
            Fire(LobbyService.Instance.DeleteLobbyAsync(_lobby.Id));
        }
    }

    private void RequestRefresh()
    {
        _isRefreshRequested = true;
    }

    private async void RefreshAsync()
    {
        _isRefreshing = true;

        try
        {
            Lobby lobby = await LobbyService.Instance.GetLobbyAsync(_lobby.Id);

            if (_isStopped)
            {
                return;
            }

            _lobby = lobby;
            ProcessOffers(lobby);
        }
        catch (LobbyServiceException exception)
        {
            Debug.LogWarning($"Pady: odczyt lobby nie powiodl sie ({exception.Reason}).");
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void ProcessOffers(Lobby lobby)
    {
        string hostId = AuthenticationService.Instance.PlayerId;
        List<string> playerIds = lobby.Players.Select(player => player.Id).ToList();

        foreach (string goneId in _answerKeys.Keys.Where(id => !playerIds.Contains(id)).ToList())
        {
            _answerKeys.Remove(goneId);
            _handledNonces.Remove(goneId);
        }

        foreach (Player player in lobby.Players)
        {
            if (player.Id == hostId || player.Data == null || !player.Data.TryGetValue(PadSdp.OfferKey, out PlayerDataObject offer))
            {
                continue;
            }

            if (!PadSdp.TryParseOffer(offer.Value, out string nonce, out string sdp))
            {
                continue;
            }

            if (_handledNonces.TryGetValue(player.Id, out string handledNonce) && handledNonce == nonce)
            {
                continue;
            }

            _handledNonces[player.Id] = nonce;
            OnOfferReceived?.Invoke(player.Id, nonce, sdp);
        }
    }

    // Kazdy telefon ma wlasne pole na odpowiedz; telefon szuka swojej po idGracza|nonce.
    private string GetAnswerKey(string playerId)
    {
        if (!_answerKeys.TryGetValue(playerId, out string key))
        {
            int slot = 1;

            while (_answerKeys.ContainsValue(PadSdp.AnswerKeyPrefix + slot))
            {
                slot++;
            }

            key = PadSdp.AnswerKeyPrefix + slot;
            _answerKeys[playerId] = key;
        }

        return key;
    }

    private static async void Fire(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Pady: zapytanie do Lobby nie powiodlo sie ({exception.Message}).");
        }
    }
}
#endregion
