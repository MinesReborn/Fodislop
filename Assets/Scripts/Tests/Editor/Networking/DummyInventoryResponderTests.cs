#nullable enable

using System.Collections.Generic;
using MinesServer.Data;
using MinesServer.Networking.Connection.Client;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Information;
using NUnit.Framework;

namespace Kern.Tests.Networking;

public sealed class DummyInventoryResponderTests
{
    [Test]
    public void Select_SendsItemDescriptionPacket()
    {
        var sent = new List<ServerPacket>();
        var responder = CreateResponder(sent, _ => { });

        responder.Select(ItemType.Rem);

        Assert.That(sent, Has.Count.EqualTo(1));
        var payload = (MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket)sent[0].Payload;
        Assert.That(payload.Item, Is.EqualTo(ItemType.Rem));
        Assert.That(payload.Name, Is.Not.Empty);
    }

    [Test]
    public void UseRepair_RestoresHealthAndConsumesOneItem()
    {
        var sent = new List<ServerPacket>();
        int restoredHealth = 0;
        DummyInventoryResponder responder = CreateResponder(sent, health => restoredHealth = health);
        responder.ReplaceItems(new Dictionary<ItemType, long> { [ItemType.Rem] = 2 });
        responder.Select(ItemType.Rem);
        sent.Clear();

        responder.Use(10, 20, Direction.Up);

        Assert.That(restoredHealth, Is.EqualTo(500));
        Assert.That(responder.Items[ItemType.Rem], Is.EqualTo(1));
        var health = (HealthPacket)sent[0].Payload;
        Assert.That(health.Current, Is.EqualTo(500));
        Assert.That(health.Max, Is.EqualTo(500));
    }

    private static DummyInventoryResponder CreateResponder(
        List<ServerPacket> sent,
        System.Action<int> setHealth)
    {
        return new DummyInventoryResponder(
            sent.Add,
            (_, _, _, _) => { },
            [],
            setHealth,
            (_, _) => CellType.Empty,
            (_, _, _) => { });
    }
}
