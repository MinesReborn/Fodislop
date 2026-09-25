#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.Networking;
using Kern.Networking.Processors;
using Kern.UI.HUD.Player.Model;
using MinesServer.Networking.Client;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Information.StatusPanel;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Networking;

[TestFixture]
public class StatusProcessorTests
{
    private PlayerStatsModel _stats = null!;
    private StatusProcessor _processor = null!;
    private NetworkStatusModel _statusModel = null!;
    private StubNetworkService _networkService = null!;

    [SetUp]
    public void SetUp()
    {
        _stats = new PlayerStatsModel();
        _statusModel = new NetworkStatusModel();
        _networkService = new StubNetworkService();

        _processor = new StatusProcessor(_stats, _statusModel, _networkService);
    }

    [Test]
    public void Process_OnlinePacket_UpdatesOnlineCounters()
    {
        var packet = new OnlinePacket(120, 15);
        _processor.Process(packet);

        Assert.AreEqual(120, _statusModel.OnlinePlayers);
        Assert.AreEqual(15, _statusModel.OnlineProgrammator);
    }

    [Test]
    public void Process_PingPacket_UpdatesPingAndRepliesWithPong()
    {
        var packet = new PingPacket(45, 123456789);
        _processor.Process(packet);

        Assert.AreEqual(123456789, _statusModel.PingMs);
        Assert.IsTrue(_networkService.SentPong, "PingPacket must be answered with a PongPacket.");
    }

    private sealed class StubNetworkService : INetworkService
    {
        // События требует INetworkService; стаб их не raises — пустые
        // акцессоры вместо CS0067.
        public event Action? PacketBatchStarted { add { } remove { } }
        public event Action? PacketBatchCompleted { add { } remove { } }

        public bool SentPong { get; private set; }

        public void Subscribe<T>(Action<T> handler)
        {
        }

        public void Unsubscribe<T>(Action<T> handler)
        {
        }

        public void SendAction(IActionClientPacket action)
        {
        }

        public void Send(IRootClientPacket packet)
        {
            if (packet is PongPacket)
            {
                SentPong = true;
            }
        }
    }

    [Test]
    public void Process_AddStatusLinePacket_AddsStatusLineToStats()
    {
        var packet = new AddStatusLinePacket(
            0,
            System.Drawing.Color.Green,
            "buff_speed",
            ["Speed +20%", "60s"]);

        _processor.Process(packet);

        Assert.IsTrue(_stats.StatusLines.ContainsKey("buff_speed"));
        var line = _stats.StatusLines["buff_speed"];
        Assert.AreEqual("Speed +20%", line.Text[0]);
    }

    [Test]
    public void Process_ClearStatusLinePacket_RemovesStatusLine()
    {
        _stats.AddStatusLine("buff_speed", ["Speed +20%"], Color.green, 0, 0);
        Assert.AreEqual(1, _stats.StatusLines.Count);

        var packet = new ClearStatusLinePacket("buff_speed");
        _processor.Process(packet);

        Assert.IsFalse(_stats.StatusLines.ContainsKey("buff_speed"));
    }

    [Test]
    public void Process_ClearStatusPacket_ClearsAllStatusLines()
    {
        _stats.AddStatusLine("buff1", ["Buff 1"], Color.green, 0, 0);
        _stats.AddStatusLine("buff2", ["Buff 2"], Color.blue, 0, 0);
        Assert.AreEqual(2, _stats.StatusLines.Count);

        var packet = default(ClearStatusPacket);
        _processor.Process(packet);

        Assert.AreEqual(0, _stats.StatusLines.Count);
    }
}
