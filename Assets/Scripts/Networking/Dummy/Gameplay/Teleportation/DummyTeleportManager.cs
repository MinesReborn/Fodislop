#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Kern;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using Kern.Networking.Protocol;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.World;
using MinesServer.Networking.Contracts.Packets;
using UnityEngine;


namespace Kern.Networking.Dummy;

internal sealed class DummyTeleportManager
{
    private readonly Action<ServerPacket> _onReceived;
    private readonly List<(ushort X, ushort Y)> _teleportPositions;
    private readonly IAsyncOperationSupervisor _operations;
    private readonly Func<ushort, ushort, UniTask>? _onTeleport;
    internal DummyTeleportManager(
    Action<ServerPacket> onReceived,
    List<(ushort X, ushort Y)> teleportPositions,
    IAsyncOperationSupervisor operations,
    Func<ushort, ushort, UniTask>? onTeleport = null)
    {
        _onReceived = onReceived;
        _teleportPositions = teleportPositions;
        _operations = operations;
        _onTeleport = onTeleport;
    }

    private List<(ushort X, ushort Y)>? _teleportDestinations;
    private int _closeElement = -1;
    private int[] _destinationElements = Array.Empty<int>();

    public bool WindowOpen { get; set; }

    public void CheckTeleportEntry(ushort x, ushort y)
    {
        if (!_teleportPositions.Contains((x, y)))
        {
            return;
        }

        SendTeleportWindow(x, y);
    }

    public void SendTeleportWindow(ushort x, ushort y)
    {
        _teleportDestinations = _teleportPositions
            .Where(tp => tp.X != x || tp.Y != y)
            .ToList();

        if (_teleportDestinations.Count == 0)
        {
            SendTeleportWindowNoDestinations();
            return;
        }

        var rows = new IGUIComponentPacket[_teleportDestinations.Count];
        for (int i = 0; i < _teleportDestinations.Count; i++)
        {
            var (destX, destY) = _teleportDestinations[i];
            rows[i] = new TextPacket
            {
                Text = $"<color=white>Телепорт на ({destX,5}, {destY,5})</color>   <color=#B2A680>[ТП]</color>",
                OnClickContext = ".",
                Style = new GUIStylePacket
                {
                    Background = System.Drawing.Color.FromArgb(242, 26, 26, 26),
                    Border = System.Drawing.Color.FromArgb(255, 89, 89, 89),
                    BorderWidth = 2,
                    Padding = new Margins(8, 12, 8, 12),
                    Margin = new Margins(0, 0, 4, 0),
                },
            };
        }

        var close = CloseButton();
        var scrollViewer = new ScrollViewerPacket
        {
            VerticalScrollBar = ScrollbarVisibility.Auto,
            HorizontalScrollBar = ScrollbarVisibility.Auto,
            Children = rows,
        };

        var root = new DockPanelPacket
        {
            Style = new GUIStylePacket
            {
                Background = System.Drawing.Color.FromArgb(242, 20, 20, 20),
                Border = System.Drawing.Color.FromArgb(255, 89, 89, 89),
                BorderWidth = 2,
                Padding = new Margins(2, 8, 2, 8),
            },
            Children = new IGUIComponentPacket[]
            {
                new DockPanelPacket
                {
                    AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Top") },
                    Style = new GUIStylePacket
                    {
                        Margin = new Margins(0, 0, 10, 0),
                        Padding = new Margins(0, 0, 0, 0),
                    },
                    Children = new IGUIComponentPacket[]
                    {
                        new TextPacket
                        {
                            Text = "<color=#B2A680>Телепорты</color>",
                            AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Left") },
                        },
                        close,
                    },
                },
                scrollViewer,
            },
        };

        _closeElement = DummyWindowElements.IndexOf(root, close);
        _destinationElements = rows.Select(row => DummyWindowElements.IndexOf(root, row)).ToArray();
        _onReceived.Invoke(new ServerPacket(new OpenWindowPacket("teleport", 400, 300, root)));
        WindowOpen = true;
    }

    public void SendTeleportWindowNoDestinations()
    {
        var text = new TextPacket
        {
            Text = "<color=gray>Нет доступных телепортов</color>",
        };
        var close = CloseButton();

        var root = new DockPanelPacket
        {
            Style = new GUIStylePacket
            {
                Background = System.Drawing.Color.FromArgb(242, 20, 20, 20),
                Border = System.Drawing.Color.FromArgb(255, 89, 89, 89),
                BorderWidth = 2,
                Padding = new Margins(0, 0, 0, 0),
            },
            Children = new IGUIComponentPacket[]
            {
                new DockPanelPacket
                {
                    AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Top") },
                    Style = new GUIStylePacket
                    {
                        Margin = new Margins(0, 0, 0, 0),
                        Padding = new Margins(0, 0, 0, 0),
                    },
                    Children = new IGUIComponentPacket[]
                    {
                        new TextPacket
                        {
                            Text = "<color=#B2A680>Телепорты</color>",
                            AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Left") },
                        },
                        close,
                    },
                },
                text,
            },
        };

        _closeElement = DummyWindowElements.IndexOf(root, close);
        _destinationElements = Array.Empty<int>();
        _onReceived.Invoke(new ServerPacket(new OpenWindowPacket("teleport", 400, 200, root)));
        WindowOpen = true;
    }

    public bool IsCloseElement(int elementIndex) => elementIndex == _closeElement;

    public int DestinationOfElement(int elementIndex) => Array.IndexOf(_destinationElements, elementIndex);

    // "." collects inputs from the button itself: it has none.
    private static TextPacket CloseButton() => new()
    {
        Text = "Закрыть",
        OnClickContext = ".",
        AttachedProperties = new[]
        {
            new StringPairPacket("DockPanel.Dock", "Right"),
            new StringPairPacket(ServerWindowActionMetadata.ActionKey, ServerWindowActionMetadata.CloseActionValue),
        },
    };

    public void HandleTeleportClick(int index)
    {
        if (index < 0 || _teleportDestinations == null || index >= _teleportDestinations.Count)
        {
            return;
        }

        var (destX, destY) = _teleportDestinations[index];
        WindowOpen = false;
        // Через супервизора, а не .Forget(): брошенная задача переживает
        // разрыв соединения, а её исключение не достаётся никому. Супервизор
        // держит её в учёте и гасит вместе с жизненным циклом.
        _operations.Run(
            "DummyTeleportManager.SendTeleport",
            _ => SendTeleportAsync(destX, destY));
    }

    private async UniTask SendTeleportAsync(ushort destX, ushort destY)
    {
        if (_onTeleport != null)
        {
            await _onTeleport(destX, destY);
        }

        _onReceived.Invoke(new ServerPacket(new TeleportPacket(destX, destY)));
        _onReceived.Invoke(new ServerPacket(new HBPacket(new IHBPacket[]
        {
            new VisualPacket(VisualEffect.TpIn, 0, destX, destY, Array.Empty<StringPairPacket>()),
        })));
        _onReceived.Invoke(new ServerPacket(new CloseWindowPacket()));
    }
}
