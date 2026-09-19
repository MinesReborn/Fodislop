#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Kern.Core.Interfaces;
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

internal sealed class DummyMissionRunner(Action<ServerPacket> onReceived)
{
    private readonly record struct MissionDef(
        int Id,
        string Title,
        string Description,
        long Target,
        ItemType RewardItem,
        long RewardAmount);

    private static readonly MissionDef[] _Missions =
    [
        new(0, "Копатель-ученик", "Сломайте 50 блоков", 50, ItemType.Cred, 25),
        new(1, "Опытный копатель", "Сломайте 200 блоков", 200, ItemType.Cred, 100),
        new(2, "Мастер-копатель", "Сломайте 500 блоков", 500, ItemType.Cred, 300),
    ];

    public int ActiveMissionID { get; private set; } = -1;
    public long MissionProgress { get; private set; }
    public bool[] MissionCompleted { get; } = new bool[_Missions.Length];
    public int MissionCount => _Missions.Length;

    public void SendMissionWindow(ushort x, ushort y)
    {
        var rows = new List<IGUIComponentPacket>();
        for (int i = 0; i < _Missions.Length; i++)
        {
            var m = _Missions[i];
            string status = ActiveMissionID == m.Id
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
                        Text = "<color=#B2A680>Миссии</color>",
                        AttachedProperties = [new("DockPanel.Dock", "Left")],
                    },
                    new TextPacket
                    {
                        Text = "<color=#B3B3B3>×</color>",
                        OnClickContext = "missions_close",
                        AttachedProperties = [new("DockPanel.Dock", "Right")],
                    },
                ],
            },
            scrollViewer,
        };

        if (ActiveMissionID >= 0)
        {
            rootChildren.Add(new TextPacket
            {
                Text = "<color=#B08050>Отменить миссию</color>",
                OnClickContext = "mission_cancel",
                AttachedProperties = [new("DockPanel.Dock", "Bottom")],
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

        onReceived.Invoke(new ServerPacket(new OpenWindowPacket("missions", 400, 300, root)));
    }

    public void StartMission(int missionID, ushort x, ushort y)
    {
        if (missionID < 0 || missionID >= _Missions.Length)
        {
            return;
        }

        if (MissionCompleted[missionID])
        {
            return;
        }

        var m = _Missions[missionID];
        ActiveMissionID = missionID;
        MissionProgress = 0;
        onReceived.Invoke(new ServerPacket(new CloseWindowPacket()));
        onReceived.Invoke(new ServerPacket(new MissionInitPacket(string.Empty, 0, 0, m.Title, m.Description)));
        onReceived.Invoke(new ServerPacket(new MissionProgressPacket(0, m.Target)));
        onReceived.Invoke(new ServerPacket(new MissionArrowPacket((ushort)(x + 2), (ushort)(y + 2))));
    }

    public void CancelMission()
    {
        if (ActiveMissionID < 0)
        {
            onReceived.Invoke(new ServerPacket(new CloseWindowPacket()));
            return;
        }

        ActiveMissionID = -1;
        MissionProgress = 0;
        onReceived.Invoke(new ServerPacket(new CloseWindowPacket()));
        onReceived.Invoke(new ServerPacket(new MissionInitPacket(string.Empty, 0, 0, string.Empty, string.Empty)));
    }

    public void OnBlockMined(Dictionary<ItemType, long> inventory)
    {
        if (ActiveMissionID < 0)
        {
            return;
        }

        var m = _Missions[ActiveMissionID];
        MissionProgress++;
        onReceived.Invoke(new ServerPacket(new MissionProgressPacket(MissionProgress, m.Target)));
        if (MissionProgress >= m.Target)
        {
            CompleteMission(inventory);
        }
    }

    public void Reset()
    {
        ActiveMissionID = -1;
        MissionProgress = 0;
        Array.Clear(MissionCompleted, 0, MissionCompleted.Length);
    }

    private void CompleteMission(Dictionary<ItemType, long> inventory)
    {
        if (ActiveMissionID < 0)
        {
            return;
        }

        var m = _Missions[ActiveMissionID];
        inventory.TryGetValue(m.RewardItem, out long current);
        inventory[m.RewardItem] = current + m.RewardAmount;
        onReceived.Invoke(new ServerPacket(new InventoryPacket(
            new Dictionary<ItemType, long> { { m.RewardItem, current + m.RewardAmount } })));

        MissionCompleted[ActiveMissionID] = true;
        ActiveMissionID = -1;
        MissionProgress = 0;

        onReceived.Invoke(new ServerPacket(new MissionInitPacket(string.Empty, 0, 0, string.Empty, string.Empty)));
        onReceived.Invoke(new ServerPacket(new ModalWindowPacket(
            "Миссия выполнена!",
            $"Вы завершили миссию \"{m.Title}\"!\n\nНаграда: {m.RewardAmount} кредитов.",
            "OK",
            string.Empty)));
    }
}
