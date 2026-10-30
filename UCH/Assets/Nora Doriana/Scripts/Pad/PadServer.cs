using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

#region Pad Server
// Host: telefony skanuja kod QR, otwieraja w przegladarce strone pada (GitHub Pages) i lacza sie z hostem przez WebRTC.
// Gra liczy sie tylko na hoscie - telefon wysyla same przyciski, bezposrednio po Wi-Fi. Kazdy telefon dostaje wlasna postac.
public class PadServer : MonoBehaviour
{
    private const string PadPageUrl = "https://doriansik.github.io/WiFi/pad/";
    private const int MaxPads = 4;
    // Tyle czeka postac na telefon, ktory zgubil Wi-Fi albo odswiezyl strone, zanim zwolnimy miejsce.
    private const float ReconnectGraceSeconds = 20f;
    private const float SpawnSpacing = 1.5f;
    private const string PhaseLobby = "lobby";
    private const string PhaseGame = "game";

    private static readonly Color[] PadColors =
    {
        new Color32(0xE5, 0x48, 0x4D, 0xFF),
        new Color32(0x3B, 0x82, 0xF6, 0xFF),
        new Color32(0x22, 0xC5, 0x5E, 0xFF),
        new Color32(0xEA, 0xB3, 0x08, 0xFF)
    };

    private sealed class PadPlayer
    {
        public PadSession Session;
        public int Number;
        public NetworkObject PlayerObject;
        public float DisconnectedTime;
        public bool AppliedReady;
        public string AppliedName;
    }

    public static PadServer Instance { get; private set; }

    public string JoinUrl { get; private set; }
    public bool IsUnavailable { get; private set; }

    private readonly List<PadPlayer> _padPlayers = new List<PadPlayer>();
    private PadHub _hub;
    private PadRtcHost _rtcHost;
    private PadSignaling _signaling;
    private string _currentPhase;

    public static void StartForHost()
    {
        if (Instance != null)
        {
            return;
        }

        var serverObject = new GameObject(nameof(PadServer));
        DontDestroyOnLoad(serverObject);
        serverObject.AddComponent<PadServer>();
    }

    public static void Shutdown()
    {
        if (Instance != null)
        {
            Destroy(Instance.gameObject);
        }
    }

    private async void Awake()
    {
        Instance = this;
        _hub = new PadHub(MaxPads);
        _rtcHost = new PadRtcHost(_hub);
        _signaling = new PadSignaling(MaxPads);
        _signaling.OnOfferReceived += HandleOffer;

        try
        {
            await _signaling.StartAsync();
            JoinUrl = $"{PadPageUrl}?c={_signaling.LobbyCode}";
            Debug.Log($"Pady: telefony dolaczaja pod {JoinUrl}");
        }
        catch (Exception exception)
        {
            // Telefon i host "poznaja sie" przez Unity Lobby - bez internetu pady nie wystartuja.
            IsUnavailable = true;
            Debug.LogWarning($"Pady niedostepne: {exception.Message}");
        }
    }

    private void OnDestroy()
    {
        _signaling?.Stop();
        _rtcHost?.Dispose();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        _signaling.Tick(Time.unscaledTime);
        _hub.CheckTimeouts(Time.unscaledTimeAsDouble);

        while (_hub.TryDequeueEvent(out PadEvent padEvent))
        {
            HandlePadEvent(padEvent);
        }

        UpdatePhase();

        for (int i = _padPlayers.Count - 1; i >= 0; i--)
        {
            UpdatePadPlayer(_padPlayers[i]);
        }
    }

    private void HandleOffer(string playerId, string nonce, string offerSdp)
    {
        StartCoroutine(_rtcHost.AnswerOffer(playerId, offerSdp, answerSdp =>
        {
            if (answerSdp != null)
            {
                _signaling.PublishAnswer(playerId, nonce, answerSdp);
            }
        }));
    }

