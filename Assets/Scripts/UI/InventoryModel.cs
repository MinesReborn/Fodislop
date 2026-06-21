using System;
using System.Collections.Generic;

namespace Fodinae.Scripts.UI
{
    public class InventoryModel
    {
        public const int HOTBAR_SIZE = 9;
        public const int INVENTORY_SIZE = 6 * 9;
        public const int TOTAL_SLOTS = HOTBAR_SIZE + INVENTORY_SIZE;

        private static InventoryModel _instance;
        public static InventoryModel Instance
        {
            get
            {
                if (_instance == null) _instance = new InventoryModel();
                return _instance;
            }
        }

        private ItemData[] _slots = new ItemData[TOTAL_SLOTS];

        public event Action<int> OnSlotChanged;

        private int _selectedSlot = -1;
        public int SelectedSlot => _selectedSlot;
        public event Action<int> OnSlotSelected;

        public ItemData GetSlot(int index) => _slots[index];
        public void SetSlot(int index, ItemData item)
        {
            _slots[index] = item;
            OnSlotChanged?.Invoke(index);
        }

        public bool CanStack(ItemData a, ItemData b)
        {
            if (a == null || b == null) return false;
            return a.Name == b.Name && a.IconColor == b.IconColor;
        }

        public void SwapSlots(int from, int to)
        {
            var temp = _slots[from];
            _slots[from] = _slots[to];
            _slots[to] = temp;
            OnSlotChanged?.Invoke(from);
            OnSlotChanged?.Invoke(to);
        }

        public bool TryStackSlots(int from, int to)
        {
            var fromItem = _slots[from];
            var toItem = _slots[to];

            if (fromItem == null) return false;

            if (toItem == null)
            {
                _slots[to] = fromItem;
                _slots[from] = null;
                OnSlotChanged?.Invoke(from);
                OnSlotChanged?.Invoke(to);
                return true;
            }

            if (!CanStack(fromItem, toItem))
                return false;

            toItem.Quantity += fromItem.Quantity;
            _slots[from] = null;
            OnSlotChanged?.Invoke(from);
            OnSlotChanged?.Invoke(to);
            return true;
        }

        public void SelectSlot(int index)
        {
            if (_selectedSlot == index) return;
            _selectedSlot = index;
            OnSlotSelected?.Invoke(index);
        }

        public void DeselectSlot()
        {
            _selectedSlot = -1;
            OnSlotSelected?.Invoke(-1);
        }
    }
}
