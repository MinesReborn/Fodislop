#nullable enable

using Kern.Core.Models;
using Kern.Game.Inventory;
using MinesServer.Data;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.UI;

[TestFixture]
public class InventoryModelFuzzTests
{
    [Test]
    public void RandomSlots_StayValid()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 200; i++)
        {
            var model = new InventoryModel();
            for (int j = 0; j < 50; j++)
            {
                int idx = random.Next(0, InventoryModel.TOTALSLOTS);
                ItemData? item = random.Next(3) == 0 ? null : MakeItem(random, random.Next(1, 50));
                model.SetSlot(idx, item);
            }
            long total = 0;
            for (int k = 0; k < InventoryModel.TOTALSLOTS; k++)
                total += model.GetSlot(k)?.Quantity ?? 0;
            Assert.That(total, Is.GreaterThanOrEqualTo(0));
        }
    }

    [Test]
    public void Swap_PreservesTotalQuantity()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 200; i++)
        {
            var model = new InventoryModel();
            for (int j = 0; j < 20; j++)
            {
                int idx = random.Next(0, InventoryModel.TOTALSLOTS);
                model.SetSlot(idx, MakeItem(random, random.Next(1, 50)));
            }
            int from = random.Next(0, InventoryModel.TOTALSLOTS);
            int to = random.Next(0, InventoryModel.TOTALSLOTS);
            long before = Total(model);
            model.SwapSlots(from, to);
            Assert.That(Total(model), Is.EqualTo(before), $"i={i}");
        }
    }

    [Test]
    public void Stack_PreservesTotalQuantity()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 200; i++)
        {
            var model = new InventoryModel();
            int from = random.Next(0, InventoryModel.TOTALSLOTS);
            int to = random.Next(0, InventoryModel.TOTALSLOTS);
            if (from == to) continue;
            string name = "item" + (i % 10);
            var c = new Color((float)random.NextDouble(), (float)random.NextDouble(), 0, 1);
            model.SetSlot(from, new ItemData(name, c, random.Next(1, 50)));
            long before = Total(model);
            model.TryStackSlots(from, to);
            Assert.That(Total(model), Is.EqualTo(before), $"i={i}");
        }
    }

    [Test]
    public void EquivalentItem_NoEvent()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 50; i++)
        {
            var model = new InventoryModel();
            var item = MakeItem(random, random.Next(1, 20));
            int idx = random.Next(0, InventoryModel.TOTALSLOTS);
            model.SetSlot(idx, item);
            int count = 0;
            model.OnSlotChanged += _ => count++;
            model.SetSlot(idx, item);
            Assert.That(count, Is.EqualTo(0), $"i={i}");
        }
    }

    [Test]
    public void Select_OutOfRange_StaysNegative()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 50; i++)
        {
            var model = new InventoryModel();
            model.SelectSlot(random.Next(-100, -1));
            Assert.That(model.SelectedSlot, Is.EqualTo(-1));
        }
    }

    [Test]
    public void CanStack_Null_False()
    {
        var item = new ItemData("x", Color.red, 1);
        Assert.IsFalse(InventoryModel.CanStack(null, item));
        Assert.IsFalse(InventoryModel.CanStack(item, null));
        Assert.IsFalse(InventoryModel.CanStack(null, null));
    }

    [Test]
    public void CanStack_SameNameAndColor_True()
    {
        var a = new ItemData("name", Color.red, 1);
        var b = new ItemData("name", Color.red, 99);
        Assert.IsTrue(InventoryModel.CanStack(a, b));
    }

    private static ItemData MakeItem(System.Random r, int q)
    {
        return new ItemData("item" + r.Next(0, 5),
            new Color((float)r.NextDouble(), (float)r.NextDouble(), 0, 1), q)
        { ItemType = (ItemType)0 };
    }

    private static long Total(InventoryModel m)
    {
        long t = 0;
        for (int i = 0; i < InventoryModel.TOTALSLOTS; i++)
            t += m.GetSlot(i)?.Quantity ?? 0;
        return t;
    }
}
