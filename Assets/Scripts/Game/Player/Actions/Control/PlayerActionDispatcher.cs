#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Game;
using Kern.Networking;
using Kern.Player.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Actions;
using UnityEngine;

using Kern.Game.Entities.Robots;
using Kern.Game.Player.Movement;

namespace Kern.Game.Player.Actions;

internal sealed class PlayerActionDispatcher
{
    private readonly IPlayerInput _input;
    private readonly INetworkService? _networkService;
    private readonly IServerAudioService _audio;
    private readonly IServerVisualEffectService _visualEffect;
    private float _lastDigTime;
    private Vector2Int? _pendingDigTarget;

    // Последнее отправленное копание. Живёт дольше ожидания подтверждения:
    // на одно копание сервер присылает и звук, и эффект, и оба рисуют
    // визуал, которому нужно направление.
    private Vector2Int? _lastDigTarget;
    private Direction _lastDigDirection;

    public PlayerActionDispatcher(
        IPlayerInput input,
        INetworkService? networkService,
        IServerAudioService audio,
        IServerVisualEffectService visualEffect)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _networkService = networkService;
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _visualEffect = visualEffect ?? throw new ArgumentNullException(nameof(visualEffect));
    }

    /// <param name="robot">Робот игрока; до его появления делать нечего.</param>
    /// <param name="playable">Позиция с сервера получена и робот в кадре.</param>
    /// <param name="inputBlocked">Ввод перехвачен меню или полем ввода.</param>
    public void UpdateHealEffect(Robot? robot, bool playable, bool inputBlocked)
    {
        robot?.SetHealHeld(playable && !inputBlocked && _input.IsHealHeld);
    }

    public bool IsDigOnCooldown => PlayerMovementController.IsDigCooldownActive(
        Time.time,
        _lastDigTime,
        GameplayActionDefaults.DefaultDigCooldown);

    public bool IsDigAwaitingConfirmation => _pendingDigTarget.HasValue;

    public void ResetDigCooldown()
    {
        _lastDigTime = 0f;
        _pendingDigTarget = null;
    }

    public void NotifyDug(Vector2Int target, Direction direction)
    {
        _lastDigTime = Time.time;
        _pendingDigTarget = target;
        _lastDigTarget = target;
        _lastDigDirection = direction;
    }

    public bool TryGetDigDirection(ushort x, ushort y, out Direction direction)
    {
        direction = _lastDigDirection;
        return _lastDigTarget == new Vector2Int(x, y);
    }

    public void ConfirmDigAction(ushort x, ushort y)
    {
        if (_pendingDigTarget == new Vector2Int(x, y))
        {
            _pendingDigTarget = null;
        }
    }

    public void HandleBoundaryDig(Vector2Int position, Direction direction)
    {
        if (IsDigOnCooldown || IsDigAwaitingConfirmation)
        {
            return;
        }

        Vector2Int target = position + PlayerDirectionMapping.DirectionToDigOffset(direction);
        _lastDigTime = Time.time;
        _lastDigTarget = target;
        _lastDigDirection = direction;
        _audio.Play(AudioEventType.Bz, (ushort)position.x, (ushort)position.y);
        _visualEffect.PlayBoundaryDig(target.x, target.y, direction);
    }

    public void HandleDig(Vector2Int position, Direction direction, IMapDataProvider? mapDataProvider)
    {
        // Движение в приоритете: пока зажаты WASD/стрелки (или стик), копание
        // не отправляется - иначе кулдаун после BZ блокировал бы старт движения.
        if (_input.MoveInput != Vector2.zero)
        {
            return;
        }

        if (!_input.WantsToDig || IsDigOnCooldown || IsDigAwaitingConfirmation)
        {
            return;
        }

        Vector2Int digTarget = position + PlayerDirectionMapping.DirectionToDigOffset(direction);
        if (mapDataProvider == null)
        {
            return;
        }

        if (!PlayerMovementController.IsWithinWorldBounds(
                digTarget,
                mapDataProvider.WorldWidth,
                mapDataProvider.WorldHeight))
        {
            HandleBoundaryDig(position, direction);
            return;
        }

        NotifyDug(digTarget, direction);
        _networkService?.Send(
            new ActionClientPacket((ushort)digTarget.x, (ushort)digTarget.y, new BzPacket()));
    }

    public void DispatchHotkeys()
    {
        if (_input.WantsToToggleAutoDig)
        {
            _networkService?.SendAction(new ToggleAutoDigPacket());
        }

        if (_input.WantsToGeo)
        {
            _networkService?.SendAction(new GeoPacket());
        }

        if (_input.WantsToHeal)
        {
            _networkService?.SendAction(new HealPacket());
        }

        if (_input.WantsToBuildCyan)
        {
            _networkService?.SendAction(new BuildCyanPacket());
        }

        if (_input.WantsToBuildGray)
        {
            _networkService?.SendAction(new BuildGrayPacket());
        }

        if (_input.WantsToBuildGreen)
        {
            _networkService?.SendAction(new BuildGreenPacket());
        }

        if (_input.WantsToBuildWhite)
        {
            _networkService?.SendAction(new BuildWhitePacket());
        }
    }
}
