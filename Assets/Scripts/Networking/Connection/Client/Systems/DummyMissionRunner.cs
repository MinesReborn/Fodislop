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

    private static readonly MissionDef[] s_missions =
    [
        new(0, "Копатель-ученик", "Сломайте 50 блоков", 50, ItemType.Cred, 25),
        new(1, "Опытный копатель", "Сломайте 200 блоков", 200, ItemType.Cred, 100),
        new(2, "Мастер-копатель", "Сломайте 500 блоков", 500, ItemType.Cred, 300),
    ];

    private bool _persistentMission;
    private int _closeElement = -1;
    private int _cancelElement = -1;
    private int[] _missionElements = [];

    public int ActiveMissionId { get; private set; } = -1;
    public long MissionProgress { get; private set; }
    public bool[] MissionCompleted { get; } = new bool[s_missions.Length];
    public int MissionCount => s_missions.Length;

    public void StartPersistentMission(ushort x, ushort y)
    {
        MissionCompleted[0] = false;
        StartMission(0, x, y);
        _persistentMission = true;
    }

    public void SendMissionWindow(ushort x, ushort y)
    {
        var rows = new List<IGUIComponentPacket>();
        for (int i = 0; i < s_missions.Length; i++)
        {
            var m = s_missions[i];
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

        // "." collects inputs from the clicked text itself: it has none.
        var close = new TextPacket
        {
            Text = "<color=#B3B3B3>×</color>",
            OnClickContext = ".",
            AttachedProperties = [new("DockPanel.Dock", "Right")],
        };
        TextPacket? cancel = null;
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
                    close,
                ],
            },
            scrollViewer,
        };

        if (ActiveMissionId >= 0)
        {
            cancel = new TextPacket
            {
                Text = "<color=#B08050>Отменить миссию</color>",
                OnClickContext = ".",
                AttachedProperties = [new("DockPanel.Dock", "Bottom")],
                Style = new GUIStylePacket
                {
                    Margin = new Margins(0, 0, 10, 0),
                    Padding = new Margins(6, 6, 6, 6),
                    Background = System.Drawing.Color.FromArgb(242, 30, 20, 20),
                    Border = System.Drawing.Color.FromArgb(255, 89, 89, 89),
                    BorderWidth = 2,
                },
            };
            rootChildren.Add(cancel);
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

        _closeElement = DummyWindowElements.IndexOf(root, close);
        _cancelElement = cancel == null ? -1 : DummyWindowElements.IndexOf(root, cancel);
        _missionElements = rows.Select(row => DummyWindowElements.IndexOf(root, row)).ToArray();
        onReceived.Invoke(new ServerPacket(new OpenWindowPacket("missions", 400, 300, root)));
    }

    public bool IsCloseElement(int elementIndex) => elementIndex == _closeElement;

    public bool IsCancelElement(int elementIndex) => _cancelElement >= 0 && elementIndex == _cancelElement;

    public int MissionOfElement(int elementIndex) => Array.IndexOf(_missionElements, elementIndex);

    public void StartMission(int missionId, ushort x, ushort y)
    {
        if (missionId < 0 || missionId >= s_missions.Length)
        {
            return;
        }

        if (MissionCompleted[missionId])
        {
            return;
        }

        var m = s_missions[missionId];
        _persistentMission = false;
        ActiveMissionId = missionId;
        MissionProgress = 0;
        onReceived.Invoke(new ServerPacket(new CloseWindowPacket()));
        onReceived.Invoke(new ServerPacket(new MissionInitPacket(string.Empty, 0, 0, m.Title, m.Description)));
        onReceived.Invoke(new ServerPacket(new MissionProgressPacket(0, m.Target)));
        onReceived.Invoke(new ServerPacket(new MissionArrowPacket((ushort)(x + 2), (ushort)(y + 2))));
    }

    public void CancelMission()
    {
        if (ActiveMissionId < 0)
        {
            onReceived.Invoke(new ServerPacket(new CloseWindowPacket()));
            return;
        }

        ActiveMissionId = -1;
        MissionProgress = 0;
        _persistentMission = false;
        onReceived.Invoke(new ServerPacket(new CloseWindowPacket()));
        onReceived.Invoke(new ServerPacket(new MissionInitPacket(string.Empty, 0, 0, string.Empty, string.Empty)));
    }

    public void OnBlockMined(Dictionary<ItemType, long> inventory)
    {
        if (ActiveMissionId < 0)
        {
            return;
        }

        if (MissionCompleted[ActiveMissionId])
        {
            return;
        }

        var m = s_missions[ActiveMissionId];
        MissionProgress++;
        onReceived.Invoke(new ServerPacket(new MissionProgressPacket(MissionProgress, m.Target)));
        if (MissionProgress >= m.Target)
        {
            CompleteMission(inventory);
        }
    }

    public void Reset()
    {
        ActiveMissionId = -1;
        MissionProgress = 0;
        _persistentMission = false;
        Array.Clear(MissionCompleted, 0, MissionCompleted.Length);
    }

    private void CompleteMission(Dictionary<ItemType, long> inventory)
    {
        if (ActiveMissionId < 0)
        {
            return;
        }

        var m = s_missions[ActiveMissionId];
        inventory.TryGetValue(m.RewardItem, out long current);
        inventory[m.RewardItem] = current + m.RewardAmount;
        onReceived.Invoke(new ServerPacket(new InventoryPacket(
            new Dictionary<ItemType, long> { { m.RewardItem, current + m.RewardAmount } })));

        MissionCompleted[ActiveMissionId] = true;
        ActiveMissionId = -1;
        MissionProgress = 0;

        if (_persistentMission)
        {
            ActiveMissionId = m.Id;
            MissionProgress = m.Target;
            onReceived.Invoke(new ServerPacket(new MissionProgressPacket(MissionProgress, m.Target)));
            onReceived.Invoke(new ServerPacket(new ModalWindowPacket(
                "Миссия выполнена!",
                $"Вы завершили миссию \"{m.Title}\"!\n\nНаграда: {m.RewardAmount} кредитов.",
                "OK",
                string.Empty)));
            return;
        }

        onReceived.Invoke(new ServerPacket(new MissionInitPacket(string.Empty, 0, 0, string.Empty, string.Empty)));
        onReceived.Invoke(new ServerPacket(new ModalWindowPacket(
            "Миссия выполнена!",
            $"Вы завершили миссию \"{m.Title}\"!\n\nНаграда: {m.RewardAmount} кредитов.",
            "OK",
            string.Empty)));
    }
}
