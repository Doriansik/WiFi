using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

#region Pad Server
// Host: telefony skanuja kod QR, otwieraja w przegladarce strone pada (GitHub Pages) i lacza sie z hostem przez WebRTC.
// Gra liczy sie tylko na hoscie - telefon wysyla same przyciski, bezposrednio po Wi-Fi. Kazdy telefon dostaje wlasna postac.
public class PadServer : MonoBehaviour
{
    private const string PadPageUrl = "https://doriansik.github.io/WiFi/pad/";
    private const int MaxPads = 10;
    // Tyle czeka postac na telefon, ktory zgubil Wi-Fi albo odswiezyl strone, zanim zwolnimy miejsce.
    private const float ReconnectGraceSeconds = 20f;
    private const float SpawnSpacing = 1.2f;  // 10 postaci obok hosta miesci sie na ekranie (20 jednostek szerokosci)
    private const string PhaseLobby = "lobby";
    private const string PhaseGame = "game";
    private const string PhaseBuild = "build";
    private const float MarkerHeight = 0.75f;  // kolorowy znacznik nad glowa - postacie moga sie powtarzac, kolor nie

    private static readonly Color[] PadColors =
    {
        new Color32(0xE5, 0x48, 0x4D, 0xFF),
        new Color32(0x3B, 0x82, 0xF6, 0xFF),
        new Color32(0x22, 0xC5, 0x5E, 0xFF),
        new Color32(0xEA, 0xB3, 0x08, 0xFF),
        new Color32(0xA8, 0x55, 0xF7, 0xFF),
        new Color32(0xF9, 0x73, 0x16, 0xFF),
        new Color32(0x06, 0xB6, 0xD4, 0xFF),
        new Color32(0xEC, 0x48, 0x99, 0xFF),
        new Color32(0x84, 0xCC, 0x16, 0xFF),
        new Color32(0x8B, 0x5A, 0x2B, 0xFF)
    };

    private sealed class PadPlayer
    {
        public PadSession Session;
        public int Number;
        public NetworkObject PlayerObject;
        public float DisconnectedTime;
        public bool AppliedReady;
        public string AppliedName;
        public int AppliedSkinRequestCount;
        public string SentSkinState;
        public string SentBuildState;
        public string SentPoolState;
    }

    public static PadServer Instance { get; private set; }

    public string JoinUrl { get; private set; }
    public bool IsUnavailable { get; private set; }

    private readonly List<PadPlayer> _padPlayers = new List<PadPlayer>();
    private PadHub _hub;
    private PadRtcHost _rtcHost;
    private PadSignaling _signaling;
    private string _currentPhase;
    private bool _isSkinStateDirty;
    private Sprite _markerSprite;

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
        PlayerSkin.OnSkinsChanged += HandleSkinsChanged;

        try
        {
            // Najpierw TURN - jego dane trafiaja do lobby od razu przy zakladaniu. Bez TURN pady dzialaja tylko lokalnie.
            string turnServersJson = await PadTurn.FetchIceServersJsonAsync();
            _rtcHost.TurnServers = PadTurn.ToRtcIceServers(turnServersJson);
            await _signaling.StartAsync(turnServersJson);
            // QR tylko otwiera strone pada - gracz sam wybiera te gre z listy. QR pojawia sie dopiero, gdy lobby istnieje,
            // zeby gra byla juz na liscie, kiedy ktos go zeskanuje.
            JoinUrl = PadPageUrl;
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
        PlayerSkin.OnSkinsChanged -= HandleSkinsChanged;
        _signaling?.Stop();
        _rtcHost?.Dispose();

        if (_markerSprite != null)
        {
            Destroy(_markerSprite.texture);
            Destroy(_markerSprite);
        }

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

        if (_isSkinStateDirty)
        {
            _isSkinStateDirty = false;

            foreach (PadPlayer padPlayer in _padPlayers)
            {
                SendSkinState(padPlayer);
            }
        }
    }

    private void HandleSkinsChanged()
    {
        _isSkinStateDirty = true;
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

        // Nowa strona w telefonie nic nie wie - wysylamy wybor postaci i stan budowania od nowa.
        padPlayer.SentSkinState = null;
        padPlayer.SentBuildState = null;
        padPlayer.SentPoolState = null;
        _isSkinStateDirty = true;
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

        if (padPlayer.AppliedSkinRequestCount != session.SkinRequestCount && padPlayer.PlayerObject.TryGetComponent(out PlayerSkin playerSkin))
        {
            padPlayer.AppliedSkinRequestCount = session.SkinRequestCount;
            playerSkin.RequestSkin(playerSkin.FindSkinIndex(session.RequestedSkin));
            // Zajeta postac zostaje odrzucona - telefon i tak musi dostac aktualny stan, zeby cofnac swoj wybor.
            padPlayer.SentSkinState = null;
            _isSkinStateDirty = true;
        }

        SendBuildState(padPlayer);
    }

