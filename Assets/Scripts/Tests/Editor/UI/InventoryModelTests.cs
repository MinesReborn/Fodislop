#nullable enable

using System.Collections.Generic;
using Kern.Game.Inventory;
using MinesServer.Data;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.UI;

[TestFixture]
public class InventoryModelTests
{
    private InventoryModel _model = null!;

    [SetUp]
    public void SetUp()
    {
        _model = new InventoryModel();
    }

    [Test]
    public void InitialState_IsEmptyAndNothingSelected()
    {
        Assert.IsEmpty(_model.OrderedTypes);
        Assert.IsNull(_model.SelectedItem);
        Assert.IsFalse(_model.HasSelectedItem);
    }

    [Test]
    public void ApplyFullSnapshot_AddsAllPositiveTypes()
    {
        _model.ApplyFullSnapshot(new Dictionary<ItemType, long>
        {
            { ItemType.Rem, 5 },
            { ItemType.Battery, 2 },
        });

        Assert.That(_model.OrderedTypes, Is.EquivalentTo(new[] { ItemType.Rem, ItemType.Battery }));
        Assert.AreEqual(5, _model.GetQuantity(ItemType.Rem));
        Assert.AreEqual(2, _model.GetQuantity(ItemType.Battery));
    }

    [Test]
    public void ApplyFullSnapshot_UpdatesQuantitiesAndDropsMissingTypes()
    {
        var existing = new Dictionary<ItemType, long>
        {
            { ItemType.Rem, 5 },
            { ItemType.Nano, 3 },
        };
        _model.ApplyFullSnapshot(existing);

        _model.ApplyFullSnapshot(new Dictionary<ItemType, long>
        {
            { ItemType.Rem, 7 },
            { ItemType.Battery, 1 },
        });

        Assert.That(_model.OrderedTypes, Is.EquivalentTo(new[] { ItemType.Rem, ItemType.Battery }));
        Assert.AreEqual(7, _model.GetQuantity(ItemType.Rem));
        Assert.AreEqual(0, _model.GetQuantity(ItemType.Nano));
        Assert.AreEqual(1, _model.GetQuantity(ItemType.Battery));
    }

    [Test]
    public void ApplyFullSnapshot_PreservesLocalOrderForSurvivingTypes()
    {
        _model.ApplyFullSnapshot(new Dictionary<ItemType, long>
        {
            { ItemType.Rem, 5 },
            { ItemType.Battery, 2 },
        });
        _model.Select(ItemType.Battery);

        _model.ApplyFullSnapshot(new Dictionary<ItemType, long>
        {
            { ItemType.Rem, 5 },
            { ItemType.Battery, 2 },
        });

        // После Select(Battery) выбранный предмет был переставлен в голову;
        // полный снимок сохраняет локальный порядок.
        Assert.AreEqual(ItemType.Battery, _model.OrderedTypes[0]);
        Assert.AreEqual(ItemType.Rem, _model.OrderedTypes[1]);
    }

    [Test]
    public void ApplyFullSnapshot_ZeroQuantityRemovesType()
    {
        var existing = new Dictionary<ItemType, long> { { ItemType.Rem, 5 } };
        _model.ApplyFullSnapshot(existing);

        _model.ApplyFullSnapshot(new Dictionary<ItemType, long> { { ItemType.Rem, 0 } });

        Assert.IsEmpty(_model.OrderedTypes);
    }

    [Test]
    public void MergeChanges_UpdatesOnlyListedTypes()
    {
        _model.ApplyFullSnapshot(new Dictionary<ItemType, long>
        {
            { ItemType.Rem, 5 },
            { ItemType.Nano, 3 },
        });

        _model.MergeChanges(new Dictionary<ItemType, long> { { ItemType.Rem, 6 } });

        Assert.AreEqual(6, _model.GetQuantity(ItemType.Rem));
        Assert.AreEqual(3, _model.GetQuantity(ItemType.Nano), "Unlisted type must be untouched by a mini update.");
    }

