#nullable enable

using System.Collections;
using System.Collections.Generic;
using Kern.Core.Models;
using Kern.Networking.Processors;
using Kern.Game.Inventory;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Inventory;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Networking;

[TestFixture]
public class InventoryProcessorTests
{
    private InventoryModel _model = null!;
    private InventoryProcessor _processor = null!;

    [SetUp]
    public void SetUp()
    {
        _model = new InventoryModel();
        _processor = new InventoryProcessor(_model);
    }

    [Test]
    public void Process_InventoryPacket_AddsNewItemsToEmptySlots()
    {
        var changes = new Dictionary<ItemType, long>
        {
            { (ItemType)1, 10 },
            { (ItemType)2, 5 },
        };

        var packet = new InventoryPacket(changes);
        _processor.Process(packet);

        var slot0 = _model.GetSlot(0);
        var slot1 = _model.GetSlot(1);

        Assert.IsNotNull(slot0);
        Assert.AreEqual((ItemType)1, slot0!.ItemType);
        Assert.AreEqual(10, slot0.Quantity);

        Assert.IsNotNull(slot1);
        Assert.AreEqual((ItemType)2, slot1!.ItemType);
        Assert.AreEqual(5, slot1.Quantity);
    }

    [Test]
    public void Process_InventoryPacket_UpdatesExistingItemQuantity()
    {
        _model.SetSlot(0, new ItemData("Iron", Color.gray, 5) { ItemType = (ItemType)1 });

        var changes = new Dictionary<ItemType, long>
        {
            { (ItemType)1, 25 },
        };

        var packet = new InventoryPacket(changes);
        _processor.Process(packet);

        var slot0 = _model.GetSlot(0);
        Assert.IsNotNull(slot0);
        Assert.AreEqual((ItemType)1, slot0!.ItemType);
        Assert.AreEqual(25, slot0.Quantity);
    }

    [Test]
    public void Process_InventoryPacket_RemovesItemWhenQuantityZeroOrNegative()
    {
        _model.SetSlot(0, new ItemData("Iron", Color.gray, 5) { ItemType = (ItemType)1 });
        _model.SetSlot(1, new ItemData("Gold", Color.yellow, 3) { ItemType = (ItemType)2 });

        var changes = new Dictionary<ItemType, long>
        {
            { (ItemType)1, 0 },
        };

        var packet = new InventoryPacket(changes);
        _processor.Process(packet);

        Assert.IsNull(_model.GetSlot(0));
        Assert.IsNotNull(_model.GetSlot(1));
    }

    [Test]
    public void Process_SelectItemPacket_UpdatesSelectedItemMetadata()
    {
        _model.SetSlot(2, new ItemData("Unknown", Color.gray, 1) { ItemType = (ItemType)1 });
        _model.SelectSlot(2);

        var packet = new SelectItemPacket(
            (ItemType)1,
            "Super Pickaxe",
            "Mines instantly",
            0,
            0,
            0,
            false,
            new BitArray(8));

        _processor.Process(packet);

        var item = _model.GetSlot(2);
        Assert.IsNotNull(item);
        Assert.AreEqual("Super Pickaxe", item!.Name);
        Assert.AreEqual("Mines instantly", item.Description);
    }

    [Test]
    public void Process_DeselectItemPacket_ClearsSelection()
    {
        _model.SelectSlot(2);
        Assert.AreEqual(2, _model.SelectedSlot);

        var packet = new DeselectItemPacket();
        _processor.Process(packet);

        Assert.AreEqual(-1, _model.SelectedSlot);
    }
}
