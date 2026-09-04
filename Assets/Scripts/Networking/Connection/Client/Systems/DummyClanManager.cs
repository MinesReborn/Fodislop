#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Shared.Packets;
using MinesServer.Networking.Client.Packets.GUI;
using UnityEngine;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyClanManager
{
    private readonly Action<ServerPacket> _onReceived;
    private ushort _clanId;
    private static readonly (ushort Id, string Name, string Desc)[] MockClans =
    {
        (1, "Альфа", "Старейший клан на сервере"),
    };

    public DummyClanManager(Action<ServerPacket> onReceived)
    {
        _onReceived = onReceived;
    }

    public ushort ClanId => _clanId;

    public void SendClanListWindow()
    {
        var items = new List<IGUIComponentPacket>();
        foreach (var clan in MockClans)
        {
            items.Add(new DockPanelPacket
            {
                Style = new GUIStylePacket
                {
                    Margin = new Margins(0, 0, 4, 0),
                    Padding = new Margins(4, 6, 4, 4),
                    Background = System.Drawing.Color.FromArgb(30, 60, 60, 60),
                    Border = System.Drawing.Color.FromArgb(60, 80, 80, 80),
                    BorderWidth = 1,
                },
                Children = new List<IGUIComponentPacket>
                {
                    new ImagePacket
                    {
                        URI = $"clan/{clan.Id}.png",
                        Width = 16,
                        Height = 16,
                        AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Left") },
                    },
                    new TextPacket
                    {
                        Text = $"<color=white><b>Клан «{clan.Name}»</b>  <color=#888888>(ID: {clan.Id})</color></color>",
                        OnClickContext = ".",
                        AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Left") },
                    },
                },
            });
            items.Add(new TextPacket
            {
                Text = $"<color=#999999>{clan.Desc}</color>",
                Style = new GUIStylePacket
                {
                    Margin = new Margins(0, 0, 8, 0),
                    Padding = new Margins(0, 10, 0, 0),
                },
            });
        }

        var root = new DockPanelPacket
        {
            Style = new GUIStylePacket
            {
                Background = System.Drawing.Color.FromArgb(242, 20, 20, 20),
                Border = System.Drawing.Color.FromArgb(255, 89, 89, 89),
                BorderWidth = 2,
                Padding = new Margins(8, 8, 8, 8),
            },
            Children = new List<IGUIComponentPacket>
            {
                new DockPanelPacket
                {
                    AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Top") },
                    Children = new List<IGUIComponentPacket>
                    {
                        new TextPacket
                        {
                            Text = "<color=#B2A680><b>Доступные кланы</b></color>",
                            AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Left") },
                        },
                        new TextPacket
                        {
                            Text = "<color=#B3B3B3>×</color>",
                            OnClickContext = "clan_close",
                            AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Right") },
                        },
                    },
                },
                new ScrollViewerPacket
                {
                    AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Top") },
                    Style = new GUIStylePacket
                    {
                        Margin = new Margins(6, 0, 0, 0),
                    },
                    Children = items,
                },
            },
        };

        _onReceived.Invoke(new ServerPacket(new OpenWindowPacket("clan_list", 320, 260, root)));
    }

    public void SendClanInfoWindow()
    {
        string clanName = _clanId.ToString();
        string clanDesc = string.Empty;
        foreach (var c in MockClans)
        {
            if (c.Id == _clanId)
            {
                clanName = c.Name;
                clanDesc = c.Desc;
                break;
            }
        }

        var root = new DockPanelPacket
        {
            Style = new GUIStylePacket
            {
                Background = System.Drawing.Color.FromArgb(242, 20, 20, 20),
                Border = System.Drawing.Color.FromArgb(255, 89, 89, 89),
                BorderWidth = 2,
                Padding = new Margins(8, 8, 8, 8),
            },
            Children = new List<IGUIComponentPacket>
            {
                new DockPanelPacket
                {
                    AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Top") },
                    Children = new List<IGUIComponentPacket>
                    {
                        new TextPacket
                        {
                            Text = "<color=#B2A680><b>Мой клан</b></color>",
                            AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Left") },
                        },
                        new TextPacket
                        {
                            Text = "<color=#B3B3B3>×</color>",
                            OnClickContext = "clan_close",
                            AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Right") },
                        },
                    },
                },
                new TextPacket
                {
                    Text = $"<color=white><b>Клан «{clanName}»</b></color>\n<color=#888888>ID: {_clanId}</color>\n<color=#999999>{clanDesc}</color>",
                    AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Top") },
                    Style = new GUIStylePacket
                    {
                        Margin = new Margins(8, 0, 8, 0),
                    },
                },
                new TextPacket
                {
                    Text = "<color=#FF6666>Покинуть клан</color>",
                    OnClickContext = ".",
                    AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Top") },
                    Style = new GUIStylePacket
                    {
                        Padding = new Margins(6, 10, 6, 6),
                        Background = System.Drawing.Color.FromArgb(40, 80, 40, 40),
                        Border = System.Drawing.Color.FromArgb(60, 120, 60, 60),
                        BorderWidth = 1,
                        Margin = new Margins(0, 0, 0, 0),
                    },
                },
            },
        };

        _onReceived.Invoke(new ServerPacket(new OpenWindowPacket("clan_info", 300, 200, root)));
    }

    public void HandleElementClick(ElementClickPacket packet)
    {
        if (packet.WindowTag == "join_clan")
        {
            _clanId = 1;
            _onReceived.Invoke(new ServerPacket(new ShowClanPacket(1)));
        }
        else if (packet.WindowTag == "leave_clan")
        {
            _clanId = 0;
            _onReceived.Invoke(new ServerPacket(new HideClanPacket()));
        }
        else if (packet.WindowTag == "clan_list")
        {
            if (packet.ElementIndex == 0)
            {
                _onReceived.Invoke(new ServerPacket(new CloseWindowPacket()));
            }
            else
            {
                int idx = packet.ElementIndex - 1;
                if (idx >= 0 && idx < MockClans.Length)
                {
                    _clanId = MockClans[idx].Id;
                    _onReceived.Invoke(new ServerPacket(new ShowClanPacket(_clanId)));
                    _onReceived.Invoke(new ServerPacket(new CloseWindowPacket()));
                }
            }
        }
        else if (packet.WindowTag == "clan_info")
        {
            if (packet.ElementIndex == 0)
            {
                _onReceived.Invoke(new ServerPacket(new CloseWindowPacket()));
            }
            else
            {
                _clanId = 0;
                _onReceived.Invoke(new ServerPacket(new HideClanPacket()));
                _onReceived.Invoke(new ServerPacket(new CloseWindowPacket()));
            }
        }
    }

    public void HandleOpenClanClick()
    {
        if (_clanId == 0)
        {
            SendClanListWindow();
        }
        else
        {
            SendClanInfoWindow();
        }
    }
}
