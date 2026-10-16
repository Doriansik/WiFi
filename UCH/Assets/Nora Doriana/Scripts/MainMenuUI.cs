using UnityEngine;
using UnityEngine.UI;

#region Main Menu UI
public class MainMenuUI : MonoBehaviour
{
    [SerializeField] private Button _hostButton;
    [SerializeField] private Button _joinButton;
    [SerializeField] private MatchmakingManager _matchmakingManager;
    [SerializeField] private string _defaultSessionName = "Imprezowa Gra";

    private void Awake()
    {
        _hostButton.onClick.AddListener(OnHostClicked);
        _joinButton.onClick.AddListener(OnJoinClicked);
    }

    private void OnDestroy()
    {
        _hostButton.onClick.RemoveListener(OnHostClicked);
        _joinButton.onClick.RemoveListener(OnJoinClicked);
    }

    private async void OnHostClicked()
    {
        await _matchmakingManager.CreateSessionAsync(_defaultSessionName);
    }

    private async void OnJoinClicked()
    {
        var sessions = await _matchmakingManager.FetchAvailableSessionsAsync();

        if (sessions.Count > 0)
        {
            await _matchmakingManager.JoinSessionAsync(sessions[0].Id);
        }
    }
}
#endregion