using System;
using System.Collections.Generic;
using System.Text;

#region Pad Link
// Polaczenie z jednym telefonem (u nas WebRTC). Send - kanal niezawodny, SendFast - kanal bez retransmisji.
public interface IPadLink
{
    void Send(string message);
    void SendFast(string message);
    void Close();
}
#endregion

#region Pad Command
public enum PadCommandType
{
    MoveCursor,
    PickItem,
    Rotate,
    Place
}

// Polecenie budowania z telefonu - idzie kanalem niezawodnym, wiec zaden krok kursora nie ginie.
public struct PadCommand
{
    public PadCommandType Type;
    public int X;
    public int Y;
}
#endregion

#region Pad Session
// Stan jednego telefonu-pada. Wszystko dzieje sie na watku glownym Unity (WebRTC dostarcza wiadomosci w Update).
public sealed class PadSession
{
    public const int ButtonLeft = 1;
    public const int ButtonRight = 2;
    public const int ButtonJump = 4;
    public const int AllButtons = ButtonLeft | ButtonRight | ButtonJump;
    private const int MaxQueuedCommands = 32;

    private readonly Queue<PadCommand> _commands = new Queue<PadCommand>();

    private int _jumpPresses;
    private int _lastInputSequence = -1;
    private int _lastJumpCount = -1;

    public string Id { get; }
    public string Name { get; internal set; }
    public int Buttons { get; private set; }
    public bool IsReady { get; internal set; }
    public string RequestedSkin { get; private set; }  // identyfikator postaci wybranej na telefonie (null = przydziela host)
    public int SkinRequestCount { get; private set; }  // ponowny wybor tej samej postaci tez sie liczy (wczesniej mogla byc zajeta)
    public bool IsConnected => Link != null;

    internal IPadLink Link { get; private set; }
    internal double LastMessageTime { get; set; }

    internal PadSession(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public bool TryDequeueCommand(out PadCommand command)
    {
        if (_commands.Count == 0)
        {
            command = default;
            return false;
        }

        command = _commands.Dequeue();
        return true;
    }

    internal void EnqueueCommand(PadCommand command)
    {
        if (_commands.Count < MaxQueuedCommands)
        {
            _commands.Enqueue(command);
        }
    }

    internal void RequestSkin(string skinId)
    {
        RequestedSkin = skinId;
        SkinRequestCount++;
    }

    // Wcisniecie skoku czeka na odebranie - tap krotszy niz klatka gry nie ginie.
    public bool ConsumeJumpPress()
    {
        bool wasPressed = _jumpPresses > 0;
        _jumpPresses = 0;
        return wasPressed;
    }

    // Kanal wejscia jest bez retransmisji i bez kolejnosci: numer sekwencji odrzuca spoznione pakiety,
    // a licznik skokow z telefonu odzyskuje tap, nawet gdy pakiet z samym wcisnieciem zginal.
    internal void ApplyInput(int sequence, int buttons, int jumpCount)
    {
        if (sequence <= _lastInputSequence)
        {
            return;
        }

        _lastInputSequence = sequence;
        Buttons = buttons & AllButtons;

        if (_lastJumpCount >= 0 && jumpCount > _lastJumpCount)
        {
            _jumpPresses += jumpCount - _lastJumpCount;
        }

        _lastJumpCount = jumpCount;
    }

    internal IPadLink AttachLink(IPadLink link)
    {
        IPadLink previous = Link;
        Link = link;
        ResetInput();
        return previous;
    }

    internal bool DetachLink(IPadLink link)
    {
        if (Link != link)
        {
            return false;
        }

        Link = null;
        ResetInput();
        return true;
    }

    // Nowe polaczenie = nowa strona w telefonie, liczniki zaczynaja od zera.
    private void ResetInput()
    {
        Buttons = 0;
        _jumpPresses = 0;
        _commands.Clear();
        _lastInputSequence = -1;
        _lastJumpCount = -1;
    }
}
#endregion

#region Pad Events
public enum PadEventType
{
    Connected,
    Disconnected
}

public struct PadEvent
{
    public PadEventType Type;
    public PadSession Session;
}
#endregion

#region Pad Hub
// Protokol padow niezalezny od transportu. Wiadomosci "typ|dane":
//   telefon -> host: h|id|nazwa (hello), i|sekwencja|przyciski|licznikSkokow, r|0/1 (gotowy), n|nazwa, k|postac, p|czas (ping),
//                    budowanie: c|k|miejsce (wez obiekt z puli), c|m|dx|dy (kursor), c|r (obrot), c|p (postaw)
//   host -> telefon: w|numer|#kolor (powitanie), s|lobby/game/build (faza), k|mojaPostac|zajete,przez,innych,
//                    q|obiekt:-/m/x,... (pula: wolny / moj / czyjs), b|mojObiekt|0/1 (postawiony),
//                    f (brak miejsc), o|czas (pong)
public sealed class PadHub
{
    // Telefon pinguje co 0.5 s, wiec 5 s ciszy = zgubil siec albo zablokowal ekran.
    public const double LinkTimeoutSeconds = 5.0;

    private const int MaxIdLength = 64;
    private const int MaxNameLength = 20;
    private const string DefaultName = "Gracz";

    private readonly int _maxPads;
    private readonly Dictionary<string, PadSession> _sessions = new Dictionary<string, PadSession>();
    private readonly Dictionary<IPadLink, PadSession> _sessionsByLink = new Dictionary<IPadLink, PadSession>();
    private readonly Queue<PadEvent> _events = new Queue<PadEvent>();

    public PadHub(int maxPads)
    {
        _maxPads = maxPads;
    }

    public bool TryDequeueEvent(out PadEvent padEvent)
    {
        if (_events.Count == 0)
        {
            padEvent = default;
            return false;
        }

        padEvent = _events.Dequeue();
        return true;
    }

