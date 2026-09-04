#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fodinae.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Information.StatusPanel;
using MinesServer.Networking.Server.Packets.Inventory;
using MinesServer.Networking.Server.Packets.Mission;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.Utilities;
using MinesServer.Networking.Server.Packets.World;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyMissionRunner
{
    private readonly Action<ServerPacket> _onReceived;

    private struct MissionDef
    {
        public int Id;
        public string Title;
        public string Description;
        public long Target;
        public ItemType RewardItem;
        public long RewardAmount;
    }

    private static readonly MissionDef[] Missions = new[]
    {
        new MissionDef { Id = 0, Title = "Копатель-ученик", Description = "Сломайте 50 блоков", Target = 50, RewardItem = ItemType.Cred, RewardAmount = 25 },
        new MissionDef { Id = 1, Title = "Опытный копатель", Description = "Сломайте 200 блоков", Target = 200, RewardItem = ItemType.Cred, RewardAmount = 100 },
        new MissionDef { Id = 2, Title = "Мастер-копатель", Description = "Сломайте 500 блоков", Target = 500, RewardItem = ItemType.Cred, RewardAmount = 300 },
    };

    public int ActiveMissionId { get; private set; } = -1;
    public long MissionProgress { get; private set; }
    public bool[] MissionCompleted { get; } = new bool[Missions.Length];
    public int MissionCount => Missions.Length;

    public DummyMissionRunner(Action<ServerPacket> onReceived)
    {
        _onReceived = onReceived;
    }

    public void SendMissionWindow(ushort x, ushort y)
    {
        var rows = new List<IGUIComponentPacket>();
        for (int i = 0; i < Missions.Length; i++)
        {
            var m = Missions[i];
            string status = ActiveMissionId == m.Id
                ? $"<color=yellow>Активно: {MissionProgress}/{m.Target}</color>"
                : MissionCompleted[m.Id]
                    ? "<color=lime>✓ Выполнено</color>"
                    : "<color=#B2A680>Выбрать</color>";
            rows.Add(new TextPacket
            {
                Text = $"<color=white>{m.Title}</color>\n<color=#B2A680>{m.Description}</color>  {status}",
                OnClickContext = ".",
                Style = new GUIStylePacket
                {
                    Background = System.Drawing.Color.FromArgb(242, 26, 26, 26),
                    Border = System.Drawing.Color.FromArgb(255, 89, 89, 89),
                    BorderWidth = 2,
                    Padding = new Margins(8, 12, 8, 12),
                    Margin = new Margins(0, 0, 4, 0),
                },
            });
        }

        var scrollViewer = new ScrollViewerPacket
        {
            VerticalScrollBar = ScrollbarVisibility.Auto,
            HorizontalScrollBar = ScrollbarVisibility.Auto,
            Children = rows.ToArray(),
        };

        var rootChildren = new List<IGUIComponentPacket>
        {
            new DockPanelPacket
            {
                AttachedProperties = new StringPairPacket[]
                {
                    new("DockPanel.Dock", "Top"),
                },
                Style = new GUIStylePacket
                {
                    Margin = new Margins(0, 0, 10, 0),
                    Padding = new Margins(0, 0, 0, 0),
                },
                Children = new List<IGUIComponentPacket>
                {
                    new TextPacket
                    {
                        Text = "<color=#B2A680>Миссии</color>",
                        AttachedProperties = new StringPairPacket[]
                        {
                            new("DockPanel.Dock", "Left"),
                        },
                    },
                    new TextPacket
                    {
                        Text = "<color=#B3B3B3>×</color>",
                        OnClickContext = "missions_close",
                        AttachedProperties = new StringPairPacket[]
                        {
                            new("DockPanel.Dock", "Right"),
                        },
                    },
                },
            },
            scrollViewer,
        };

        if (ActiveMissionId >= 0)
        {
            rootChildren.Add(new TextPacket
            {
                Text = "<color=#B08050>Отменить миссию</color>",
                OnClickContext = "mission_cancel",
                AttachedProperties = new StringPairPacket[]
                {
                    new("DockPanel.Dock", "Bottom"),
                },
                Style = new GUIStylePacket
                {
                    Margin = new Margins(0, 0, 10, 0),
                    Padding = new Margins(6, 6, 6, 6),
                    Background = System.Drawing.Color.FromArgb(242, 30, 20, 20),
                    Border = System.Drawing.Color.FromArgb(255, 89, 89, 89),
                    BorderWidth = 2,
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
                Padding = new Margins(2, 8, 2, 8),
            },
            Children = rootChildren,
        };

        _onReceived.Invoke(new ServerPacket(new OpenWindowPacket("missions", 400, 300, root)));
    }

    public void StartMission(int missionId, ushort x, ushort y)
    {
        if (missionId < 0 || missionId >= Missions.Length)
        {
            return;
        }

        if (MissionCompleted[missionId])
        {
            return;
        }

        var m = Missions[missionId];
        ActiveMissionId = missionId;
        MissionProgress = 0;
        _onReceived.Invoke(new ServerPacket(new CloseWindowPacket()));
        _onReceived.Invoke(new ServerPacket(new MissionInitPacket(string.Empty, 0, 0, m.Title, m.Description)));
        _onReceived.Invoke(new ServerPacket(new MissionProgressPacket(0, m.Target)));
            _onReceived.Invoke(new ServerPacket(new MissionArrowPacket((ushort)(x + 2), (ushort)(y + 2))));
    }

    public void CancelMission()
    {
        if (ActiveMissionId < 0)
        {
            _onReceived.Invoke(new ServerPacket(new CloseWindowPacket()));
            return;
        }

        ActiveMissionId = -1;
        MissionProgress = 0;
        _onReceived.Invoke(new ServerPacket(new CloseWindowPacket()));
        _onReceived.Invoke(new ServerPacket(new MissionInitPacket(string.Empty, 0, 0, string.Empty, string.Empty)));
    }

    public void OnBlockMined(Dictionary<ItemType, long> inventory)
    {
        if (ActiveMissionId < 0)
        {
            return;
        }

        var m = Missions[ActiveMissionId];
        MissionProgress++;
        _onReceived.Invoke(new ServerPacket(new MissionProgressPacket(MissionProgress, m.Target)));
        if (MissionProgress >= m.Target)
        {
            CompleteMission(inventory);
        }
    }

    public void Reset()
    {
        ActiveMissionId = -1;
        MissionProgress = 0;
        Array.Clear(MissionCompleted, 0, MissionCompleted.Length);
    }

    private void CompleteMission(Dictionary<ItemType, long> inventory)
    {
        if (ActiveMissionId < 0)
        {
            return;
        }

        var m = Missions[ActiveMissionId];
        inventory.TryGetValue(m.RewardItem, out long current);
        inventory[m.RewardItem] = current + m.RewardAmount;
        _onReceived.Invoke(new ServerPacket(new InventoryPacket(
            new Dictionary<ItemType, long> { { m.RewardItem, current + m.RewardAmount } })));

        MissionCompleted[ActiveMissionId] = true;
        ActiveMissionId = -1;
        MissionProgress = 0;

        _onReceived.Invoke(new ServerPacket(new MissionInitPacket(string.Empty, 0, 0, string.Empty, string.Empty)));
        _onReceived.Invoke(new ServerPacket(new ModalWindowPacket(
            "Миссия выполнена!",
            $"Вы завершили миссию \"{m.Title}\"!\n\nНаграда: {m.RewardAmount} кредитов.",
            "OK",
            string.Empty)));
    }
}
