#nullable enable

using Kern.Core.Models;

namespace Kern.Core.Interfaces;

public interface IInventoryState
{
    int SelectedSlot { get; }
    ItemData? GetSlot(int index);
    void SetSlot(int index, ItemData? item);
    void ClearSelection();
}
