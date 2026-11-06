using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.Networking;

#region Pad Turn Config
// Assets/Resources/PadTurn.json - NIE trafia do repo (repo jest publiczne), wzor w UCH/PadTurn.example.json.
//   Cloudflare: credentialsUrl = https://rtc.live.cloudflare.com/v1/turn/keys/<ID klucza>/credentials/generate-ice-servers
//               apiToken       = token API tego klucza TURN
//   Metered:    credentialsUrl = https://<aplikacja>.metered.live/api/v1/turn/credentials?apiKey=<klucz>
//               apiToken       = puste
[Serializable]
public sealed class PadTurnConfig
{
    public string credentialsUrl;
    public string apiToken;
    public int ttlSeconds = 86400;
}
#endregion

#region Pad Turn
// Serwer TURN (przekaznik) - bez niego telefon w innej sieci niz komputer (dane komorkowe, inne Wi-Fi) czesto sie nie polaczy,
// bo routery i operatorzy nie przepuszczaja bezposrednich polaczen. Host pobiera krotkotrwale dane logowania
// i podaje je telefonom w danych lobby (widoczne tylko dla graczy, ktorzy dolaczyli).
public static class PadTurn
{
    private const string ConfigResource = "PadTurn";
    private const int MaxJsonLength = 1500;  // pole Lobby ma 2048 znakow
    private const int TimeoutSeconds = 8;

    // Lista serwerow TURN w formacie przegladarki (JSON) albo null, gdy TURN nie jest skonfigurowany lub nie odpowiada.
    public static async Task<string> FetchIceServersJsonAsync()
    {
        TextAsset asset = Resources.Load<TextAsset>(ConfigResource);

        if (asset == null)
        {
            Debug.Log("Pady: brak Resources/PadTurn.json - telefony polacza sie tylko w tej samej sieci (albo przez STUN, jesli router pozwoli).");
            return null;
        }

        PadTurnConfig config = JsonUtility.FromJson<PadTurnConfig>(asset.text);

        if (config == null || string.IsNullOrWhiteSpace(config.credentialsUrl))
        {
            Debug.LogWarning("Pady: PadTurn.json nie ma credentialsUrl - TURN wylaczony.");
            return null;
        }

        try
        {
            string response = await RequestAsync(config);
            string json = Normalize(response);
            Debug.Log(json != null ? $"Pady: TURN gotowy - telefony moga dolaczac z innych sieci ({json.Length} znakow)." : "Pady: TURN nie zwrocil zadnego serwera.");
            return json;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Pady: nie udalo sie pobrac danych TURN ({exception.Message}) - tylko polaczenia lokalne.");
            return null;
        }
    }

    public static RTCIceServer[] ToRtcIceServers(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return Array.Empty<RTCIceServer>();
        }

        return JArray.Parse(json).Select(server => new RTCIceServer
        {
            urls = server["urls"].ToObject<string[]>(),
            username = (string)server["username"],
            credential = (string)server["credential"],
            credentialType = RTCIceCredentialType.Password
        }).ToArray();
    }

    private static async Task<string> RequestAsync(PadTurnConfig config)
    {
        bool usesToken = !string.IsNullOrEmpty(config.apiToken);

        // Cloudflare: POST z tokenem i czasem waznosci; Metered: zwykly GET z kluczem w adresie.
        using UnityWebRequest request = usesToken
            ? new UnityWebRequest(config.credentialsUrl, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes($"{{\"ttl\":{config.ttlSeconds}}}")),
                downloadHandler = new DownloadHandlerBuffer()
            }
            : UnityWebRequest.Get(config.credentialsUrl);

        request.timeout = TimeoutSeconds;

        if (usesToken)
        {
            request.SetRequestHeader("Authorization", "Bearer " + config.apiToken);
            request.SetRequestHeader("Content-Type", "application/json");
        }

        var completion = new TaskCompletionSource<bool>();
        request.SendWebRequest().completed += _ => completion.TrySetResult(true);
        await completion.Task;

        if (request.result != UnityWebRequest.Result.Success)
        {
            throw new Exception($"{request.responseCode} {request.error}");
        }

        return request.downloadHandler.text;
    }

    // Dostawcy zwracaja rozne ksztalty: {iceServers: [...]}, {iceServers: {...}} albo sama tablice.
    // Zostawiamy same adresy turn/turns (STUN telefon i host dodaja sami) bez portu 53, ktory przegladarki blokuja.
    private static string Normalize(string response)
    {
        JToken root = JToken.Parse(response);
        JToken servers = root is JObject rootObject && rootObject["iceServers"] != null ? rootObject["iceServers"] : root;
        JArray serverArray = servers as JArray ?? new JArray(servers);
        var result = new List<(List<string> Urls, string Username, string Credential)>();

        foreach (JToken server in serverArray)
        {
            JToken urls = server["urls"] ?? server["url"];

            if (urls == null)
            {
                continue;
            }

            List<string> turnUrls = (urls is JArray urlArray ? urlArray.Select(url => (string)url) : new[] { (string)urls })
                .Where(url => url != null && url.StartsWith("turn", StringComparison.OrdinalIgnoreCase) && !url.Contains(":53"))
                .ToList();

            if (turnUrls.Count > 0)
            {
                result.Add((turnUrls, (string)server["username"], (string)server["credential"]));
            }
        }

        if (result.Count == 0)
        {
            return null;
        }

        string json = Serialize(result);

        // Za dlugie dla pola Lobby - zdejmujemy adresy od konca najdluzszej listy (zostaje UDP, potem TCP/TLS).
        while (json.Length > MaxJsonLength && result.Any(server => server.Urls.Count > 1))
        {
            List<string> longest = result.OrderByDescending(server => server.Urls.Count).First().Urls;
            longest.RemoveAt(longest.Count - 1);
            json = Serialize(result);
        }

        return json.Length <= MaxJsonLength ? json : null;
    }

    private static string Serialize(List<(List<string> Urls, string Username, string Credential)> servers)
    {
        var array = new JArray(servers.Select(server => new JObject
        {
            ["urls"] = new JArray(server.Urls),
            ["username"] = server.Username,
            ["credential"] = server.Credential
        }));
        return array.ToString(Newtonsoft.Json.Formatting.None);
    }
}
#endregion
