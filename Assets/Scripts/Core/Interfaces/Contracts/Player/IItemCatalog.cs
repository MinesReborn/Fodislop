#nullable enable

using System.Collections.Generic;
using MinesServer.Data;
using UnityEngine;

namespace Kern.Core.Interfaces;

public interface IItemCatalog
{
    IEnumerable<ItemType> AllTypes { get; }

    string GetName(ItemType type);

    string GetDescription(ItemType type);

    Texture2D? GetIcon(ItemType type);
}
