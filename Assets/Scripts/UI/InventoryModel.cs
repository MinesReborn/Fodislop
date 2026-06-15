using System;
using System.Collections.Generic;

namespace Fodinae.Assets.Scripts.UI
{
    public class InventoryModel
    {
        public const int HOTBAR_SIZE = 9;
        public const int INVENTORY_SIZE = 3 * 9;
        public const int TOTAL_SLOTS = HOTBAR_SIZE + INVENTORY_SIZE;

        private ItemData[] _slots = new ItemData[TOTAL_SLOTS];

        public event Action<int> OnSlotChanged;

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

        // Перемещение/обмен предметов между слотами
        public void SwapSlots(int from, int to)
        {
            var temp = _slots[from];
            _slots[from] = _slots[to];
            _slots[to] = temp;
            OnSlotChanged?.Invoke(from);
            OnSlotChanged?.Invoke(to);
        }

        // Стаккинг: переложить предмет из from в to
        // Возвращает true если удалось полностью переложить
        public bool TryStackSlots(int from, int to)
        {
            var fromItem = _slots[from];
            var toItem = _slots[to];

            if (fromItem == null) return false;

            if (toItem == null)
            {
                // Просто переместить
                _slots[to] = fromItem;
                _slots[from] = null;
                OnSlotChanged?.Invoke(from);
                OnSlotChanged?.Invoke(to);
                return true;
            }

            if (!CanStack(fromItem, toItem))
                return false;

            // Объединить
            toItem.Quantity += fromItem.Quantity;
            _slots[from] = null;
            OnSlotChanged?.Invoke(from);
            OnSlotChanged?.Invoke(to);
            return true;
        }
    }
}
