#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Shared.Packets;
using UnityEngine.UIElements;

namespace Kern.UI;

internal sealed class PauseMenuDebugSectionBuilder
{
    private readonly INetworkService _networkService;
    private readonly IConnectionService _connectionService;
    private readonly ILocalPlayerState _localPlayer;
    private readonly Action _closeMenu;
    private readonly ILocalizationService _loc;

    public PauseMenuDebugSectionBuilder(
        INetworkService networkService,
        IConnectionService connectionService,
        ILocalPlayerState localPlayer,
        Action closeMenu,
        ILocalizationService loc)
    {
        _networkService = networkService;
        _connectionService = connectionService;
        _localPlayer = localPlayer;
        _closeMenu = closeMenu;
        _loc = loc;
    }

    public Foldout Build()
    {
        var debugSection = new Foldout
        {
            text = _loc.Get("settings.debug.tools"),
            value = false,
        };
        debugSection.AddToClassList("settings-section");
        debugSection.AddToClassList("settings-section--debug");

        debugSection.Add(PauseMenuUIFactory.CreateLabel(_loc.Get("settings.debug.tools")));
        debugSection.Add(PauseMenuUIFactory.CreateButton(_loc.Get("settings.debug.test_kick"), () =>
        {
            _connectionService.TriggerDisconnect(_loc.Get("settings.debug.test_disconnect"));
            _closeMenu();
        }));
        debugSection.Add(PauseMenuUIFactory.CreateButton(_loc.Get("settings.debug.test_reconnect"), () =>
        {
            _connectionService.TriggerReconnect(_loc.Get("settings.debug.server_restart"));
            _closeMenu();
        }));
        debugSection.Add(PauseMenuUIFactory.CreateButton(_loc.Get("settings.debug.test_open_url"), () =>
        {
            SendElementClick("open_url_test");
        }));
        debugSection.Add(PauseMenuUIFactory.CreateButton(_loc.Get("settings.debug.test_modal"), () =>
        {
            SendElementClick("test_modal");
        }));
        debugSection.Add(PauseMenuUIFactory.CreateButton(_loc.Get("settings.debug.join_clan"), () =>
        {
            SendElementClick("join_clan");
        }));
        debugSection.Add(PauseMenuUIFactory.CreateButton(_loc.Get("settings.debug.leave_clan"), () =>
        {
            SendElementClick("leave_clan");
        }));
        debugSection.Add(PauseMenuUIFactory.CreateButton(_loc.Get("settings.debug.test_mission_arrow"), () =>
        {
            SendElementClick("test_mission_arrow");
        }));
        debugSection.Add(PauseMenuUIFactory.CreateButton(_loc.Get("settings.debug.missions"), () =>
        {
            SendElementClick("open_missions");
        }));
        debugSection.Add(PauseMenuUIFactory.CreateButton(_loc.Get("settings.debug.walls_off"), () =>
        {
            var player = _localPlayer.Current;
            if (player != null)
            {
                player.IgnoreCollision = !player.IgnoreCollision;
                _closeMenu();
            }
        }));

        return debugSection;
    }

    private void SendElementClick(string tag)
    {
        _networkService.Send(new ElementClickPacket(tag, 0, Array.Empty<StringPairPacket>()));
        _closeMenu();
    }
}
