using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

#region Ready Check System
public class ReadyCheckSystem : NetworkBehaviour
{
    private const float CheckIntervalSeconds = 1f;

    private float _timer;
    private bool _isGameStarted;

    private void Update()
    {
        if (!IsServer || _isGameStarted || GameManager.Instance.GetCurrentState() != GameState.Lobby)
        {
            return;
        }

        _timer += Time.deltaTime;
        if (_timer >= CheckIntervalSeconds)
        {
            _timer -= CheckIntervalSeconds;
            PerformReadyCheck();
        }
    }

    private void PerformReadyCheck()
    {
        IReadOnlyDictionary<ulong, NetworkClient> clients = NetworkManager.Singleton.ConnectedClients;

        if (clients.Count == 0)
        {
            return;
        }

        bool allReady = true;

        foreach (var client in clients.Values)
        {
            if (client.PlayerObject != null && client.PlayerObject.TryGetComponent<IPlayerReady>(out var playerReady))
            {
                if (!playerReady.GetReadyState())
                {
                    allReady = false;
                    break;
                }
            }
            else
            {
                allReady = false;
                break;
            }
        }

        if (allReady)
        {
            StartGame();
        }
    }

    private void StartGame()
    {
        _isGameStarted = true;
        GameManager.Instance.ChangeState(GameState.Placement);
    }
}
#endregion