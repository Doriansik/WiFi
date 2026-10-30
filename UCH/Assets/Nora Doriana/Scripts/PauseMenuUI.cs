using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

#region Pause Menu UI
public class PauseMenuUI : MonoBehaviour
{
    private const string PauseMessageName = "GamePauseState";

    [SerializeField] private GameObject _pausePanel;
    [SerializeField] private Button _resumeButton;
    [SerializeField] private Button _mainMenuButton;
    [SerializeField] private Button _exitButton;

    private bool _isPaused;
    private bool _isLeaving;
    private bool _isOnline;

    private void Awake()
    {
        _resumeButton.onClick.AddListener(OnResumeClicked);
        _mainMenuButton.onClick.AddListener(OnMainMenuClicked);
        _exitButton.onClick.AddListener(OnExitClicked);
    }

    private void Start()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        _isOnline = networkManager != null && networkManager.IsListening;

        // Scena mapy odpalona bezposrednio w edytorze - pauza dziala lokalnie, bez sieci.
        if (!_isOnline)
        {
            ApplyPause(false);
            return;
        }

        networkManager.OnClientStopped += HandleClientStopped;
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(PauseMessageName, HandlePauseMessage);

        _resumeButton.gameObject.SetActive(networkManager.IsServer);
        ApplyPause(false);
    }

    private void OnDestroy()
    {
        _resumeButton.onClick.RemoveListener(OnResumeClicked);
        _mainMenuButton.onClick.RemoveListener(OnMainMenuClicked);
        _exitButton.onClick.RemoveListener(OnExitClicked);

        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager != null)
        {
            networkManager.OnClientStopped -= HandleClientStopped;
            networkManager.CustomMessagingManager?.UnregisterNamedMessageHandler(PauseMessageName);
        }

        Time.timeScale = 1f;
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            SetPausedForEveryone(!_isPaused);
        }
    }

    // Tylko host (PC) moze pauzowac i wznawiac gre - klienci dostaja stan przez wiadomosc sieciowa.
    private void SetPausedForEveryone(bool isPaused)
    {
        if (!_isOnline)
        {
            ApplyPause(isPaused);
            return;
        }

        NetworkManager networkManager = NetworkManager.Singleton;

        if (!networkManager.IsServer)
        {
            return;
        }

        ApplyPause(isPaused);

        using FastBufferWriter writer = new FastBufferWriter(sizeof(bool), Allocator.Temp);
        writer.WriteValueSafe(isPaused);
        networkManager.CustomMessagingManager.SendNamedMessageToAll(PauseMessageName, writer, NetworkDelivery.Reliable);
    }

    private void HandlePauseMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (NetworkManager.Singleton.IsServer)
        {
            return;
        }

        reader.ReadValueSafe(out bool isPaused);
        ApplyPause(isPaused);
    }

    private void ApplyPause(bool isPaused)
    {
        _isPaused = isPaused;
        _pausePanel.SetActive(isPaused);
        Time.timeScale = isPaused ? 0f : 1f;
    }

    private void HandleClientStopped(bool wasHost)
    {
        ReturnToMenu();
    }

    private void OnResumeClicked()
    {
        SetPausedForEveryone(false);
    }

    private void OnMainMenuClicked()
    {
        ReturnToMenu();
    }

    private void OnExitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void ReturnToMenu()
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
}
#endregion