    // q|chleb:-,noz:m,lod:x - pula rundy (wolny / moj / czyjs); b|idObiektu|0/1 - co gracz wzial i czy juz postawil.
    // Telefon rysuje obrazki obiektow po identyfikatorze (docs/pad/items/<id>.png).
    private void SendBuildState(PadPlayer padPlayer)
    {
        PlacementManager manager = PlacementManager.Instance;

        if (manager == null || !PlacementManager.IsPlacing || !padPlayer.PlayerObject.TryGetComponent(out PlayerBuilder builder))
        {
            return;
        }

        var pool = new StringBuilder("q|");

        for (int i = 0; i < manager.PoolCount; i++)
        {
            PoolSlot slot = manager.GetPoolSlot(i);
            char owner = !slot.IsTaken ? '-' : slot.TakenBy == builder.NetworkObjectId ? 'm' : 'x';
            pool.Append(i > 0 ? "," : string.Empty).Append(manager.Items[slot.ItemIndex].Id).Append(':').Append(owner);
        }

        SendIfChanged(padPlayer.Session, pool.ToString(), ref padPlayer.SentPoolState);

        string itemId = builder.CurrentItem != null ? builder.CurrentItem.Id : string.Empty;
        SendIfChanged(padPlayer.Session, $"b|{itemId}|{(builder.HasPlaced ? 1 : 0)}", ref padPlayer.SentBuildState);
    }

    private void SendIfChanged(PadSession session, string message, ref string sentMessage)
    {
        if (message != sentMessage)
        {
            sentMessage = message;
            _hub.Send(session, message);
        }
    }

    // k|mojaPostac|postacie,zajete,przez,innych - telefon na tej podstawie blokuje zajete kafelki.
    private void SendSkinState(PadPlayer padPlayer)
    {
        if (padPlayer.PlayerObject == null || !padPlayer.PlayerObject.TryGetComponent(out PlayerSkin playerSkin) || playerSkin.CurrentSkin == null)
        {
            return;
        }

        IEnumerable<string> takenSkins = PlayerSkin.SpawnedSkins
            .Where(other => other != playerSkin && other.CurrentSkin != null)
            .Select(other => other.CurrentSkin.Id)
            .Distinct();
        string state = $"k|{playerSkin.CurrentSkin.Id}|{string.Join(",", takenSkins)}";

        if (state != padPlayer.SentSkinState)
        {
            padPlayer.SentSkinState = state;
            _hub.Send(padPlayer.Session, state);
        }
    }

    private void SpawnPlayer(PadPlayer padPlayer)
    {
        GameObject playerPrefab = NetworkManager.Singleton.NetworkConfig.PlayerPrefab;
        Vector3 position = playerPrefab.transform.position + Vector3.right * SpawnSpacing * padPlayer.Number;
        GameObject instance = Instantiate(playerPrefab, position, Quaternion.identity);
        instance.AddComponent<PadInput>().Bind(padPlayer.Session);
        AddMarker(instance, PadColors[(padPlayer.Number - 1) % PadColors.Length]);

        // Obiekt nalezy do serwera; destroyWithScene: false - postac przechodzi z lobby do gry jak zwykly gracz.
        padPlayer.PlayerObject = instance.GetComponent<NetworkObject>();
        padPlayer.PlayerObject.Spawn(destroyWithScene: false);
        padPlayer.AppliedName = null;
        padPlayer.AppliedReady = false;
        padPlayer.AppliedSkinRequestCount = 0;
        padPlayer.SentSkinState = null;
        padPlayer.SentBuildState = null;
        padPlayer.SentPoolState = null;
    }

    // Trojkat w kolorze telefonu nad glowa postaci - ten sam kolor co na ekranie telefonu.
    private void AddMarker(GameObject player, Color color)
    {
        if (_markerSprite == null)
        {
            _markerSprite = CreateMarkerSprite();
        }

        var marker = new GameObject("PadMarker");
        marker.transform.SetParent(player.transform, false);
        marker.transform.localPosition = Vector3.up * MarkerHeight;

        SpriteRenderer markerRenderer = marker.AddComponent<SpriteRenderer>();
        markerRenderer.sprite = _markerSprite;
        markerRenderer.color = color;

        if (player.TryGetComponent(out SpriteRenderer playerRenderer))
        {
            markerRenderer.sortingLayerID = playerRenderer.sortingLayerID;
            markerRenderer.sortingOrder = playerRenderer.sortingOrder + 1;
        }
    }

    // 5x3 piksele, grotem w dol; bialy, kolor nadaje SpriteRenderer.
    private static Sprite CreateMarkerSprite()
    {
        const int width = 5;
        const int height = 3;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool isFilled = Mathf.Abs(x - width / 2) <= y;
                texture.SetPixel(x, y, isFilled ? Color.white : Color.clear);
            }
        }

        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0f), 32f);
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
        if (SceneManager.GetActiveScene().name != SceneNames.Game)
        {
            return PhaseLobby;
        }

        return PlacementManager.IsPlacing ? PhaseBuild : PhaseGame;
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
