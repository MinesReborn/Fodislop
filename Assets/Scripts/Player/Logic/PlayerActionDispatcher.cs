#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game;
using Fodinae.Networking;
using Fodinae.Player.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Actions;
using UnityEngine;

namespace Fodinae.Player.Logic;

/// <summary>
/// Dispatches gameplay action packets (heal, geo, build packets, toggles) triggered by player input.
/// </summary>
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

    /// <summary>
    /// Зажатая клавиша лечения зажигает вокруг игрока кольцо стрелок.
    /// </summary>
    /// <param name="robot">Робот игрока; до его появления делать нечего.</param>
    /// <param name="playable">Позиция с сервера получена и робот в кадре.</param>
    /// <param name="inputBlocked">Ввод перехвачен меню или полем ввода.</param>
    /// <remarks>
    /// Метод обязан вызываться каждый кадр, включая кадры, где играть
    /// нельзя: аура гаснет ровно тем же вызовом, каким зажигается, и
    /// пропущенный кадр оставил бы её гореть на застывшем роботе.
    /// </remarks>
    public void UpdateAura(Robot? robot, bool playable, bool inputBlocked)
    {
        robot?.SetAuraVisible(playable && !inputBlocked && _input.IsHealHeld);
    }

    /// <summary>
    /// Откат копки ещё не истёк.
    /// </summary>
    /// <remarks>
    /// Спрашивает не только копка: тем же откатом ограничен шаг, иначе
    /// автокопка отправляла бы BzPacket на каждом тике движения, опираясь
    /// на задержку клетки вместо серверного отката.
    /// </remarks>
    public bool IsDigOnCooldown => PlayerMovementController.IsDigCooldownActive(
        Time.time,
        _lastDigTime,
        ProjectRuntimeContracts.Gameplay.DefaultDigCooldown);

    /// <summary>Сбрасывает откат копки при входе в мир.</summary>
    public void ResetDigCooldown() => _lastDigTime = 0f;

    /// <summary>
    /// Отмечает копку, выполненную помимо этого класса.
    /// </summary>
    /// <remarks>
    /// Автокопка живёт в шаге движения: она срабатывает, когда путь занят,
    /// и это часть решения «куда шагнуть». Откат у копки при этом один на
    /// оба пути, поэтому владелец отката должен узнать и про неё — иначе
    /// автокопка и ручная копка считали бы время независимо и вместе
    /// стучали бы вдвое чаще разрешённого.
    /// </remarks>
    public void NotifyDug() => _lastDigTime = Time.time;

    /// <summary>
    /// Ручная копка клетки перед роботом.
    /// </summary>
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
