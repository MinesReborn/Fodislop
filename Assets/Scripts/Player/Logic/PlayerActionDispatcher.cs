#nullable enable

using System;
using Fodinae.Core.Interfaces;
using Fodinae.Networking;
using Fodinae.Player.Interfaces;
using MinesServer.Networking.Client.Packets.Actions;

namespace Fodinae.Player.Logic;

/// <summary>
/// Dispatches gameplay action packets (heal, geo, build packets, toggles) triggered by player input.
/// </summary>
internal sealed class PlayerActionDispatcher
{
    private readonly IPlayerInput _input;
    private readonly INetworkService? _networkService;
    private readonly Action _toggleAggression;

    public PlayerActionDispatcher(
        IPlayerInput input,
        INetworkService? networkService,
        Action toggleAggression)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _networkService = networkService;
        _toggleAggression = toggleAggression ?? throw new ArgumentNullException(nameof(toggleAggression));
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
