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
        public ulong ClientId;
        public int Score;

        public bool Equals(PlayerScore other)
        {
            return ClientId == other.ClientId && Score == other.Score;
        }
    }

    private NetworkList<PlayerScore> _playerScores = new NetworkList<PlayerScore>();

    private readonly Dictionary<ulong, bool> _wasFinished = new Dictionary<ulong, bool>();

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
            ulong clientId = builder.OwnerClientId;

            if (!HasScoreForClient(clientId))
            {
                _playerScores.Add(new PlayerScore { ClientId = clientId, Score = 0 });
            }

            bool isCurrentlyFinished = builder.HasFinished;
            bool wasFinishedBefore = _wasFinished.ContainsKey(clientId) && _wasFinished[clientId];

            if (isCurrentlyFinished && !wasFinishedBefore)
            {
                AddScore(clientId, PointsPerFinish);
            }

            _wasFinished[clientId] = isCurrentlyFinished;
        }
    }

    private bool HasScoreForClient(ulong clientId)
    {
        for (int i = 0; i < _playerScores.Count; i++)
        {
            if (_playerScores[i].ClientId == clientId)
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
            if (_playerScores[i].ClientId == clientId)
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