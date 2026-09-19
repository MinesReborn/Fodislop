#nullable enable

using System;
using Kern.Core.Models;

namespace Kern.Game.Inventory;
public interface IInventoryModel
{
    event Action<int>? OnSlotChanged;
    event Action<int>? OnSlotSelected;

    int SelectedSlot { get; }

    ItemData? GetSlot(int index);
    void SetSlot(int index, ItemData? item);
    void SwapSlots(int from, int to);
    bool TryStackSlots(int from, int to);
    void SelectSlot(int index);
    void DeselectSlot();
    void ClearSelection();
    void UseSelectedItem();
}