    [Test]
    public void MergeChanges_AddsNewTypeAtEnd()
    {
        _model.ApplyFullSnapshot(new Dictionary<ItemType, long> { { ItemType.Rem, 5 } });

        _model.MergeChanges(new Dictionary<ItemType, long> { { ItemType.Battery, 1 } });

        Assert.That(_model.OrderedTypes, Is.EqualTo(new[] { ItemType.Rem, ItemType.Battery }));
    }

    [Test]
    public void MergeChanges_ZeroQuantityRemovesType()
    {
        _model.ApplyFullSnapshot(new Dictionary<ItemType, long> { { ItemType.Rem, 5 } });

        _model.MergeChanges(new Dictionary<ItemType, long> { { ItemType.Rem, 0 } });

        Assert.IsEmpty(_model.OrderedTypes);
    }

    [Test]
    public void Select_MovesTypeToFrontAndRaisesSelected()
    {
        _model.ApplyFullSnapshot(new Dictionary<ItemType, long>
        {
            { ItemType.Rem, 5 },
            { ItemType.Battery, 2 },
        });

        ItemType? selected = null;
        int orderChangeEvents = 0;
        _model.OnSelectedChanged += type => selected = type;
        _model.OnItemsChanged += () => orderChangeEvents++;

        _model.Select(ItemType.Battery);

        Assert.AreEqual(ItemType.Battery, _model.SelectedItem);
        Assert.AreEqual(ItemType.Battery, selected);
        Assert.AreEqual(ItemType.Battery, _model.OrderedTypes[0]);
        Assert.AreNotEqual(0, orderChangeEvents);
        Assert.IsTrue(_model.HasSelectedItem);
    }

    [Test]
    public void Select_UnknownType_IsIgnored()
    {
        var existing = new Dictionary<ItemType, long> { { ItemType.Rem, 5 } };
        _model.ApplyFullSnapshot(existing);

        _model.Select(ItemType.Battery);

        Assert.IsNull(_model.SelectedItem);
        Assert.IsFalse(_model.HasSelectedItem);
    }

    [Test]
    public void Deselect_ClearsSelection()
    {
        _model.ApplyFullSnapshot(new Dictionary<ItemType, long> { { ItemType.Rem, 5 } });
        _model.Select(ItemType.Rem);

        ItemType? selected = ItemType.Rem;
        _model.OnSelectedChanged += type => selected = type;

        _model.Deselect();

        Assert.IsNull(_model.SelectedItem);
        Assert.IsNull(selected);
        Assert.IsFalse(_model.HasSelectedItem);
    }

    [Test]
    public void ClearSelection_ClearsWithoutRaisedEventsWhenAlreadyEmpty()
    {
        int events = 0;
        _model.OnSelectedChanged += _ => events++;
        _model.ClearSelection();
        Assert.AreEqual(0, events);
    }

    [Test]
    public void ApplyItemMetadata_UpdatesItemShell()
    {
        _model.ApplyFullSnapshot(new Dictionary<ItemType, long> { { ItemType.Rem, 5 } });
        _model.Select(ItemType.Rem);

        _model.ApplyItemMetadata(ItemType.Rem, "Ремонтный бот", "Восстанавливает здоровье");

        var item = _model.GetItem(ItemType.Rem);
        Assert.IsNotNull(item);
        Assert.AreEqual("Ремонтный бот", item!.Name);
        Assert.AreEqual("Восстанавливает здоровье", item.Description);
    }

    [Test]
    public void ApplyItemMetadata_UnknownType_IsIgnored()
    {
        Assert.DoesNotThrow(() => _model.ApplyItemMetadata(ItemType.Rem, "x", "y"));
    }

    [Test]
    public void FullSnapshotVanishingSelection_ClearsSelection()
    {
        _model.ApplyFullSnapshot(new Dictionary<ItemType, long>
        {
            { ItemType.Rem, 5 },
            { ItemType.Battery, 2 },
        });
        _model.Select(ItemType.Battery);

        _model.ApplyFullSnapshot(new Dictionary<ItemType, long> { { ItemType.Rem, 5 } });

        Assert.IsNull(_model.SelectedItem);
        Assert.IsFalse(_model.HasSelectedItem);
    }
}
