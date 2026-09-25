#nullable enable

using System.Collections;
using System.Collections.Generic;
using Kern.Core.Interfaces;
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
    private static readonly ItemType[] KnownTypes =
    [
        ItemType.Rem,
        ItemType.Battery,
        ItemType.Nano,
    ];

    private InventoryModel _model = null!;
    private InventoryProcessor _processor = null!;

    [SetUp]
    public void SetUp()
    {
        _model = new InventoryModel();
        _processor = new InventoryProcessor(_model, new StubItemCatalog(KnownTypes));
    }

    [Test]
    public void Process_FullSnapshot_AddsAllTypes()
    {
        var changes = new Dictionary<ItemType, long>
        {
            { ItemType.Rem, 10 },
            { ItemType.Battery, 5 },
            { ItemType.Nano, 1 },
        };

        _processor.Process(new InventoryPacket(changes));

        Assert.That(_model.OrderedTypes, Is.EquivalentTo(changes.Keys));
        Assert.AreEqual(10, _model.GetQuantity(ItemType.Rem));
        Assert.AreEqual(5, _model.GetQuantity(ItemType.Battery));
        Assert.AreEqual(1, _model.GetQuantity(ItemType.Nano));
    }

    [Test]
    public void Process_FullSnapshot_CountEqualsKnownTypeCount_UsesAbsoluteSet()
    {
        // Три известных типа в снимке — это полный набор (≥ KnownTypes.Length),
        // даже если из модели выпал какой-то прежний тип: он исчезает.
        _model.ApplyFullSnapshot(new Dictionary<ItemType, long>
        {
            { ItemType.Rem, 10 },
            { ItemType.Battery, 5 },
        });

        var changes = new Dictionary<ItemType, long>
        {
            { ItemType.Rem, 10 },
            { ItemType.Nano, 7 },
            { ItemType.Battery, 0 },
        };

        _processor.Process(new InventoryPacket(changes));

        Assert.That(_model.OrderedTypes, Is.EquivalentTo(new[] { ItemType.Rem, ItemType.Nano }));
        Assert.AreEqual(0, _model.GetQuantity(ItemType.Battery));
    }

    [Test]
    public void Process_MiniSnapshot_UpdatesOnlyListedTypes()
    {
        _model.ApplyFullSnapshot(new Dictionary<ItemType, long>
        {
            { ItemType.Rem, 10 },
            { ItemType.Battery, 5 },
        });

        var changes = new Dictionary<ItemType, long> { { ItemType.Rem, 25 } };

        _processor.Process(new InventoryPacket(changes));

        Assert.AreEqual(25, _model.GetQuantity(ItemType.Rem));
        Assert.AreEqual(5, _model.GetQuantity(ItemType.Battery), "Mini update must leave unlisted types untouched.");
    }

    [Test]
    public void Process_MiniSnapshot_RemovesItemWhenQuantityZeroOrNegative()
    {
        _model.ApplyFullSnapshot(new Dictionary<ItemType, long>
        {
            { ItemType.Rem, 10 },
            { ItemType.Battery, 5 },
        });

        var changes = new Dictionary<ItemType, long> { { ItemType.Rem, 0 } };

        _processor.Process(new InventoryPacket(changes));

        Assert.AreEqual(0, _model.GetQuantity(ItemType.Rem));
        Assert.AreEqual(5, _model.GetQuantity(ItemType.Battery));
    }

    [Test]
    public void Process_SelectItemPacket_AppliesMetadataToThatType()
    {
        _model.ApplyFullSnapshot(new Dictionary<ItemType, long> { { ItemType.Rem, 1 } });

        var packet = new SelectItemPacket(
            ItemType.Rem,
            "Super Pickaxe",
            "Mines instantly",
            0,
            0,
            0,
            false,
            new BitArray(8));

        _processor.Process(packet);

        var item = _model.GetItem(ItemType.Rem);
        Assert.IsNotNull(item);
        Assert.AreEqual("Super Pickaxe", item!.Name);
        Assert.AreEqual("Mines instantly", item.Description);
    }

    [Test]
    public void Process_LateSelectItemPacket_UpdatesMatchingTypeWithoutChangingOtherItems()
    {
        _model.ApplyFullSnapshot(new Dictionary<ItemType, long>
        {
            { ItemType.Rem, 1 },
            { ItemType.Battery, 1 },
        });
        _model.ApplyItemMetadata(ItemType.Battery, "Scanner", "Scans nearby objects");

        _processor.Process(new SelectItemPacket(
            ItemType.Rem,
            "Super Pickaxe",
            "Mines instantly",
            0,
            0,
            0,
            false,
            new BitArray(8)));

        Assert.AreEqual("Super Pickaxe", _model.GetItem(ItemType.Rem)!.Name);
        Assert.AreEqual("Mines instantly", _model.GetItem(ItemType.Rem)!.Description);
        Assert.AreEqual("Scanner", _model.GetItem(ItemType.Battery)!.Name);
        Assert.IsNull(_model.SelectedItem);
    }

    [Test]
    public void Process_SelectItemPacket_IgnoresUnknownType()
    {
        var packet = new SelectItemPacket(
            ItemType.Nano,
            "x",
            "y",
            0,
            0,
            0,
            false,
            new BitArray(8));

        Assert.DoesNotThrow(() => _processor.Process(packet));
        Assert.IsNull(_model.GetItem(ItemType.Nano));
    }

    [Test]
    public void Process_DeselectItemPacket_ClearsSelection()
    {
        _model.ApplyFullSnapshot(new Dictionary<ItemType, long> { { ItemType.Rem, 1 } });
        _model.Select(ItemType.Rem);
        Assert.AreEqual(ItemType.Rem, _model.SelectedItem);

        _processor.Process(new DeselectItemPacket());

        Assert.IsNull(_model.SelectedItem);
        Assert.IsFalse(_model.HasSelectedItem);
    }

    private sealed class StubItemCatalog(IEnumerable<ItemType> knownTypes) : IItemCatalog
    {
        public IEnumerable<ItemType> AllTypes => knownTypes;

        public string GetName(ItemType type) => type.ToString();

        public string GetDescription(ItemType type) => string.Empty;

        public Texture2D? GetIcon(ItemType type) => null;
    }
}
