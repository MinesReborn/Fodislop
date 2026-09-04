#nullable enable

using System;
using Fodinae.Core.Interfaces;
using UnityEngine;

namespace Fodinae.UI;

/// <summary>
/// Manages player subscription, position tracking, and blink timing for the world map.
/// </summary>
public sealed class MapPlayerTracker
{
    private readonly ILocalPlayerState _localPlayer;
    private ILocalPlayer? _player;
    private Vector2Int _lastPlayerPos = new(int.MinValue, int.MinValue);
    private bool _playerSpawnSubscription;
    private bool _playerMoveSubscription;
    private float _playerBlinkTimer;
    private bool _playerBlinkState = true;

    public ILocalPlayer? CurrentPlayer => _player;

    public bool PlayerBlinkState => _playerBlinkState;

    public event Action? OnPlayerSpawned;

    public event Action<Vector2Int>? OnPlayerMoved;

    public event Action? OnBlinkFlipped;

    public MapPlayerTracker(ILocalPlayerState localPlayer)
    {
        _localPlayer = localPlayer;
    }

    public void EnsureBinding()
    {
        if (_playerSpawnSubscription)
        {
            _localPlayer.Changed -= OnLocalPlayerChanged;
            _playerSpawnSubscription = false;
        }

        ILocalPlayer? player = _localPlayer.Current;
        if (player != null)
        {
            SubscribeToPlayer(player);
            return;
        }

        _localPlayer.Changed += OnLocalPlayerChanged;
        _playerSpawnSubscription = true;
    }

    public void Dispose()
    {
        _localPlayer.Changed -= OnLocalPlayerChanged;
        _playerSpawnSubscription = false;
        if (_player != null)
        {
            UnsubscribeFromPlayer(_player);
        }
    }

    public void ResetState()
    {
        _playerBlinkState = true;
        _playerBlinkTimer = 0f;
        _lastPlayerPos = new Vector2Int(int.MinValue, int.MinValue);
    }

    public void Update(
        float deltaTime,
        bool followPlayer,
        ref float viewCenterX,
        ref float viewCenterY,
        ref bool renderRequested)
    {
        if (_player != null)
        {
            Vector2Int pos = _player.Position;
            if (pos.x != _lastPlayerPos.x || pos.y != _lastPlayerPos.y)
            {
                _lastPlayerPos = pos;
                if (followPlayer)
                {
                    viewCenterX = pos.x;
                    viewCenterY = pos.y;
                    renderRequested = true;
                }
            }
        }

        _playerBlinkTimer += deltaTime;
        if (_playerBlinkTimer >= 0.5f)
        {
            _playerBlinkTimer = 0f;
            _playerBlinkState = !_playerBlinkState;
            renderRequested = true;
            OnBlinkFlipped?.Invoke();
        }
    }

    private void SubscribeToPlayer(ILocalPlayer player)
    {
        if (_playerMoveSubscription && ReferenceEquals(_player, player))
        {
            return;
        }

        if (_playerMoveSubscription && _player != null)
        {
            _player.OnPlayerMoved -= HandlePlayerPositionChanged;
        }

        _player = player;
        _player.OnPlayerMoved += HandlePlayerPositionChanged;
        _playerMoveSubscription = true;
    }

    private void UnsubscribeFromPlayer(ILocalPlayer player)
    {
        if (!_playerMoveSubscription)
        {
            return;
        }

        player.OnPlayerMoved -= HandlePlayerPositionChanged;
        _playerMoveSubscription = false;
    }

    private void OnLocalPlayerChanged(ILocalPlayer? player)
    {
        _localPlayer.Changed -= OnLocalPlayerChanged;
        _playerSpawnSubscription = false;
        if (player == null)
        {
            return;
        }

        SubscribeToPlayer(player);
        _lastPlayerPos = new Vector2Int(int.MinValue, int.MinValue);
        OnPlayerSpawned?.Invoke();
    }

    private void HandlePlayerPositionChanged(Vector2Int oldPosition, Vector2Int newPosition)
    {
        _lastPlayerPos = newPosition;
        OnPlayerMoved?.Invoke(newPosition);
    }
}
