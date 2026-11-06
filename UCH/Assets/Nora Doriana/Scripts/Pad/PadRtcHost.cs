using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Unity.WebRTC;
using UnityEngine;

#region Pad RTC Host
// Polaczenia WebRTC z telefonami. Telefon wysyla oferte (przez Lobby), host odpowiada - dalej dane ida bezposrednio po Wi-Fi.
// Host nie nasluchuje na zadnym porcie: to on wysyla pierwszy pakiet do telefonu, wiec zapora Windows nie wymaga regul.
public sealed class PadRtcHost : IDisposable
{
    private const float IceGatheringTimeoutSeconds = 3f;
    private const float MdnsTimeoutSeconds = 2f;
    private const string StunServer = "stun:stun.l.google.com:19302";  // zapas, gdy nazwa .local telefonu sie nie rozwiaze
    private const string ControlChannelLabel = "ctl";
    private const string InputChannelLabel = "in";

    private readonly PadHub _hub;
    private readonly Dictionary<string, RtcPadLink> _links = new Dictionary<string, RtcPadLink>();

    // Przekaznik TURN dla telefonow z innych sieci (PadTurn) - pusty, gdy nie skonfigurowany.
    public RTCIceServer[] TurnServers { get; set; } = Array.Empty<RTCIceServer>();

    public PadRtcHost(PadHub hub)
    {
        _hub = hub;
    }

    public void Dispose()
    {
        foreach (RtcPadLink link in _links.Values)
        {
            link.Close();
        }

        _links.Clear();
    }

    // Zwraca SDP odpowiedzi przez onAnswer albo null, gdy sie nie udalo.
    public IEnumerator AnswerOffer(string playerId, string offerSdp, Action<string> onAnswer)
    {
        string resolvedOffer = offerSdp;
        yield return ResolveMdnsHostnames(offerSdp, sdp => resolvedOffer = sdp);

        // Nowa oferta od tego samego telefonu (odswiezona strona, ponowne laczenie) - stare polaczenie idzie do kosza.
        if (_links.TryGetValue(playerId, out RtcPadLink previous))
        {
            _hub.HandleLinkClosed(previous);
            previous.Close();
        }

        var configuration = new RTCConfiguration
        {
            iceServers = new[] { new RTCIceServer { urls = new[] { StunServer } } }.Concat(TurnServers).ToArray()
        };
        var link = new RtcPadLink(_hub, new RTCPeerConnection(ref configuration), PadSignaling.ShortId(playerId));
        _links[playerId] = link;
        RTCPeerConnection peerConnection = link.PeerConnection;

        var offer = new RTCSessionDescription { type = RTCSdpType.Offer, sdp = resolvedOffer };
        RTCSetSessionDescriptionAsyncOperation setRemote = peerConnection.SetRemoteDescription(ref offer);
        yield return setRemote;

        if (link.IsClosed || setRemote.IsError)
        {
            Fail(playerId, link, "oferta", setRemote.Error.message, onAnswer);
            yield break;
        }

        RTCSessionDescriptionAsyncOperation createAnswer = peerConnection.CreateAnswer();
        yield return createAnswer;

        if (link.IsClosed || createAnswer.IsError)
        {
            Fail(playerId, link, "odpowiedz", createAnswer.Error.message, onAnswer);
            yield break;
        }

        RTCSessionDescription answer = createAnswer.Desc;
        RTCSetSessionDescriptionAsyncOperation setLocal = peerConnection.SetLocalDescription(ref answer);
        yield return setLocal;

        if (link.IsClosed || setLocal.IsError)
        {
            Fail(playerId, link, "lokalny opis", setLocal.Error.message, onAnswer);
            yield break;
        }

        // Odpowiedz idzie przez Lobby w calosci (bez "trickle ICE"), wiec czekamy na zebranie kandydatow.
        float deadline = Time.unscaledTime + IceGatheringTimeoutSeconds;

        while (!link.IsClosed && peerConnection.GatheringState != RTCIceGatheringState.Complete && Time.unscaledTime < deadline)
        {
            yield return null;
        }

        if (link.IsClosed)
        {
            onAnswer(null);
            yield break;
        }

        string answerSdp = peerConnection.LocalDescription.sdp;
        Debug.Log($"Pady: odpowiedz WebRTC dla {PadSignaling.ShortId(playerId)}, kandydaci hosta: {DescribeCandidates(answerSdp)}, telefonu: {DescribeCandidates(resolvedOffer)}");
        onAnswer(answerSdp);
    }

