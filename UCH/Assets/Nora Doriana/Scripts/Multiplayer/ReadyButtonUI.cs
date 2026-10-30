using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

#region Ready Button UI
public class ReadyButtonUI : MonoBehaviour
{
    [SerializeField] private Button _readyButton;

    private void Awake()
    {
        _readyButton.onClick.AddListener(OnReadyClicked);
    }

    private void OnDestroy()
    {
        _readyButton.onClick.RemoveListener(OnReadyClicked);
    }

    private void OnReadyClicked()
    {
        if (NetworkManager.Singleton.LocalClient != null &&
            NetworkManager.Singleton.LocalClient.PlayerObject != null &&
            NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent<IPlayerReady>(out var playerReady))
        {
            playerReady.SetReadyState(true);
            _readyButton.interactable = false;
        }
    }
}
#endregion