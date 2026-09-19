#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Kern;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyTeleportManager(
    Action<ServerPacket> onReceived,
    List<(ushort X, ushort Y)> teleportPositions,
    IAsyncOperationSupervisor operations,
    Func<ushort, ushort, UniTask>? onTeleport = null)
{
    private List<(ushort X, ushort Y)>? _teleportDestinations;

    public bool WindowOpen { get; set; }

    public void CheckTeleportEntry(ushort x, ushort y)
    {
        if (!teleportPositions.Contains((x, y)))
        {
            return;
        }

        SendTeleportWindow(x, y);
    }

    public void SendTeleportWindow(ushort x, ushort y)
    {
        _teleportDestinations = teleportPositions
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
            Children =
            [
                new DockPanelPacket
                {
                    AttachedProperties = [new("DockPanel.Dock", "Top")],
                    Style = new GUIStylePacket
                    {
                        Margin = new Margins(0, 0, 10, 0),
                        Padding = new Margins(0, 0, 0, 0),
                    },
                    Children =
                    [
                        new TextPacket
                        {
                            Text = "<color=#B2A680>Телепорты</color>",
                            AttachedProperties = [new("DockPanel.Dock", "Left")],
                        },
                        new TextPacket
                        {
                            Text = "<color=#B3B3B3>×</color>",
                            OnClickContext = "teleport_close",
                            AttachedProperties = [new("DockPanel.Dock", "Right")],
                        },
                    ],
                },
                scrollViewer,
            ],
        };

        onReceived.Invoke(new ServerPacket(new OpenWindowPacket("teleport", 400, 300, root)));
        WindowOpen = true;
    }

    public void SendTeleportWindowNoDestinations()
    {
        var text = new TextPacket
        {
            Text = "<color=gray>Нет доступных телепортов</color>",
        };

        var root = new DockPanelPacket
        {
            Style = new GUIStylePacket
            {
                Background = System.Drawing.Color.FromArgb(242, 20, 20, 20),
                Border = System.Drawing.Color.FromArgb(255, 89, 89, 89),
                BorderWidth = 2,
                Padding = new Margins(0, 0, 0, 0),
            },
            Children =
            [
                new DockPanelPacket
                {
                    AttachedProperties = [new("DockPanel.Dock", "Top")],
                    Style = new GUIStylePacket
                    {
                        Margin = new Margins(0, 0, 0, 0),
                        Padding = new Margins(0, 0, 0, 0),
                    },
                    Children =
                    [
                        new TextPacket
                        {
                            Text = "<color=#B2A680>Телепорты</color>",
                            AttachedProperties = [new("DockPanel.Dock", "Left")],
                        },
                        new TextPacket
                        {
                            Text = "<color=#B3B3B3>×</color>",
                            OnClickContext = "teleport_close",
                            AttachedProperties = [new("DockPanel.Dock", "Right")],
                        },
                    ],
                },
                text,
            ],
        };

        onReceived.Invoke(new ServerPacket(new OpenWindowPacket("teleport", 400, 200, root)));
        WindowOpen = true;
    }

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
        operations.Run(
            "DummyTeleportManager.SendTeleport",
            _ => SendTeleportAsync(destX, destY));
    }

    private async UniTask SendTeleportAsync(ushort destX, ushort destY)
    {
        if (onTeleport != null)
        {
            await onTeleport(destX, destY);
        }

        onReceived.Invoke(new ServerPacket(new TeleportPacket(destX, destY, false)));
        onReceived.Invoke(new ServerPacket(new CloseWindowPacket()));
    }
}
