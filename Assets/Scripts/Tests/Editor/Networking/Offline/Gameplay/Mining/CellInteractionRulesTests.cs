#nullable enable

using Kern.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using NUnit.Framework;

using Kern.Networking.Offline;

namespace Kern.Tests.Player;

public sealed class CellInteractionRulesTests
{
    [TestCase(CellRenderLayer.Foreground, true)]
    [TestCase(CellRenderLayer.Background, false)]
    [TestCase(CellRenderLayer.Underlay, false)]
    [TestCase(CellRenderLayer.WorldBoundary, false)]
    public void OfflineExcavation_DependsOnDrawLayer(CellRenderLayer drawLayer, bool expected)
    {
        var config = CellPacket.Create(drawLayer: drawLayer);
        Assert.That(CellInteractionRules.CanMine(config), Is.EqualTo(expected));
    }
}