    private void HandlePadEvent(PadEvent padEvent)
    {
        PadPlayer padPlayer = _padPlayers.Find(player => player.Session == padEvent.Session);

        Debug.Log($"Pady: telefon {padEvent.Session.Name} ({PadSignaling.ShortId(padEvent.Session.Id)}) - {padEvent.Type}");

        if (padEvent.Type == PadEventType.Disconnected)
        {
            if (padPlayer != null)
            {
                padPlayer.DisconnectedTime = Time.unscaledTime;  // pauza zatrzymuje timeScale
            }

            return;
        }

        if (padPlayer == null)
        {
            padPlayer = new PadPlayer { Session = padEvent.Session, Number = GetFreeNumber() };
            _padPlayers.Add(padPlayer);
        }

        Color color = PadColors[(padPlayer.Number - 1) % PadColors.Length];
        _hub.Send(padPlayer.Session, $"w|{padPlayer.Number}|#{ColorUtility.ToHtmlStringRGB(color)}");
        _hub.Send(padPlayer.Session, "s|" + GetCurrentPhase());
    }

    private void UpdatePadPlayer(PadPlayer padPlayer)
    {
        PadSession session = padPlayer.Session;

        if (!session.IsConnected && Time.unscaledTime - padPlayer.DisconnectedTime > ReconnectGraceSeconds)
        {
            RemovePadPlayer(padPlayer);
            return;
        }

        // Spawn dopiero gdy host faktycznie dziala - telefon mogl dolaczyc w trakcie ladowania sceny.
        if (padPlayer.PlayerObject == null && session.IsConnected && IsServerRunning())
        {
            SpawnPlayer(padPlayer);
        }

        if (padPlayer.PlayerObject == null || !padPlayer.PlayerObject.TryGetComponent(out LobbyPlayer lobbyPlayer))
        {
            return;
        }

        if (padPlayer.AppliedName != session.Name)
        {
            padPlayer.AppliedName = session.Name;
            lobbyPlayer.InitializeAsPad(session.Name);
        }

        if (padPlayer.AppliedReady != session.IsReady)
        {
            padPlayer.AppliedReady = session.IsReady;
            lobbyPlayer.SetReadyState(session.IsReady);
        }
    }

    private void SpawnPlayer(PadPlayer padPlayer)
    {
        GameObject playerPrefab = NetworkManager.Singleton.NetworkConfig.PlayerPrefab;
        Vector3 position = playerPrefab.transform.position + Vector3.right * SpawnSpacing * padPlayer.Number;
        GameObject instance = Instantiate(playerPrefab, position, Quaternion.identity);
        instance.AddComponent<PadInput>().Bind(padPlayer.Session);

        Color color = PadColors[(padPlayer.Number - 1) % PadColors.Length];

        foreach (SpriteRenderer spriteRenderer in instance.GetComponentsInChildren<SpriteRenderer>())
        {
            spriteRenderer.color = color;
        }

        // Obiekt nalezy do serwera; destroyWithScene: false - postac przechodzi z lobby do gry jak zwykly gracz.
        padPlayer.PlayerObject = instance.GetComponent<NetworkObject>();
        padPlayer.PlayerObject.Spawn(destroyWithScene: false);
        padPlayer.AppliedName = null;
        padPlayer.AppliedReady = false;
    }

    private void RemovePadPlayer(PadPlayer padPlayer)
    {
        if (padPlayer.PlayerObject != null && padPlayer.PlayerObject.IsSpawned)
        {
            padPlayer.PlayerObject.Despawn(true);
        }

        _hub.RemoveSession(padPlayer.Session);
        _rtcHost.Forget(padPlayer.Session.Id);
        _signaling.RemovePlayer(padPlayer.Session.Id);
        _padPlayers.Remove(padPlayer);
    }

    private void UpdatePhase()
    {
        string phase = GetCurrentPhase();

        if (phase != _currentPhase)
        {
            _currentPhase = phase;
            _hub.Broadcast("s|" + phase);
        }
    }

    private static string GetCurrentPhase()
    {
        return SceneManager.GetActiveScene().name == SceneNames.Game ? PhaseGame : PhaseLobby;
    }

    private int GetFreeNumber()
    {
        int number = 1;

        while (_padPlayers.Any(player => player.Number == number))
        {
            number++;
        }

        return number;
    }

    private static bool IsServerRunning()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsServer && networkManager.IsListening;
    }
}
#endregion
