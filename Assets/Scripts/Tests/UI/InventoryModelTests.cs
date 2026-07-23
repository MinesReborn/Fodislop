using NUnit.Framework;
using Fodinae.Scripts.UI.HUD.Inventory.Model;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Tests.UI
{
    [TestFixture]
    public class InventoryModelTests
    {
        private InventoryModel _model;

        [SetUp]
        public void SetUp()
        {
            _model = new InventoryModel();
        }

        [Test]
        public void InitialState_TotalSlotsMatchConstant_AllSlotsNull()
        {
            Assert.AreEqual(63, InventoryModel.TOTALSLOTS);
            for (int i = 0; i < InventoryModel.TOTALSLOTS; i++)
            {
                Assert.IsNull(_model.GetSlot(i), $"Slot {i} should initially be null.");
            }
        }

        [Test]
        public void SetSlot_FiresOnSlotChangedEvent_UpdatesSlotData()
        {
            int changedIndex = -1;
            _model.OnSlotChanged += (idx) => changedIndex = idx;

            var item = new ItemData("Iron Ore", Color.gray, 5) { ItemType = (ItemType)1 };
            _model.SetSlot(3, item);

            Assert.AreEqual(3, changedIndex, "OnSlotChanged should be invoked with slot index 3.");
            Assert.AreEqual(item, _model.GetSlot(3));
        }

        [Test]
        public void SwapSlots_ExchangesItems_FiresSlotChangedEvents()
        {
            var itemA = new ItemData("Iron", Color.gray, 10) { ItemType = (ItemType)1 };
            var itemB = new ItemData("Gold", Color.yellow, 5) { ItemType = (ItemType)2 };

            _model.SetSlot(0, itemA);
            _model.SetSlot(1, itemB);

            _model.SwapSlots(0, 1);

            Assert.AreEqual(itemB, _model.GetSlot(0), "Slot 0 should now contain Gold.");
            Assert.AreEqual(itemA, _model.GetSlot(1), "Slot 1 should now contain Iron.");
        }

        [Test]
        public void TryStackSlots_SameItemType_CombinesQuantities()
        {
            var itemFrom = new ItemData("Coal", Color.black, 15) { ItemType = (ItemType)1 };
            var itemTo = new ItemData("Coal", Color.black, 20) { ItemType = (ItemType)1 };

            _model.SetSlot(0, itemFrom);
            _model.SetSlot(1, itemTo);

            bool stacked = _model.TryStackSlots(0, 1);

            Assert.IsTrue(stacked, "TryStackSlots should return true for identical stackable items.");
            Assert.IsNull(_model.GetSlot(0), "From slot should be emptied after stacking.");
            Assert.AreEqual(35, _model.GetSlot(1).Quantity, "Target slot quantity should be the sum (15 + 20 = 35).");
        }

        [Test]
        public void TryStackSlots_DifferentItems_ReturnsFalseAndPreservesSlots()
        {
            var itemFrom = new ItemData("Coal", Color.black, 15) { ItemType = (ItemType)1 };
            var itemTo = new ItemData("Diamond", Color.cyan, 1) { ItemType = (ItemType)2 };

            _model.SetSlot(0, itemFrom);
            _model.SetSlot(1, itemTo);

            bool stacked = _model.TryStackSlots(0, 1);

            Assert.IsFalse(stacked, "TryStackSlots should return false for different items.");
            Assert.AreEqual(15, _model.GetSlot(0).Quantity);
            Assert.AreEqual(1, _model.GetSlot(1).Quantity);
        }

        [Test]
        public void SelectSlotAndClearSelection_UpdatesSelectedSlotIndex()
        {
            int selectedIdx = -2;
            _model.OnSlotSelected += (idx) => selectedIdx = idx;

            _model.SelectSlot(4);
            Assert.AreEqual(4, _model.SelectedSlot);
            Assert.AreEqual(4, selectedIdx);

            _model.ClearSelection();
            Assert.AreEqual(-1, _model.SelectedSlot);
            Assert.AreEqual(-1, selectedIdx);
        }
    }
}
