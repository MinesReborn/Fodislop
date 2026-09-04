#nullable enable

using System;
using System.Collections.Generic;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Mission;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.Utilities;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyWindowBuilder
{
    public static ServerPacket BuildAuthWindow()
    {
        var titleText = new TextPacket
        {
            Text = "<color=#B2A680>Авторизация</color>",
            AttachedProperties = new StringPairPacket[]
            {
                new("DockPanel.Dock", "Top"),
            },
        };

        var descriptionText = new TextPacket
        {
            Text = "<color=white>Нажмите «Авторизоваться» чтобы начать игру</color>",
            Style = new GUIStylePacket
            {
                Margin = new Margins(0, 0, 20, 0),
            },
            AttachedProperties = new StringPairPacket[]
            {
                new("DockPanel.Dock", "Top"),
            },
        };

        var authButton = new TextPacket
        {
            Text = "<color=white>Авторизоваться</color>",
            OnClickContext = ".",
            Style = new GUIStylePacket
            {
                Background = System.Drawing.Color.FromArgb(242, 40, 167, 69),
                Border = System.Drawing.Color.FromArgb(255, 60, 200, 100),
                BorderWidth = 2,
                Padding = new Margins(10, 10, 6, 6),
                Margin = new Margins(0, 0, 0, 0),
            },
            AttachedProperties = new StringPairPacket[]
            {
                new("DockPanel.Dock", "Top"),
            },
        };

        var root = new DockPanelPacket
        {
            Style = new GUIStylePacket
            {
                Background = System.Drawing.Color.FromArgb(242, 20, 20, 20),
                Border = System.Drawing.Color.FromArgb(255, 89, 89, 89),
                BorderWidth = 2,
                Padding = new Margins(10, 10, 10, 10),
            },
            Children = new List<IGUIComponentPacket>
            {
                titleText,
                descriptionText,
                authButton,
            },
        };

        return new ServerPacket(new OpenWindowPacket("auth", 300, 160, root));
    }

    public static ServerPacket BuildTestModalWindow()
    {
        return new ServerPacket(new ModalWindowPacket(
            "Тестовое окно",
            "Это модальное окно вызывается из HUD.\n\nНажмите OK чтобы продолжить.",
            "OK",
            string.Empty));
    }

    public static ServerPacket BuildOpenUrlPacket(string url)
    {
        return new ServerPacket(new OpenURLPacket(url));
    }

    public static ServerPacket BuildTestMissionArrowPacket(ushort x, ushort y)
    {
        return new ServerPacket(new MissionArrowPacket(x, y));
    }
}
