#nullable enable

using System;
using System.Collections.Generic;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Inventory;

namespace Kern.Networking.Dummy;

// Правила работы с предметами для dummy-сервера: какие предметы являются
// строительными паками, в какой пак превращается предмет и списание из
// инвентаря. Названия и описания живут в ItemInfoCatalog.
public static class DummyItemRules
{
    public static bool IsBuildingPack(ItemType type) => type switch
    {
        ItemType.Teleport or ItemType.Resp or ItemType.Up or ItemType.Market or
        ItemType.Clans or ItemType.Craft or ItemType.BombShop or ItemType.Gun or
        ItemType.Storage or ItemType.ScienceCentre => true,
        _ => false,
    };

    public static BuildingType ItemTypeToPackType(ItemType type) => type switch
    {
        ItemType.Teleport => BuildingType.Teleport,
        ItemType.Resp => BuildingType.Resp,
        ItemType.Up => BuildingType.Up,
        ItemType.Market => BuildingType.Market,
        ItemType.Clans => BuildingType.Clans,
        ItemType.Craft => BuildingType.Craft,
        ItemType.BombShop => BuildingType.BombShop,
        ItemType.Gun => BuildingType.Gun,
        ItemType.Storage => BuildingType.Storage,
        ItemType.ScienceCentre => BuildingType.Science,
        _ => BuildingType.None,
    };

    public static void ConsumeItem(Dictionary<ItemType, long> inventory, ItemType type, long count)
    {
        if (!inventory.TryGetValue(type, out long current) || current <= 0)
        {
            return;
        }

        long remaining = Math.Max(0, current - count);
        inventory[type] = remaining;
    }
}
