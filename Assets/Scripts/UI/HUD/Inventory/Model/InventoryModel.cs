#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Models;
using Fodinae.Networking;
using MinesServer.Networking.Client.Packets.Inventory;
using UnityEngine;
using VContainer;

namespace Fodinae.UI.HUD.Inventory.Model
{
    public class InventoryModel : Fodinae.UI.HUD.Inventory.Interfaces.IInventoryModel, IInventoryState
    {
        public const int HOTBAR_SIZE = 9;
        public const int INVENTORY_SIZE = 6 * 9;
        public const int TOTALSLOTS = HOTBAR_SIZE + INVENTORY_SIZE;

        [Inject]
        private INetworkService _networkService = null!;

        private ItemData?[] _slots = new ItemData?[TOTALSLOTS];

        public event Action<int>? OnSlotChanged;

        private int _selectedSlot = -1;
        public int SelectedSlot => _selectedSlot;

        /// <summary>
        /// True, когда выбран непустой слот — то есть Enter должен применить
        /// предмет, а не открыть чат.
        /// </summary>
        public bool HasSelectedItem =>
            _selectedSlot >= 0 && _selectedSlot < _slots.Length && _slots[_selectedSlot] != null;

        public event Action<int>? OnSlotSelected;

        public ItemData? GetSlot(int index) => (index >= 0 && index < _slots.Length) ? _slots[index] : null;
        public void SetSlot(int index, ItemData? item)
        {
            if (index >= 0 && index < _slots.Length)
            {
                if (AreEquivalent(_slots[index], item))
                {
                    return;
                }

                _slots[index] = item;
                OnSlotChanged?.Invoke(index);
                if (index == _selectedSlot)
                {
                    OnSlotSelected?.Invoke(index);
                }
            }
        }

        public static bool CanStack(ItemData? a, ItemData? b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            return a.Name == b.Name && a.IconColor == b.IconColor;
        }

        public void SwapSlots(int from, int to)
        {
            if (!IsValidSlot(from) || !IsValidSlot(to) || from == to)
            {
                return;
            }

            var temp = _slots[from];
            _slots[from] = _slots[to];
            _slots[to] = temp;
            OnSlotChanged?.Invoke(from);
            OnSlotChanged?.Invoke(to);
        }

        public bool TryStackSlots(int from, int to)
        {
            if (!IsValidSlot(from) || !IsValidSlot(to) || from == to)
            {
                return false;
            }

            var fromItem = _slots[from];
            var toItem = _slots[to];

            if (fromItem == null)
            {
                return false;
            }

            if (toItem == null)
            {
                _slots[to] = fromItem;
                _slots[from] = null;
                OnSlotChanged?.Invoke(from);
                OnSlotChanged?.Invoke(to);
                return true;
            }

            if (!CanStack(fromItem, toItem))
            {
                return false;
            }

            toItem.Quantity += fromItem.Quantity;
            _slots[from] = null;
            OnSlotChanged?.Invoke(from);
            OnSlotChanged?.Invoke(to);
            return true;
        }

        public void SelectSlot(int index)
        {
            if (!IsValidSlot(index))
            {
                return;
            }

            if (_selectedSlot == index)
            {
                return;
            }

            _selectedSlot = index;
            OnSlotSelected?.Invoke(index);

            if (_networkService == null)
            {
                Debug.LogWarning("[InventoryModel] NetworkService is not injected, cannot send packet");
                return;
            }

            ItemData? item = _slots[index];
            if (item != null)
            {
                _networkService.Send(new SelectItemPacket(item.ItemType));
            }
            else
            {
                _networkService.Send(new DeselectItemPacket());
            }
        }

        public void DeselectSlot()
        {
            _selectedSlot = -1;
            OnSlotSelected?.Invoke(-1);

            if (_networkService == null)
            {
                return;
            }

            _networkService.Send(new DeselectItemPacket());
        }

        public void ClearSelection()
        {
            if (_selectedSlot < 0)
            {
                return;
            }

            _selectedSlot = -1;
            OnSlotSelected?.Invoke(-1);
        }

        public void UseSelectedItem()
        {
            if (_selectedSlot < 0 || _slots[_selectedSlot] == null)
            {
                return;
            }

            _networkService.Send(new UseItemPacket());
        }

        private static bool AreEquivalent(ItemData? left, ItemData? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
                left.Quantity == right.Quantity &&
                string.Equals(left.Description, right.Description, StringComparison.Ordinal) &&
                left.ItemType == right.ItemType;
        }

        private static bool IsValidSlot(int index)
        {
            return index >= 0 && index < TOTALSLOTS;
        }
    }
}
