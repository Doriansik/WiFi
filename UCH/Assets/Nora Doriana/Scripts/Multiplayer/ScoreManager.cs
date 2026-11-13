using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using System;

#region Score Manager
public class ScoreManager : NetworkBehaviour
{
    private const int PointsPerFinish = 1;

    public struct PlayerScore : INetworkSerializeByMemcpy, IEquatable<PlayerScore>
    {
        // NetworkObjectId gracza - pady z telefonow maja to samo OwnerClientId co host, wiec po nim nie rozroznimy graczy.
        public ulong PlayerObjectId;
        public int Score;

        public bool Equals(PlayerScore other)
        {
            return PlayerObjectId == other.PlayerObjectId && Score == other.Score;
        }
    }

    private NetworkList<PlayerScore> _playerScores = new NetworkList<PlayerScore>();

    public static ScoreManager Instance { get; private set; }

    public NetworkList<PlayerScore> PlayerScores => _playerScores;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }

    public override void OnNetworkSpawn()
    {
        gameObject.AddComponent<ScoreHud>();
    }

    public override void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        base.OnDestroy();
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        for (int i = 0; i < PlayerBuilder.SpawnedBuilders.Count; i++)
        {
            PlayerBuilder builder = PlayerBuilder.SpawnedBuilders[i];
            ulong clientId = builder.NetworkObjectId;

            if (!HasScoreForClient(clientId))
            {
                _playerScores.Add(new PlayerScore { PlayerObjectId = clientId, Score = 0 });
            }
        }
    }

    // Wolane przez PlayerBuilder.MarkFinished w chwili wejscia do mety. Sprawdzanie HasFinished co klatke gubilo punkt:
    // gdy wszyscy sa juz w mecie, PlacementManager w tej samej klatce zaczyna nowa runde i kasuje flage.
    public void AwardFinish(PlayerBuilder builder)
    {
        if (!IsServer)
        {
            return;
        }

        ulong clientId = builder.NetworkObjectId;

        if (!HasScoreForClient(clientId))
        {
            _playerScores.Add(new PlayerScore { PlayerObjectId = clientId, Score = 0 });
        }

        AddScore(clientId, PointsPerFinish);
    }

    private bool HasScoreForClient(ulong clientId)
    {
        for (int i = 0; i < _playerScores.Count; i++)
        {
            if (_playerScores[i].PlayerObjectId == clientId)
            {
                return true;
            }
        }
        return false;
    }

    private void AddScore(ulong clientId, int pointsToAdd)
    {
        for (int i = 0; i < _playerScores.Count; i++)
        {
            if (_playerScores[i].PlayerObjectId == clientId)
            {
                PlayerScore updatedScore = _playerScores[i];
                updatedScore.Score += pointsToAdd;

                _playerScores[i] = updatedScore;
                break;
            }
        }
    }
}
#endregion