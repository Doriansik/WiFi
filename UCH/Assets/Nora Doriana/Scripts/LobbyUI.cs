using System.Linq;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#region Lobby UI
public class LobbyUI : MonoBehaviour
{
    private const string WaitingForHostText = "Czekanie na hosta...";
    private const string WaitingForPlayersText = "Czekanie az wszyscy beda gotowi...";

    [SerializeField] private TMP_Text _playerListText;
    [SerializeField] private Button _startButton;
    [SerializeField] private Button _readyButton;
    [SerializeField] private Button _leaveButton;

    private bool _isLoadingGame;
    private bool _isLeaving;

    private void Awake()
    {
        _startButton.onClick.AddListener(OnStartClicked);
        _leaveButton.onClick.AddListener(OnLeaveClicked);
    }

    private void Start()
    {
        // Scena lobby odpalona bezposrednio w edytorze - bez sieci nie ma lobby.
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            LeaveToMainMenu();
            return;
        }

        LobbyPlayer.OnPlayerListChanged += Refresh;
        NetworkManager.Singleton.OnClientStopped += HandleClientStopped;
        Refresh();
    }

    private void OnDestroy()
    {
        _startButton.onClick.RemoveListener(OnStartClicked);
        _leaveButton.onClick.RemoveListener(OnLeaveClicked);
        LobbyPlayer.OnPlayerListChanged -= Refresh;

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientStopped -= HandleClientStopped;
        }
    }

    // Host wyszedl albo polaczenie padlo.
    private void HandleClientStopped(bool wasHost)
    {
        LeaveToMainMenu();
    }

    private void Refresh()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        // Host ma tylko START, klienci tylko READY.
        _startButton.gameObject.SetActive(networkManager.IsServer);
        _readyButton.gameObject.SetActive(!networkManager.IsServer);
        _startButton.interactable = !_isLoadingGame && AreAllClientsReady();
        _playerListText.text = BuildPlayerList(networkManager);
    }

    private static bool AreAllClientsReady()
    {
        return LobbyPlayer.SpawnedPlayers
            .Where(player => !player.IsHostPlayer)
            .All(player => player.GetReadyState());
    }

    private string BuildPlayerList(NetworkManager networkManager)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine($"Gracze ({LobbyPlayer.SpawnedPlayers.Count}):");

        // Pady z telefonow naleza do serwera (to samo OwnerClientId co host), wiec numerujemy po kolei.
        int number = 1;

        foreach (LobbyPlayer player in LobbyPlayer.SpawnedPlayers.OrderBy(player => player.OwnerClientId).ThenBy(player => player.IsPadPlayer))
        {
            builder.Append($"{number++}. {player.GetDeviceName()}");

            if (player.IsHostPlayer)
            {
                builder.Append(" (Host)");
            }
            else
            {
                builder.Append(player.IsPadPlayer ? " (telefon)" : string.Empty);
                builder.Append(player.GetReadyState() ? " - gotowy" : " - niegotowy");
            }

            if (player.OwnerClientId == networkManager.LocalClientId && !player.IsPadPlayer)
            {
                builder.Append(" (Ty)");
            }

            builder.AppendLine();
        }

        if (!networkManager.IsServer)
        {
            builder.AppendLine();
            builder.Append(WaitingForHostText);
        }
        else if (!AreAllClientsReady())
        {
            builder.AppendLine();
            builder.Append(WaitingForPlayersText);
        }

        return builder.ToString();
    }

    private void OnLeaveClicked()
    {
        LeaveToMainMenu();
    }

    private void LeaveToMainMenu()
    {
        if (_isLeaving)
        {
            return;
        }

        _isLeaving = true;

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientStopped -= HandleClientStopped;
        }

        GameSession.ReturnToMainMenu();
    }

    private async void OnStartClicked()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (!networkManager.IsServer || !AreAllClientsReady())
        {
            return;
        }

        _isLoadingGame = true;
        _startButton.interactable = false;

        SceneEventProgressStatus status = networkManager.SceneManager.LoadScene(SceneNames.Game, LoadSceneMode.Single);

        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError($"Nie udało się załadować sceny '{SceneNames.Game}': {status}");
            _isLoadingGame = false;
            _startButton.interactable = true;
            return;
        }

        // Gra wystartowala - usuwamy lobby, zeby nikt nie dolaczyl w trakcie meczu
        // i zeby po powrocie do menu nie zostalo martwe lobby na liscie.
        MatchmakingManager matchmaking = GameSession.Matchmaking;

        if (matchmaking != null)
        {
            await matchmaking.LeaveSessionAsync();
        }
    }
}
#endregion
