#nullable enable

using System.Collections.Generic;
using Kern.Core.Models;
using MinesServer.Data;

namespace Kern.Core.Interfaces;

/// <summary>
/// Write-side inventory contract consumed by network processors: a full
/// authoritative snapshot replaces the entire held set (preserving the local
/// display order), a mini snapshot only merges the listed types, and metadata
/// packets update the selected item's name/description shell.
/// </summary>
public interface IInventoryState
{
    /// <summary>Held types in display order; the selected one first.</summary>
    IReadOnlyList<ItemType> OrderedTypes { get; }

    /// <summary>The currently selected type, or null.</summary>
    ItemType? SelectedItem { get; }

    ItemData? GetItem(ItemType type);

    long GetQuantity(ItemType type);

    /// <summary>
    /// Replaces the held set with an authoritative snapshot: types still held
    /// keep their local positions, types absent from the snapshot (or present
    /// with quantity &lt;= 0) are removed, brand-new types are appended. The
    /// selection is cleared if the selected type vanished.
    /// </summary>
    void ApplyFullSnapshot(IDictionary<ItemType, long> snapshot);

    /// <summary>
    /// Merges a mini update: every listed type is updated (quantity &lt;= 0
    /// removes it), everything else is left untouched. The selection is
    /// cleared if the selected type vanished.
    /// </summary>
    void MergeChanges(IDictionary<ItemType, long> changes);

    /// <summary>Applies the server-provided name/description to a held item.</summary>
    void ApplyItemMetadata(ItemType item, string name, string description);

    /// <summary>Clears the selection locally (no client packet is sent).</summary>
    void ClearSelection();
}
