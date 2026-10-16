using Unity.Netcode;
using UnityEngine;
using System;

#region Enums
public enum GameState
{
    Lobby,
    Placement,
    Platforming,
    Results
}
#endregion

#region Game Manager
public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    private NetworkVariable<GameState> _currentGameState = new NetworkVariable<GameState>(
        GameState.Lobby,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public event Action<GameState> OnGameStateChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        _currentGameState.OnValueChanged += HandleGameStateChanged;
    }

    public override void OnNetworkDespawn()
    {
        _currentGameState.OnValueChanged -= HandleGameStateChanged;
    }

    public void ChangeState(GameState newState)
    {
        if (IsServer)
        {
            _currentGameState.Value = newState;
        }
    }

    public GameState GetCurrentState()
    {
        return _currentGameState.Value;
    }

    private void HandleGameStateChanged(GameState previousState, GameState newState)
    {
        OnGameStateChanged?.Invoke(newState);
    }
}
#endregion