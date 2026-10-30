using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

#region Skin Definition
[Serializable]
public class SkinDefinition
{
    public string Id;  // ten sam identyfikator uzywa strona pada (docs/pad/skins/<Id>.png)
    public string DisplayName;
    public Sprite[] Frames;

    public Sprite Preview => Frames != null && Frames.Length > 0 ? Frames[0] : null;
}
#endregion

#region Player Skin
// Postac gracza wybrana w lobby - z aplikacji albo z telefonu. Wybor zatwierdza serwer, wiec wszyscy widza to samo.
// Kazdy ma inna postac, dopoki starcza wolnych - przy wiekszej liczbie graczy niz postaci moga sie powtarzac.
public class PlayerSkin : NetworkBehaviour
{
    private const float WalkSpeedThreshold = 0.1f;
    // Pozycja zmienia sie co krok fizyki, nie co klatke - bez zapasu animacja migalaby na pierwsza klatke.
    private const float WalkStopDelaySeconds = 0.15f;

    private static readonly List<PlayerSkin> _spawnedSkins = new List<PlayerSkin>();

    [SerializeField] private SpriteRenderer _spriteRenderer;
    [SerializeField] private float _walkFramesPerSecond = 8f;
    [SerializeField] private SkinDefinition[] _skins;

    private NetworkVariable<int> _skinIndex = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private float _previousX;
    private float _lastMoveTime = float.MinValue;
    private float _walkTime;

    public static IReadOnlyList<PlayerSkin> SpawnedSkins => _spawnedSkins;
    public static event Action OnSkinsChanged;

    public IReadOnlyList<SkinDefinition> Skins => _skins;
    public int SkinIndex => _skinIndex.Value;
    public SkinDefinition CurrentSkin => IsValidIndex(SkinIndex) ? _skins[SkinIndex] : null;

    public override void OnNetworkSpawn()
    {
        _skinIndex.OnValueChanged += HandleSkinIndexChanged;

        if (IsServer && !IsValidIndex(_skinIndex.Value))
        {
            _skinIndex.Value = GetLeastUsedSkinIndex();
        }

        _spawnedSkins.Add(this);
        _previousX = transform.position.x;
        ShowFrame(0);
        OnSkinsChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        _skinIndex.OnValueChanged -= HandleSkinIndexChanged;
        _spawnedSkins.Remove(this);
        OnSkinsChanged?.Invoke();
    }

    public int FindSkinIndex(string skinId)
    {
        return _skins == null ? -1 : Array.FindIndex(_skins, skin => skin.Id == skinId);
    }

    // Postac noszona przez kogos innego jest zajeta - chyba ze zajete sa wszystkie, wtedy wolno sie powtarzac.
    public bool CanSelect(int index)
    {
        if (!IsValidIndex(index))
        {
            return false;
        }

        if (!IsTakenByOthers(index))
        {
            return true;
        }

        for (int i = 0; i < _skins.Length; i++)
        {
            if (!IsTakenByOthers(i))
            {
                return false;
            }
        }

        return true;
    }

    public bool IsTakenByOthers(int index)
    {
        foreach (PlayerSkin other in _spawnedSkins)
        {
            if (other != this && other.SkinIndex == index)
            {
                return true;
            }
        }

        return false;
    }

    // Host i pady (obiekty serwera) zmieniaja od razu, gracz z aplikacji prosi serwer.
    public void RequestSkin(int index)
    {
        if (IsServer)
        {
            TrySetSkin(index);
        }
        else if (IsOwner)
        {
            RequestSkinRpc(index);
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestSkinRpc(int index, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId == OwnerClientId)
        {
            TrySetSkin(index);
        }
    }

    private void TrySetSkin(int index)
    {
        if (CanSelect(index))
        {
            _skinIndex.Value = index;
        }
    }

    private void LateUpdate()
    {
        SkinDefinition skin = CurrentSkin;

        if (skin == null || skin.Frames == null || skin.Frames.Length == 0)
        {
            return;
        }

        // Ruch liczony z pozycji, nie z fizyki - dziala tak samo na hoscie i u klientow (NetworkTransform).
        float x = transform.position.x;

        if (Time.deltaTime > 0f && Mathf.Abs(x - _previousX) / Time.deltaTime > WalkSpeedThreshold)
        {
            _lastMoveTime = Time.time;
        }

        _previousX = x;

        if (Time.time - _lastMoveTime <= WalkStopDelaySeconds)
        {
            _walkTime += Time.deltaTime;
            ShowFrame((int)(_walkTime * _walkFramesPerSecond) % skin.Frames.Length);
        }
        else
        {
            _walkTime = 0f;
            ShowFrame(0);
        }
    }

    private void HandleSkinIndexChanged(int previousValue, int newValue)
    {
        ShowFrame(0);
        OnSkinsChanged?.Invoke();
    }

    private void ShowFrame(int frame)
    {
        SkinDefinition skin = CurrentSkin;

        if (skin != null && skin.Frames != null && frame < skin.Frames.Length && _spriteRenderer.sprite != skin.Frames[frame])
        {
            _spriteRenderer.sprite = skin.Frames[frame];
        }
    }

    // Nowy gracz dostaje postac, ktorej nikt jeszcze nie ma (przy remisie - pierwsza z listy, czyli parowke).
    private int GetLeastUsedSkinIndex()
    {
        int bestIndex = 0;
        int bestCount = int.MaxValue;

        for (int i = 0; i < _skins.Length; i++)
        {
            int count = 0;

            foreach (PlayerSkin other in _spawnedSkins)
            {
                if (other != this && other.SkinIndex == i)
                {
                    count++;
                }
            }

            if (count < bestCount)
            {
                bestIndex = i;
                bestCount = count;
            }
        }

        return bestIndex;
    }

    private bool IsValidIndex(int index)
    {
        return _skins != null && index >= 0 && index < _skins.Length;
    }
}
#endregion