    public void Send(PadSession session, string message)
    {
        session.Link?.Send(message);
    }

    public void Broadcast(string message)
    {
        foreach (PadSession session in _sessions.Values)
        {
            session.Link?.Send(message);
        }
    }

    // Zwalnia miejsce po telefonie, ktory nie wrocil - kolejny moze dolaczyc.
    public void RemoveSession(PadSession session)
    {
        _sessions.Remove(session.Id);
        IPadLink link = session.Link;

        if (link != null)
        {
            _sessionsByLink.Remove(link);
            session.DetachLink(link);
            link.Close();
        }
    }

    public void HandleMessage(IPadLink link, string message, double now)
    {
        string[] parts = message.Split('|');
        _sessionsByLink.TryGetValue(link, out PadSession session);

        if (session != null)
        {
            session.LastMessageTime = now;
        }

        switch (parts[0])
        {
            case "h":
                if (session == null && parts.Length >= 3)
                {
                    TryAttach(link, parts[1], parts[2], now);
                }
                else if (session != null)
                {
                    // Telefon powtarza powitanie, bo nie dostal stanu gry - wysylamy go jeszcze raz.
                    _events.Enqueue(new PadEvent { Type = PadEventType.Connected, Session = session });
                }

                return;
            case "p":
                if (parts.Length >= 2)
                {
                    link.SendFast("o|" + parts[1]);
                }

                return;
        }

        if (session == null)
        {
            return;  // przed hello ignorujemy reszte
        }

        switch (parts[0])
        {
            case "i":
                if (parts.Length >= 4 && int.TryParse(parts[1], out int sequence) &&
                    int.TryParse(parts[2], out int buttons) && int.TryParse(parts[3], out int jumpCount))
                {
                    session.ApplyInput(sequence, buttons, jumpCount);
                }

                break;
            case "r":
                session.IsReady = parts.Length >= 2 && parts[1] == "1";
                break;
            case "n":
                session.Name = SanitizeName(parts.Length >= 2 ? parts[1] : null);
                break;
            case "k":
                if (parts.Length >= 2 && IsValidId(parts[1]))
                {
                    session.RequestSkin(parts[1]);
                }

                break;
            case "c":
                if (TryParseCommand(parts, out PadCommand command))
                {
                    session.EnqueueCommand(command);
                }

                break;
        }
    }

    public void HandleLinkClosed(IPadLink link)
    {
        if (_sessionsByLink.TryGetValue(link, out PadSession session))
        {
            _sessionsByLink.Remove(link);

            if (session.DetachLink(link))
            {
                _events.Enqueue(new PadEvent { Type = PadEventType.Disconnected, Session = session });
            }
        }
    }

    // Kanal WebRTC nie zawsze sie zamyka, gdy telefon zniknie - pilnujemy ciszy na laczu.
    public void CheckTimeouts(double now)
    {
        var silentLinks = new List<IPadLink>();

        foreach (KeyValuePair<IPadLink, PadSession> pair in _sessionsByLink)
        {
            if (now - pair.Value.LastMessageTime > LinkTimeoutSeconds)
            {
                silentLinks.Add(pair.Key);
            }
        }

        foreach (IPadLink link in silentLinks)
        {
            HandleLinkClosed(link);
            link.Close();
        }
    }

    private void TryAttach(IPadLink link, string id, string name, double now)
    {
        if (!IsValidId(id))
        {
            link.Close();
            return;
        }

        if (!_sessions.TryGetValue(id, out PadSession session))
        {
            if (_sessions.Count >= _maxPads)
            {
                link.Send("f");
                return;
            }

            session = new PadSession(id, SanitizeName(name));
            _sessions.Add(id, session);
        }
        else
        {
            session.Name = SanitizeName(name);
        }

        // Ten sam telefon wrocil (zerwane Wi-Fi, odswiezona strona) - podmieniamy lacze, postac zostaje ta sama.
        IPadLink previous = session.AttachLink(link);

        if (previous != null)
        {
            _sessionsByLink.Remove(previous);
            previous.Close();
        }

        _sessionsByLink[link] = session;
        session.LastMessageTime = now;
        _events.Enqueue(new PadEvent { Type = PadEventType.Connected, Session = session });
    }

    private static bool TryParseCommand(string[] parts, out PadCommand command)
    {
        command = default;

        if (parts.Length < 2)
        {
            return false;
        }

        switch (parts[1])
        {
            case "m":
                if (parts.Length >= 4 && int.TryParse(parts[2], out int dx) && int.TryParse(parts[3], out int dy))
                {
                    command = new PadCommand { Type = PadCommandType.MoveCursor, X = Math.Sign(dx), Y = Math.Sign(dy) };
                    return true;
                }

                return false;
            case "k":
                if (parts.Length >= 3 && int.TryParse(parts[2], out int slot) && slot >= 0)
                {
                    command = new PadCommand { Type = PadCommandType.PickItem, X = slot };
                    return true;
                }

                return false;
            case "r":
                command = new PadCommand { Type = PadCommandType.Rotate };
                return true;
            case "p":
                command = new PadCommand { Type = PadCommandType.Place };
                return true;
            default:
                return false;
        }
    }

    private static bool IsValidId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength)
        {
            return false;
        }

        foreach (char c in id)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static string SanitizeName(string name)
    {
        var builder = new StringBuilder();

        foreach (char c in name ?? string.Empty)
        {
            if (!char.IsControl(c) && c != '|' && c != '<' && c != '>' && builder.Length < MaxNameLength)
            {
                builder.Append(c);
            }
        }

        string result = builder.ToString().Trim();
        return result.Length > 0 ? result : DefaultName;
    }
}
#endregion
