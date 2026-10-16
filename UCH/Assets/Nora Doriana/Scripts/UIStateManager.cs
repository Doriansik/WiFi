using UnityEngine;

#region UI State Manager
public class UIStateManager : MonoBehaviour
{
    [SerializeField] private GameObject _lobbyPanel;
    [SerializeField] private GameObject _placementPanel;
    [SerializeField] private GameObject _gameplayPanel;

    private void Start()
    {
        GameManager.Instance.OnGameStateChanged += HandleGameStateChanged;
        UpdateUI(GameManager.Instance.GetCurrentState());
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGameStateChanged -= HandleGameStateChanged;
        }
    }

    private void HandleGameStateChanged(GameState newState)
    {
        UpdateUI(newState);
    }

    private void UpdateUI(GameState currentState)
    {
        _lobbyPanel.SetActive(currentState == GameState.Lobby);
        _placementPanel.SetActive(currentState == GameState.Placement);
        _gameplayPanel.SetActive(currentState == GameState.Platforming);
    }
}
#endregion