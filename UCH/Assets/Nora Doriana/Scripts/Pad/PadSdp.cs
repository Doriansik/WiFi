using System;
using System.Collections.Generic;
using System.Linq;

#region Pad SDP
// Opisy polaczen WebRTC (SDP) przesylane przez Lobby. Pole Lobby miesci max 2048 znakow.
//   oferta (dane gracza "o"):   nonce|sdp
//   odpowiedz (dane lobby "aN"): idGracza|nonce|sdp
public static class PadSdp
{
    public const int MaxLobbyValueLength = 2048;
    public const string OfferKey = "o";
    public const string AnswerKeyPrefix = "a";
    public const string TurnKey = "ice";  // dane lobby: serwery TURN w formacie przegladarki (JSON)

    public static bool TryParseOffer(string value, out string nonce, out string sdp)
    {
        nonce = null;
        sdp = null;
        int separator = value?.IndexOf('|') ?? -1;

        if (separator <= 0)
        {
            return false;
        }

        nonce = value.Substring(0, separator);
        sdp = value.Substring(separator + 1);
        return sdp.Length > 0;
    }

    public static string FormatAnswer(string playerId, string nonce, string sdp)
    {
        string prefix = $"{playerId}|{nonce}|";
        return prefix + FitCandidates(sdp, MaxLobbyValueLength - prefix.Length);
    }

    // Przegladarki ukrywaja adres IP telefonu pod losowa nazwa "xxxx.local" (mDNS).
    public static IReadOnlyList<string> GetMdnsHostnames(string sdp)
    {
        return GetCandidateLines(sdp)
            .Select(line => SplitCandidate(line)[4])
            .Where(address => address.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();
    }

    // Host sam tlumaczy nazwy .local na adresy - wtedy to on pierwszy wysyla pakiet do telefonu,
    // a zapora Windows przepuszcza odpowiedz bez zadnych regul.
    public static string ReplaceHostnames(string sdp, IReadOnlyDictionary<string, string> addresses)
    {
        return string.Join("\r\n", SplitLines(sdp).Select(line =>
        {
            if (!IsCandidateLine(line))
            {
                return line;
            }

            string[] fields = SplitCandidate(line);
            return addresses.TryGetValue(fields[4], out string address) ? ReplaceField(line, 4, address) : line;
        })) + "\r\n";
    }

    // Za dlugie SDP (duzo kart sieciowych) - wyrzucamy kandydatow, ktorzy w sieci lokalnej i tak nie pomoga.
    public static string FitCandidates(string sdp, int maxLength)
    {
        List<string> lines = SplitLines(sdp).ToList();
        var dropOrder = new Func<string, bool>[]
        {
            line => SplitCandidate(line)[2].Equals("tcp", StringComparison.OrdinalIgnoreCase),
            line => SplitCandidate(line)[4].Contains(":"),  // IPv6
            line => SplitCandidate(line)[4].StartsWith("169.254."),  // link-local
            line => SplitCandidate(line)[7] == "srflx"
        };

        foreach (Func<string, bool> shouldDrop in dropOrder)
        {
            if (JoinLength(lines) <= maxLength)
            {
                break;
            }

            lines.RemoveAll(line => IsCandidateLine(line) && shouldDrop(line));
        }

        return string.Join("\r\n", lines) + "\r\n";
    }

    private static int JoinLength(List<string> lines)
    {
        return lines.Sum(line => line.Length + 2);
    }

    private static IEnumerable<string> GetCandidateLines(string sdp)
    {
        return SplitLines(sdp).Where(IsCandidateLine);
    }

    private static IEnumerable<string> SplitLines(string sdp)
    {
        return sdp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
    }

    // a=candidate:<fundacja> <komponent> <protokol> <priorytet> <adres> <port> typ <typ> ...
    private static bool IsCandidateLine(string line)
    {
        return line.StartsWith("a=candidate:") && SplitCandidate(line).Length >= 8;
    }

    private static string[] SplitCandidate(string line)
    {
        return line.Split(' ');
    }

    private static string ReplaceField(string line, int index, string value)
    {
        string[] fields = SplitCandidate(line);
        fields[index] = value;
        return string.Join(" ", fields);
    }
}
#endregion
