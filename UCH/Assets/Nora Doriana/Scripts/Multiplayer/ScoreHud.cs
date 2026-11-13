using Unity.Netcode;
using UnityEngine;

#region Score HUD
public class ScoreHud : MonoBehaviour
{
    private const float BaseBoxWidth = 230f;
    private const float BaseRowHeight = 35f;
    private const float BasePadding = 15f;
    private const float BaseOffset = 10f;
    private const float MinScale = 1.5f;
    private const float ReferenceScreenHeight = 720f;
    private const float HeaderHeightOffset = 20f;
    private const float DepthZ = 1f;

    private void OnGUI()
    {
        if (ScoreManager.Instance == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient)
        {
            return;
        }

        var scores = ScoreManager.Instance.PlayerScores;
        int playerCount = scores.Count;

        if (playerCount == 0)
        {
            return;
        }

        float scale = Mathf.Max(MinScale, Screen.height / ReferenceScreenHeight);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, DepthZ));

        float scaledScreenWidth = Screen.width / scale;

        float totalHeight = (playerCount * BaseRowHeight) + (BasePadding * 2) + HeaderHeightOffset;
        float startX = scaledScreenWidth - BaseBoxWidth - BaseOffset;
        float startY = BaseOffset;

        GUI.Box(new Rect(startX, startY, BaseBoxWidth, totalHeight), "Wyniki Graczy");

        for (int i = 0; i < playerCount; i++)
        {
            ScoreManager.PlayerScore playerScore = scores[i];

            float rowY = startY + BasePadding + HeaderHeightOffset + (i * BaseRowHeight);

            Rect labelRect = new Rect(startX + BasePadding, rowY, BaseBoxWidth - (BasePadding * 2), BaseRowHeight);

            string playerLabel = $"{GetPlayerName(playerScore.PlayerObjectId, i)}: {playerScore.Score}";

            GUI.Label(labelRect, playerLabel);
        }
    }

    // Nazwa z lobby (wpisana na telefonie / nazwa urzadzenia), "(Ty)" tylko dla wlasnej postaci na tym urzadzeniu.
    private static string GetPlayerName(ulong playerObjectId, int index)
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (!networkManager.SpawnManager.SpawnedObjects.TryGetValue(playerObjectId, out NetworkObject playerObject)
            || !playerObject.TryGetComponent(out LobbyPlayer lobbyPlayer))
        {
            return $"Gracz {index + 1}";
        }

        string name = lobbyPlayer.GetDeviceName();

        if (string.IsNullOrEmpty(name))
        {
            name = $"Gracz {index + 1}";
        }

        bool isLocal = playerObject.OwnerClientId == networkManager.LocalClientId && !lobbyPlayer.IsPadPlayer;
        return isLocal ? $"{name} (Ty)" : name;
    }
}
#endregion