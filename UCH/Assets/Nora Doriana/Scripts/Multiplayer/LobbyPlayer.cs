using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using System;
using System.Collections.Generic;

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
    private const int MaxDeviceNameLength = 20;

    private static readonly List<LobbyPlayer> _spawnedPlayers = new List<LobbyPlayer>();

    public static IReadOnlyList<LobbyPlayer> SpawnedPlayers => _spawnedPlayers;
    public static event Action OnPlayerListChanged;

    private NetworkVariable<bool> _isReady = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<FixedString64Bytes> _deviceName = new NetworkVariable<FixedString64Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public event Action<bool> OnReadyStateChanged;

    public override void OnNetworkSpawn()
    {
        _isReady.OnValueChanged += HandleReadyStateChanged;
        _deviceName.OnValueChanged += HandleDeviceNameChanged;

        if (IsOwner)
        {
            _deviceName.Value = GetLocalDeviceName();
        }

        _spawnedPlayers.Add(this);
        OnPlayerListChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        _isReady.OnValueChanged -= HandleReadyStateChanged;
        _deviceName.OnValueChanged -= HandleDeviceNameChanged;

        _spawnedPlayers.Remove(this);
        OnPlayerListChanged?.Invoke();
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

    public string GetDeviceName()
    {
        return _deviceName.Value.ToString();
    }

    private void HandleReadyStateChanged(bool previousValue, bool newValue)
    {
        OnReadyStateChanged?.Invoke(newValue);
        OnPlayerListChanged?.Invoke();
    }

    private void HandleDeviceNameChanged(FixedString64Bytes previousValue, FixedString64Bytes newValue)
    {
        OnPlayerListChanged?.Invoke();
    }

    private static FixedString64Bytes GetLocalDeviceName()
    {
        string deviceName = SystemInfo.deviceName;

        if (deviceName.Length > MaxDeviceNameLength)
        {
            deviceName = deviceName.Substring(0, MaxDeviceNameLength);
        }

        return new FixedString64Bytes(deviceName);
    }
}
#endregion
