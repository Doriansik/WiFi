using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#region Main Menu UI
public class MainMenuUI : MonoBehaviour
{
    [SerializeField] private Button _hostButton;
    [SerializeField] private Button _joinButton;
    [SerializeField] private Button _exitButton;
    [SerializeField] private MatchmakingManager _matchmakingManager;
    [SerializeField] private string _defaultSessionName = "Imprezowa Gra";

    private void Awake()
    {
        _hostButton.onClick.AddListener(OnHostClicked);
        _joinButton.onClick.AddListener(OnJoinClicked);

        // SampleScene (stara scena) nie ma przycisku EXIT.
        if (_exitButton != null)
        {
            _exitButton.onClick.AddListener(OnExitClicked);
        }

        bool isHostDevice = IsHostDevice();
        _hostButton.gameObject.SetActive(isHostDevice);
        _joinButton.gameObject.SetActive(!isHostDevice);
    }

    private void Start()
    {
        NetworkManager.Singleton.OnClientStopped += HandleClientStopped;
    }

    private void OnDestroy()
    {
        _hostButton.onClick.RemoveListener(OnHostClicked);
        _joinButton.onClick.RemoveListener(OnJoinClicked);

        if (_exitButton != null)
        {
            _exitButton.onClick.RemoveListener(OnExitClicked);
        }

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientStopped -= HandleClientStopped;
        }
    }

    // PC hostuje gre, telefony dolaczaja. Klon ParrelSync udaje telefon, zeby dalo sie testowac w edytorze.
    public static bool IsHostDevice()
    {
#if UNITY_EDITOR
        return !ParrelSync.ClonesManager.IsClone();
#else
        return !Application.isMobilePlatform;
#endif
    }

    private async void OnHostClicked()
    {
        if (!IsHostDevice())
        {
            return;
        }

        try
        {
            await _matchmakingManager.CreateSessionAsync(_defaultSessionName);
        }
        catch (Exception exception)
        {
            // Bez internetu nie ma Relay/Lobby - gra startuje lokalnie, ale pady (signaling przez Lobby) tez nie rusza.
            Debug.LogWarning($"Relay/Lobby niedostepne, start bez internetu: {exception.Message}");
            _matchmakingManager.StartLocalHost();
        }

        // Klienci dostana scene lobby automatycznie po polaczeniu (synchronizacja scen Netcode).
        if (NetworkManager.Singleton.IsServer)
        {
            PadServer.StartForHost();
            NetworkManager.Singleton.SceneManager.LoadScene(SceneNames.Lobby, LoadSceneMode.Single);
        }
    }

    // Nieudany JOIN - wypisujemy sie z lobby, zeby nie wisiec na liscie graczy.
    private async void HandleClientStopped(bool wasHost)
    {
        await _matchmakingManager.LeaveSessionAsync();
    }

    private async void OnJoinClicked()
    {
        var sessions = await _matchmakingManager.FetchAvailableSessionsAsync();

        if (sessions.Count > 0)
        {
            await _matchmakingManager.JoinSessionAsync(sessions[0].Id);
        }
    }

    private void OnExitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
#endregion
