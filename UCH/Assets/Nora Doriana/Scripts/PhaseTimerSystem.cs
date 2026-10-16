using Unity.Netcode;
using UnityEngine;

#region Phase Timer System
public class PhaseTimerSystem : NetworkBehaviour
{
    private const float PlacementPhaseDuration = 15f;
    private const float PlatformingPhaseDuration = 60f;
    private const float ResultsPhaseDuration = 10f;

    private NetworkVariable<float> _currentPhaseTimer = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private void Update()
    {
        if (!IsServer)
        {
            return;
        }

        GameState currentState = GameManager.Instance.GetCurrentState();

        if (currentState == GameState.Lobby)
        {
            return;
        }

        ProcessTimer(currentState);
    }

    private void ProcessTimer(GameState currentState)
    {
        _currentPhaseTimer.Value -= Time.deltaTime;

        if (_currentPhaseTimer.Value <= 0f)
        {
            TransitionToNextPhase(currentState);
        }
    }

    private void TransitionToNextPhase(GameState currentState)
    {
        switch (currentState)
        {
            case GameState.Placement:
                _currentPhaseTimer.Value = PlatformingPhaseDuration;
                GameManager.Instance.ChangeState(GameState.Platforming);
                break;
            case GameState.Platforming:
                _currentPhaseTimer.Value = ResultsPhaseDuration;
                GameManager.Instance.ChangeState(GameState.Results);
                break;
            case GameState.Results:
                _currentPhaseTimer.Value = PlacementPhaseDuration;
                GameManager.Instance.ChangeState(GameState.Placement);
                break;
        }
    }

    public float GetRemainingTime()
    {
        return Mathf.Max(0f, _currentPhaseTimer.Value);
    }
}
#endregion