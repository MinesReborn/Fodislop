#nullable enable

using System.Collections.Generic;
using MinesServer.Data;
using NUnit.Framework;

namespace Kern.Tests.UI;

[TestFixture]
public class InventoryModelFuzzTests
{
    private static readonly ItemType[] Types =
    [
        ItemType.Rem,
        ItemType.Battery,
        ItemType.Nano,
        ItemType.Poly,
        ItemType.UpgradeBooster,
        ItemType.GeoCyan,
    ];

    [Test]
    public void RandomSnapshots_KeepOrderConsistentWithSelection()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 200; i++)
        {
            var model = new Kern.Game.Inventory.InventoryModel();
            model.ApplyFullSnapshot(MakeSnapshot(random));

            // Каждый предмет в OrderedTypes обязан быть держимым.
            foreach (ItemType type in model.OrderedTypes)
            {
                Assert.That(model.GetQuantity(type), Is.GreaterThan(0), $"i={i}");
            }
        }
    }

    [Test]
    public void Select_PreservesOrderAndSet()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 200; i++)
        {
            var model = new Kern.Game.Inventory.InventoryModel();
            Dictionary<ItemType, long> snapshot = MakeSnapshot(random);
            model.ApplyFullSnapshot(snapshot);

            var pick = model.OrderedTypes[RandomIndex(random, model.OrderedTypes.Count)];
            model.Select(pick);

            Assert.That(model.SelectedItem, Is.EqualTo(pick), $"i={i}");
            Assert.That(model.OrderedTypes[0], Is.EqualTo(pick), $"i={i}");
            Assert.That(model.OrderedTypes, Is.EquivalentTo(snapshot.Keys), $"i={i}");

            long total = 0;
            foreach (ItemType type in model.OrderedTypes)
            {
                total += model.GetQuantity(type);
            }

            long expected = 0;
            foreach (long quantity in snapshot.Values)
            {
                expected += quantity;
            }

            Assert.That(total, Is.EqualTo(expected), $"i={i}");
        }
    }

    [Test]
    public void MiniMerges_DoNotGrowSetBeyondListedTypes()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 200; i++)
        {
            var model = new Kern.Game.Inventory.InventoryModel();
            model.ApplyFullSnapshot(new Dictionary<ItemType, long>() { });
            model.MergeChanges(MakeChanges(random));
            Assert.That(model.OrderedTypes.Count, Is.LessThanOrEqualTo(Types.Length), $"i={i}");
        }
    }

    private static Dictionary<ItemType, long> MakeSnapshot(System.Random random)
    {
        var snapshot = new Dictionary<ItemType, long>();
        foreach (ItemType type in Types)
        {
            if (random.Next(3) != 0)
            {
                snapshot[type] = random.Next(1, 50);
            }
        }

        return snapshot;
    }

    private static Dictionary<ItemType, long> MakeChanges(System.Random random)
    {
        var changes = new Dictionary<ItemType, long>();
        for (int j = 0; j < 3; j++)
        {
            ItemType type = Types[RandomIndex(random, Types.Length)];
            long quantity = random.Next(4) == 0 ? 0 : random.Next(1, 50);
            changes[type] = quantity;
        }

        return changes;
    }

    private static int RandomIndex(System.Random random, int count) =>
        count == 0 ? 0 : random.Next(0, count);
}
