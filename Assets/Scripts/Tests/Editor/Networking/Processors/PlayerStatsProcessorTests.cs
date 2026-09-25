#nullable enable

using Kern.Core.Interfaces;
using Kern.Networking.Processors;
using Kern.UI.HUD.Player.Model;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Information;
using NUnit.Framework;

namespace Kern.Tests.Networking;

[TestFixture]
public class PlayerStatsProcessorTests
{
    private PlayerStatsModel _stats = null!;
    private PlayerStatsProcessor _processor = null!;

    [SetUp]
    public void SetUp()
    {
        _stats = new PlayerStatsModel();
        _processor = new PlayerStatsProcessor(_stats);
    }

    [Test]
    public void Process_HealthPacket_UpdatesHealthAndMaxHealth()
    {
        var packet = new HealthPacket(75, 100);
        _processor.Process(packet);

        Assert.AreEqual(75, _stats.Health);
        Assert.AreEqual(100, _stats.MaxHealth);
        Assert.AreEqual(0.75f, _stats.HealthPercent, 0.001f);
    }

    [Test]
    public void Process_LevelPacket_UpdatesPlayerLevel()
    {
        var packet = new LevelPacket(42);
        _processor.Process(packet);

        Assert.AreEqual(42, _stats.Level);
    }

    [Test]
    public void Process_CurrencyPacket_UpdatesMoneyAndCreds()
    {
        var packet = new CurrencyPacket(1500, 250);
        _processor.Process(packet);

        Assert.AreEqual(1500, _stats.Money);
        Assert.AreEqual(250, _stats.Creds);
    }

    [Test]
    public void Process_BasketPacket_UpdatesBasketCapacityAndContents()
    {
        long[] contents = [10, 20, 30];
        var packet = new BasketPacket(100, contents);
        _processor.Process(packet);

        Assert.AreEqual(100, _stats.BasketCapacity);
        Assert.AreEqual(contents, _stats.BasketContents);
    }

    [Test]
    public void Process_MaxDepthPacket_UpdatesMaxDepth()
    {
        var packet = new MaxDepthPacket(512);
        _processor.Process(packet);

        Assert.AreEqual(512, _stats.MaxDepth);
    }
}
