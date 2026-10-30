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
    // Lobby wpuszcza ~1 heartbeat na sekunde na gracza, a host podtrzymuje tez lobby z MatchmakingManager -
    // przesuniecie i szybka ponowna proba, zeby oba nie trafialy w te sama sekunde.
    private const float FirstHeartbeatDelaySeconds = 7.5f;
    private const float HeartbeatRetrySeconds = 2f;
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
        _nextHeartbeatTime = Time.unscaledTime + FirstHeartbeatDelaySeconds;

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
            SendHeartbeatAsync();
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

        PublishAsync(playerId, data);
    }

    private async void PublishAsync(string playerId, Dictionary<string, DataObject> data)
    {
        try
        {
            await LobbyService.Instance.UpdateLobbyAsync(_lobby.Id, new UpdateLobbyOptions { Data = data });
            Debug.Log($"Pady: odpowiedz dla telefonu {ShortId(playerId)} opublikowana w lobby.");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Pady: nie udalo sie opublikowac odpowiedzi dla {ShortId(playerId)} ({exception.Message}).");
        }
    }

    private async void SendHeartbeatAsync()
    {
        try
        {
            await LobbyService.Instance.SendHeartbeatPingAsync(_lobby.Id);
        }
        catch (LobbyServiceException exception) when (exception.Reason == LobbyExceptionReason.RateLimited)
        {
            _nextHeartbeatTime = Time.unscaledTime + HeartbeatRetrySeconds;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Pady: heartbeat lobby nie powiodl sie ({exception.Message}).");
        }
    }

    public static string ShortId(string playerId)
    {
        return playerId.Length > 6 ? playerId.Substring(0, 6) : playerId;
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
            Debug.Log($"Pady: oferta od telefonu {ShortId(player.Id)} (proba {nonce}).");
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
