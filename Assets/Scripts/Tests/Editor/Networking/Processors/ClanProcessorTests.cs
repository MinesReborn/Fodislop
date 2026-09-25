#nullable enable

using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.Networking.Processors;
using Kern.UI.HUD.Player.Model;
using MinesServer.Networking.Server.Packets.Information;
using NUnit.Framework;

namespace Kern.Tests.Networking;

[TestFixture]
public class ClanProcessorTests
{
    private PlayerStatsModel _stats = null!;
    private ClanProcessor _processor = null!;

    [SetUp]
    public void SetUp()
    {
        _stats = new PlayerStatsModel();
        _processor = new ClanProcessor(_stats);
    }

    [Test]
    public void Process_ShowClanPacket_SetsClanIdInPlayerStats()
    {
        var packet = new ShowClanPacket(777);
        _processor.Process(packet);

        Assert.AreEqual(777, _stats.ClanID);
    }

    [Test]
    public void Process_HideClanPacket_ResetsClanIdToZero()
    {
        _stats.SetClanID(777);
        Assert.AreEqual(777, _stats.ClanID);

        var packet = new HideClanPacket();
        _processor.Process(packet);

        Assert.AreEqual(0, _stats.ClanID);
    }
}