    // Do logu: "host 192.168.1.7:51923, srflx 46.1.2.3:62927" - od razu widac, czy telefon jest w tej samej sieci.
    private static string DescribeCandidates(string sdp)
    {
        IEnumerable<string> candidates = sdp.Split('\n')
            .Where(line => line.StartsWith("a=candidate:"))
            .Select(line => line.Trim().Split(' '))
            .Where(fields => fields.Length >= 8)
            .Select(fields => $"{fields[7]} {fields[4]}:{fields[5]}");
        string result = string.Join(", ", candidates);
        return result.Length > 0 ? result : "brak";
    }

    public void Forget(string playerId)
    {
        if (_links.TryGetValue(playerId, out RtcPadLink link))
        {
            link.Close();
            _links.Remove(playerId);
        }
    }

    private void Fail(string playerId, RtcPadLink link, string step, string error, Action<string> onAnswer)
    {
        if (!link.IsClosed)
        {
            Debug.LogWarning($"Pady: WebRTC nie powiodlo sie ({step}): {error}");
            link.Close();
        }

        if (_links.TryGetValue(playerId, out RtcPadLink current) && current == link)
        {
            _links.Remove(playerId);
        }

        onAnswer(null);
    }

    private static IEnumerator ResolveMdnsHostnames(string sdp, Action<string> onResolved)
    {
        IReadOnlyList<string> hostnames = PadSdp.GetMdnsHostnames(sdp);

        if (hostnames.Count == 0)
        {
            onResolved(sdp);
            yield break;
        }

        // Windows rozwiazuje nazwy .local przez mDNS - tak samo jak przegladarka po drugiej stronie.
        Dictionary<string, Task<IPAddress[]>> lookups = hostnames.ToDictionary(name => name, name => Dns.GetHostAddressesAsync(name));
        float deadline = Time.unscaledTime + MdnsTimeoutSeconds;

        while (lookups.Values.Any(task => !task.IsCompleted) && Time.unscaledTime < deadline)
        {
            yield return null;
        }

        var addresses = new Dictionary<string, string>();

        foreach (KeyValuePair<string, Task<IPAddress[]>> lookup in lookups)
        {
            IPAddress address = lookup.Value.Status == TaskStatus.RanToCompletion
                ? lookup.Value.Result.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                : null;

            if (address != null)
            {
                addresses[lookup.Key] = address.ToString();
                Debug.Log($"Pady: adres telefonu {lookup.Key} -> {address}");
            }
            else
            {
                Debug.LogWarning($"Pady: nie udalo sie rozwiazac {lookup.Key} - zostaje polaczenie przez STUN.");
            }
        }

        onResolved(PadSdp.ReplaceHostnames(sdp, addresses));
    }

    #region RTC Pad Link
    private sealed class RtcPadLink : IPadLink
    {
        private readonly PadHub _hub;
        private readonly string _logName;
        private RTCDataChannel _controlChannel;
        private RTCDataChannel _inputChannel;
        private bool _hasLoggedFirstMessage;
        private readonly Queue<string> _pendingControl = new Queue<string>();
        private const int MaxPendingMessages = 64;

        public RTCPeerConnection PeerConnection { get; }
        public bool IsClosed { get; private set; }

        public RtcPadLink(PadHub hub, RTCPeerConnection peerConnection, string logName)
        {
            _hub = hub;
            _logName = logName;
            PeerConnection = peerConnection;
            PeerConnection.OnDataChannel = HandleDataChannel;
            PeerConnection.OnConnectionStateChange = HandleConnectionStateChange;
            PeerConnection.OnIceConnectionChange = state => Debug.Log($"Pady: {_logName} ICE: {state}");
        }

