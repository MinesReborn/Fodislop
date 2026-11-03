#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces;
using UnityEngine;

namespace Kern.UI.Map;

internal sealed class MinimapPlayerBinding : IDisposable
{
    private readonly ILocalPlayerState _localPlayerState;
    private readonly Action<ILocalPlayer?> _playerChanged;
    private readonly Action<Vector2Int, Vector2Int> _playerMoved;
    private readonly Action<IReadOnlyList<Vector2Int>?> _pathChanged;
    private bool _localPlayerSubscribed;
    private bool _playerMoveSubscribed;

    public MinimapPlayerBinding(
        ILocalPlayerState localPlayerState,
        Action<ILocalPlayer?> playerChanged,
        Action<Vector2Int, Vector2Int> playerMoved,
        Action<IReadOnlyList<Vector2Int>?> pathChanged)
    {
        _localPlayerState = localPlayerState;
        _playerChanged = playerChanged;
        _playerMoved = playerMoved;
        _pathChanged = pathChanged;
    }

    public ILocalPlayer? Player { get; private set; }

    public IClickPathWalker? PathWalker { get; private set; }

    public IReadOnlyList<Vector2Int>? ClickPath { get; private set; }

    public void Subscribe()
    {
        if (_localPlayerSubscribed)
        {
            return;
        }

        _localPlayerState.Changed += _playerChanged;
        _localPlayerSubscribed = true;
    }

    public void Unsubscribe()
    {
        if (_localPlayerSubscribed)
        {
            _localPlayerState.Changed -= _playerChanged;
            _localPlayerSubscribed = false;
        }

        UnsubscribeFromPlayer();
    }

    public void BindPlayer(ILocalPlayer? player)
    {
        if (ReferenceEquals(Player, player) && _playerMoveSubscribed)
        {
            return;
        }

        UnsubscribeFromPlayer();
        UnbindPathWalker();
        Player = player;
        if (player == null)
        {
            return;
        }

        player.OnPlayerMoved += _playerMoved;
        _playerMoveSubscribed = true;
        if (player is IClickPathWalker pathWalker)
        {
            PathWalker = pathWalker;
            pathWalker.OnPathChanged += HandlePathChanged;
            ClickPath = pathWalker.Path;
        }
    }

    public void Dispose() => Unsubscribe();

    private void UnsubscribeFromPlayer()
    {
        if (_playerMoveSubscribed && Player != null)
        {
            Player.OnPlayerMoved -= _playerMoved;
            _playerMoveSubscribed = false;
        }
    }

    private void UnbindPathWalker()
    {
        if (PathWalker != null)
        {
            PathWalker.OnPathChanged -= HandlePathChanged;
            PathWalker = null;
        }

        ClickPath = null;
    }

    private void HandlePathChanged(IReadOnlyList<Vector2Int>? path)
    {
        ClickPath = path;
        _pathChanged(path);
    }
}
