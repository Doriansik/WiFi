using Unity.Netcode;
using UnityEngine;
using System;

#region Interfaces
public interface IPlayerReady
{
    void SetReadyState(bool isReady);
    bool GetReadyState();
}
#endregion

#region Lobby Player
public class LobbyPlayer : NetworkBehaviour, IPlayerReady
{
    private NetworkVariable<bool> _isReady = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public event Action<bool> OnReadyStateChanged;

    public override void OnNetworkSpawn()
    {
        _isReady.OnValueChanged += HandleReadyStateChanged;
    }

    public override void OnNetworkDespawn()
    {
        _isReady.OnValueChanged -= HandleReadyStateChanged;
    }

    public void SetReadyState(bool isReady)
    {
        if (IsOwner)
        {
            _isReady.Value = isReady;
        }
    }

    public bool GetReadyState()
    {
        return _isReady.Value;
    }

    private void HandleReadyStateChanged(bool previousValue, bool newValue)
    {
        OnReadyStateChanged?.Invoke(newValue);
    }
}
#endregion