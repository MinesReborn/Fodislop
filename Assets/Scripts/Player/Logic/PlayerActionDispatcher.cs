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

namespace Kern.Player.Logic;

internal sealed class PlayerActionDispatcher
{
    private readonly IPlayerInput _input;
    private readonly INetworkService? _networkService;
    private readonly Action _toggleAggression;
    private float _lastDigTime;

    public PlayerActionDispatcher(
        IPlayerInput input,
        INetworkService? networkService,
        Action toggleAggression)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _networkService = networkService;
        _toggleAggression = toggleAggression ?? throw new ArgumentNullException(nameof(toggleAggression));
    }

    /// <param name="robot">Робот игрока; до его появления делать нечего.</param>
    /// <param name="playable">Позиция с сервера получена и робот в кадре.</param>
    /// <param name="inputBlocked">Ввод перехвачен меню или полем ввода.</param>
    public void UpdateAura(Robot? robot, bool playable, bool inputBlocked)
    {
        robot?.SetAuraWanted(playable && !inputBlocked && _input.IsHealHeld);
    }

    public bool IsDigOnCooldown => PlayerMovementController.IsDigCooldownActive(
        Time.time,
        _lastDigTime,
        ProjectRuntimeContracts.Gameplay.DefaultDigCooldown);

    public void ResetDigCooldown() => _lastDigTime = 0f;

    public void NotifyDug() => _lastDigTime = Time.time;

    public void HandleDig(Vector2Int position, Direction direction, IMapDataProvider? mapDataProvider)
    {
        if (!_input.WantsToDig || IsDigOnCooldown)
        {
            return;
        }

        Vector2Int digTarget = position + PlayerMovementMath.DirectionToDigOffset(direction);
        if (mapDataProvider == null ||
            !PlayerMovementController.IsWithinWorldBounds(
                digTarget,
                mapDataProvider.WorldWidth,
                mapDataProvider.WorldHeight))
        {
            return;
        }

        _networkService?.Send(
            new ActionClientPacket((ushort)digTarget.x, (ushort)digTarget.y, new BzPacket()));
        _lastDigTime = Time.time;
    }

    public void DispatchHotkeys()
    {
        if (_input.WantsToToggleAutoDig)
        {
            _networkService?.SendAction(new ToggleAutoDigPacket());
        }

        if (_input.WantsToToggleAggression)
        {
            _toggleAggression();
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
