#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.Core.Models;
using Kern.Networking;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Inventory;
using UnityEngine;
using VContainer;

namespace Kern.Game.Inventory;

/// <summary>
/// Ordered-type inventory. Holds every item type that the player owns (with
/// long quantities, no slot abstraction), keeps a display order whose head is
/// the currently selected item, and forwards client-to-server selection
/// commands through <see cref="INetworkService"/>.
/// </summary>
public class InventoryModel : IInventoryModel, IInventoryState, IBatchAwareProcessor
{
    [Inject]
    private INetworkService _networkService = null!;

    private readonly List<ItemType> _order = new();
    private readonly Dictionary<ItemType, ItemData> _items = new();

    private ItemType? _selectedItem;
    private int _batchDepth;
    private bool _hasPendingItemsChanged;
    private bool _hasPendingSelectedChanged;

    public event Action? OnItemsChanged;
    public event Action<ItemType?>? OnSelectedChanged;

    public void BeginBatch()
    {
        _batchDepth++;
    }

    public void EndBatch()
    {
        if (_batchDepth <= 0)
        {
            return;
        }

        _batchDepth--;
        if (_batchDepth == 0)
        {
            if (_hasPendingItemsChanged)
            {
                _hasPendingItemsChanged = false;
                OnItemsChanged?.Invoke();
            }

            if (_hasPendingSelectedChanged)
            {
                _hasPendingSelectedChanged = false;
                OnSelectedChanged?.Invoke(_selectedItem);
            }
        }
    }

    private void NotifyItemsChanged()
    {
        if (_batchDepth > 0)
        {
            _hasPendingItemsChanged = true;
            return;
        }

        OnItemsChanged?.Invoke();
    }

    private void NotifySelectedChanged()
    {
        if (_batchDepth > 0)
        {
            _hasPendingSelectedChanged = true;
            return;
        }

        OnSelectedChanged?.Invoke(_selectedItem);
    }

    public IReadOnlyList<ItemType> OrderedTypes => _order;

    public ItemType? SelectedItem => _selectedItem;

    public bool HasSelectedItem =>
        _selectedItem is { } selected &&
        _items.TryGetValue(selected, out ItemData? item) &&
        item.Quantity > 0;

    public ItemData? GetItem(ItemType type) =>
        _items.TryGetValue(type, out ItemData? item) ? item : null;

    public long GetQuantity(ItemType type) =>
        _items.TryGetValue(type, out ItemData? item) ? item.Quantity : 0;

    public void ApplyFullSnapshot(IDictionary<ItemType, long> snapshot)
    {
        HashSet<ItemType> nextTypes = new();
        SnapshotKeySet(snapshot, nextTypes);

        var nextOrder = new List<ItemType>(_order.Count);
        foreach (ItemType type in _order)
        {
            if (nextTypes.Contains(type))
            {
                nextOrder.Add(type);
            }
        }

        foreach (ItemType type in snapshot.Keys)
        {
            if (!nextTypes.Contains(type) || _order.Contains(type))
            {
                continue;
            }

            nextOrder.Add(type);
        }

        _order.Clear();
        _order.AddRange(nextOrder);

        _items.Clear();
        foreach (ItemType type in _order)
        {
            _items[type] = new ItemData(type.ToString(), Color.gray, snapshot[type])
            {
                ItemType = type,
            };
        }

        ClearSelectionIfVanished();
        NotifyItemsChanged();
    }

    public void MergeChanges(IDictionary<ItemType, long> changes)
    {
        bool shouldInvalidate = false;

        foreach ((ItemType type, long quantity) in changes)
        {
            if (quantity <= 0)
            {
                if (!_items.Remove(type))
                {
                    continue;
                }

                _order.Remove(type);
                shouldInvalidate = true;
                continue;
            }

            if (_items.TryGetValue(type, out ItemData? existing))
            {
                if (existing.Quantity != quantity)
                {
                    existing.Quantity = quantity;
                    shouldInvalidate = true;
                }

                continue;
            }

            _items[type] = new ItemData(type.ToString(), Color.gray, quantity)
            {
                ItemType = type,
            };
            _order.Add(type);
            shouldInvalidate = true;
        }

        if (ClearSelectionIfVanished())
        {
            shouldInvalidate = true;
        }

        if (shouldInvalidate)
        {
            NotifyItemsChanged();
        }
    }

    public void ApplyItemMetadata(ItemType item, string name, string description)
    {
        if (!_items.TryGetValue(item, out ItemData? data))
        {
            return;
        }

        data.Name = name;
        data.Description = description;
        NotifyItemsChanged();
    }

    public void Select(ItemType type)
    {
        if (!_items.TryGetValue(type, out ItemData? item) || item.Quantity <= 0)
        {
            return;
        }

        if (_selectedItem != type)
        {
            _selectedItem = type;
            MoveToFront(type);
            NotifySelectedChanged();
            NotifyItemsChanged();
        }

        if (_networkService == null)
        {
            Debug.LogWarning("[InventoryModel] NetworkService is not injected, cannot send packet");
            return;
        }

        _networkService.Send(new SelectItemPacket(type));
    }

    public void Deselect()
    {
        if (_selectedItem == null)
        {
            return;
        }

        _selectedItem = null;
        NotifySelectedChanged();

        if (_networkService == null)
        {
            Debug.LogWarning("[InventoryModel] NetworkService is not injected, cannot send packet");
            return;
        }

        _networkService.Send(new DeselectItemPacket());
    }

    public void ClearSelection()
    {
        if (_selectedItem == null)
        {
            return;
        }

        _selectedItem = null;
        NotifySelectedChanged();
    }

    public void UseSelectedItem()
    {
        if (!HasSelectedItem)
        {
            return;
        }

        if (_networkService == null)
        {
            Debug.LogWarning("[InventoryModel] NetworkService is not injected, cannot send packet");
            return;
        }

        // Server decides which held type is used from its own selection state;
        // the client just asks to use whatever is currently selected.
        _networkService.Send(new UseItemPacket());
    }

    private void MoveToFront(ItemType type)
    {
        int index = _order.IndexOf(type);
        if (index <= 0)
        {
            return;
        }

        _order.RemoveAt(index);
        _order.Insert(0, type);
    }

    private bool ClearSelectionIfVanished()
    {
        if (_selectedItem is not { } selected)
        {
            return false;
        }

        if (_items.TryGetValue(selected, out ItemData? item) && item.Quantity > 0)
        {
            return false;
        }

        _selectedItem = null;
        NotifySelectedChanged();
        return true;
    }

    private static void SnapshotKeySet(IDictionary<ItemType, long> snapshot, HashSet<ItemType> into)
    {
        foreach ((ItemType type, long quantity) in snapshot)
        {
            if (quantity > 0)
            {
                into.Add(type);
            }
        }
    }
}
