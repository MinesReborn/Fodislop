#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using Kern.Networking.Buildings;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Inventory;
using MinesServer.Networking.Server.Packets.World;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyInventoryResponder(
    Action<ServerPacket> sendPacket,
    Action<string, int, System.Drawing.Color, string> activateBuff,
    List<(ushort X, ushort Y)> teleportPositions,
    Action<int> setHealth,
    Func<ushort, ushort, CellType> getCell,
    Action<ushort, ushort, CellType> setCell)
{
    private ItemType? _selectedItemType;

    public Dictionary<ItemType, long> Items { get; } = new();

    public void ReplaceItems(IEnumerable<KeyValuePair<ItemType, long>> items)
    {
        Items.Clear();
        foreach (var (key, value) in items)
        {
            Items[key] = value;
        }
    }

    public void Select(ItemType item)
    {
        _selectedItemType = item;
        var (name, description) = DummyItemInfo.GetItemInfo(item);
        sendPacket(new ServerPacket(
            new SelectItemPacket(
                item,
                name,
                description,
                1,
                1,
                3,
                false,
                new BitArray(0))));
    }

    public void Deselect()
    {
        _selectedItemType = null;
        sendPacket(new ServerPacket(default(DeselectItemPacket)));
    }

    public void Use(ushort playerX, ushort playerY, Direction direction)
    {
        if (_selectedItemType is not { } selectedType)
        {
            return;
        }

        if (DummyItemInfo.IsBuildingPack(selectedType))
        {
            UseBuildingPack(selectedType, playerX, playerY, direction);
            return;
        }

        switch (selectedType)
        {
            case ItemType.Rem:
                setHealth(500);
                sendPacket(new ServerPacket(new HealthPacket(500, 500)));
                break;
            case ItemType.UpgradeBooster:
                activateBuff("xp3", 86400, System.Drawing.Color.FromArgb(0, 200, 0), "Прокачка x3");
                break;
            case ItemType.FreeUp:
                activateBuff("freeup", 43200, System.Drawing.Color.Cyan, "Freeup");
                break;
            case ItemType.MineBooster:
                activateBuff("x4", 43200, System.Drawing.Color.FromArgb(255, 165, 0), "Добыча x4");
                break;
            case ItemType.Battery:
                activateBuff("battery", 3600, System.Drawing.Color.FromArgb(65, 105, 225), "Аккумулятор");
                break;
            default:
                break;
        }

        DummyItemInfo.ConsumeItem(Items, selectedType, 1);
    }

    private void UseBuildingPack(
        ItemType selectedType,
        ushort playerX,
        ushort playerY,
        Direction direction)
    {
        PackType packType = DummyItemInfo.ItemTypeToPackType(selectedType);
        if (packType == PackType.None)
        {
            return;
        }

        ushort distance = BuildingTemplates.GetAnchorDistance(packType);
        (int offsetX, int offsetY) = direction switch
        {
            Direction.Up => (0, -1),
            Direction.Down => (0, 1),
            Direction.Left => (-1, 0),
            Direction.Right => (1, 0),
            _ => (0, 0),
        };

        long anchorXValue = playerX + (offsetX * distance);
        long anchorYValue = playerY + (offsetY * distance);
        if (anchorXValue is < 0 or > ushort.MaxValue ||
            anchorYValue is < 0 or > ushort.MaxValue)
        {
            return;
        }

        var anchorX = (ushort)anchorXValue;
        var anchorY = (ushort)anchorYValue;
        List<IHBPacket> placementPackets =
        [
            new PackPacket(anchorX, anchorY, packType, 0, 0),
        ];
        PlaceBuildingCells(placementPackets, anchorX, anchorY, packType);
        sendPacket(new ServerPacket(new HBPacket([.. placementPackets])));
        if (packType == PackType.Teleport)
        {
            teleportPositions.Add((anchorX, anchorY));
        }

        DummyItemInfo.ConsumeItem(Items, selectedType, 1);
    }

    private void PlaceBuildingCells(
        List<IHBPacket> packets,
        ushort anchorX,
        ushort anchorY,
        PackType packType)
    {
        if (!BuildingTemplates.TryGet(packType, out PackBuilding? building) || building == null)
        {
            return;
        }

        foreach (((int dx, int dy), CellType cell) in building.CellsToPlace())
        {
            long targetXValue = anchorX + dx;
            long targetYValue = anchorY + dy;
            if (targetXValue is < 0 or > ushort.MaxValue ||
                targetYValue is < 0 or > ushort.MaxValue)
            {
                continue;
            }

            var targetX = (ushort)targetXValue;
            var targetY = (ushort)targetYValue;
            CellType current = getCell(targetX, targetY);
            bool isAllowedBase = current is CellType.Empty or CellType.Road or
                CellType.GoldenRoad or CellType.BuildingRoad;
            if (!isAllowedBase)
            {
                continue;
            }

            setCell(targetX, targetY, cell);
            packets.Add(new MapRegionPacket(
                targetX,
                targetY,
                0,
                0,
                [cell]));
        }
    }
}