        // Tekst zawsze jako bajty UTF-8 - Send(string) w Unity WebRTC wysyla ANSI.
        // Na wolniejszym laczu (dane komorkowe) host potrafi chciec wyslac powitanie, zanim jego strona kanalu sterowania
        // sie otworzy - wtedy wiadomosc czeka w kolejce zamiast przepasc (telefon stalby na "Odbieranie stanu gry").
        public void Send(string message)
        {
            if (IsClosed)
            {
                return;
            }

            if (IsOpen(_controlChannel))
            {
                FlushPending();
                _controlChannel.Send(Encoding.UTF8.GetBytes(message));
            }
            else if (_pendingControl.Count < MaxPendingMessages)
            {
                _pendingControl.Enqueue(message);
            }
        }

        private void FlushPending()
        {
            if (_pendingControl.Count > 0)
            {
                Debug.Log($"Pady: {_logName} kanal sterowania otwarty - wysylam {_pendingControl.Count} zaleglych wiadomosci");
            }

            while (_pendingControl.Count > 0 && !IsClosed && IsOpen(_controlChannel))
            {
                _controlChannel.Send(Encoding.UTF8.GetBytes(_pendingControl.Dequeue()));
            }
        }

        // Pong idzie kanalem bez retransmisji, zeby pomiar opoznienia pokazywal prawdziwe Wi-Fi.
        public void SendFast(string message)
        {
            SendOn(IsOpen(_inputChannel) ? _inputChannel : _controlChannel, message);
        }

        public void Close()
        {
            if (IsClosed)
            {
                return;
            }

            IsClosed = true;
            PeerConnection.OnDataChannel = null;
            PeerConnection.OnConnectionStateChange = null;
            PeerConnection.OnIceConnectionChange = null;
            _controlChannel?.Dispose();
            _inputChannel?.Dispose();
            PeerConnection.Close();
            PeerConnection.Dispose();
        }

        private void HandleDataChannel(RTCDataChannel channel)
        {
            if (channel.Label == ControlChannelLabel)
            {
                _controlChannel = channel;
                channel.OnClose = () => _hub.HandleLinkClosed(this);
                channel.OnOpen = FlushPending;
            }
            else if (channel.Label == InputChannelLabel)
            {
                _inputChannel = channel;
            }
            else
            {
                return;
            }

            Debug.Log($"Pady: {_logName} kanal '{channel.Label}' otwarty");

            channel.OnMessage = bytes =>
            {
                if (IsClosed)
                {
                    return;
                }

                FlushPending();  // skoro cos doszlo, kanal juz dziala - wysylamy, co czekalo
                string message = Encoding.UTF8.GetString(bytes);

                // Diagnostyka: pierwsza wiadomosc kanalem sterowania to powinno byc powitanie "h|...".
                if (channel == _controlChannel && !_hasLoggedFirstMessage)
                {
                    _hasLoggedFirstMessage = true;
                    Debug.Log($"Pady: {_logName} pierwsza wiadomosc od telefonu: {message.Split('|')[0]}");
                }

                _hub.HandleMessage(this, message, Time.unscaledTimeAsDouble);
            };
        }

        private void HandleConnectionStateChange(RTCPeerConnectionState state)
        {
            Debug.Log($"Pady: {_logName} polaczenie: {state}");

            if (state == RTCPeerConnectionState.Failed || state == RTCPeerConnectionState.Closed)
            {
                _hub.HandleLinkClosed(this);
            }
        }

        private static bool IsOpen(RTCDataChannel channel)
        {
            return channel != null && channel.ReadyState == RTCDataChannelState.Open;
        }

        private void SendOn(RTCDataChannel channel, string message)
        {
            if (!IsClosed && IsOpen(channel))
            {
                channel.Send(Encoding.UTF8.GetBytes(message));
            }
        }
    }
    #endregion
}
#endregion
