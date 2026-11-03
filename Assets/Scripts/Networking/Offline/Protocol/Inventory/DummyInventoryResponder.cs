#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Inventory;
using MinesServer.Networking.Server.Packets.World;

namespace Kern.Networking.Offline;

internal sealed class DummyInventoryResponder
{
    private readonly Action<ServerPacket> _sendPacket;
    private readonly Action<string, int, System.Drawing.Color, string> _activateBuff;
    private readonly List<(ushort X, ushort Y)> _teleportPositions;
    private readonly Action<int> _setHealth;
    private readonly Func<ushort, ushort, CellType> _getCell;
    private readonly Action<ushort, ushort, CellType> _setCell;
    internal DummyInventoryResponder(
    Action<ServerPacket> sendPacket,
    Action<string, int, System.Drawing.Color, string> activateBuff,
    List<(ushort X, ushort Y)> teleportPositions,
    Action<int> setHealth,
    Func<ushort, ushort, CellType> getCell,
    Action<ushort, ushort, CellType> setCell)
    {
        _sendPacket = sendPacket;
        _activateBuff = activateBuff;
        _teleportPositions = teleportPositions;
        _setHealth = setHealth;
        _getCell = getCell;
        _setCell = setCell;
    }

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
        _sendPacket(new ServerPacket(
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
        _sendPacket(new ServerPacket(default(DeselectItemPacket)));
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
                _setHealth(500);
                _sendPacket(new ServerPacket(new HealthPacket(500, 500)));
                break;
            case ItemType.UpgradeBooster:
                _activateBuff("xp3", 86400, System.Drawing.Color.FromArgb(0, 200, 0), "Прокачка x3");
                break;
            case ItemType.FreeUp:
                _activateBuff("freeup", 43200, System.Drawing.Color.Cyan, "Freeup");
                break;
            case ItemType.MineBooster:
                _activateBuff("x4", 43200, System.Drawing.Color.FromArgb(255, 165, 0), "Добыча x4");
                break;
            case ItemType.Battery:
                _activateBuff("battery", 3600, System.Drawing.Color.FromArgb(65, 105, 225), "Аккумулятор");
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
        BuildingType buildingType = DummyItemInfo.ItemTypeToPackType(selectedType);
        if (buildingType == BuildingType.None)
        {
            return;
        }

        ushort distance = BuildingTemplates.GetAnchorDistance(buildingType);
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
        List<IHBPacket> placementPackets = new()
        {
            new BuildingPacket(anchorX, anchorY, buildingType, 0, 0),
        };
        PlaceBuildingCells(placementPackets, anchorX, anchorY, buildingType);
        _sendPacket(new ServerPacket(new HBPacket(placementPackets)));
        if (buildingType == BuildingType.Teleport)
        {
            _teleportPositions.Add((anchorX, anchorY));
        }

        DummyItemInfo.ConsumeItem(Items, selectedType, 1);
    }

    private void PlaceBuildingCells(
        List<IHBPacket> packets,
        ushort anchorX,
        ushort anchorY,
        BuildingType buildingType)
    {
        if (!BuildingTemplates.TryGet(buildingType, out PackBuilding? building) || building == null)
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
            CellType current = _getCell(targetX, targetY);
            bool isAllowedBase = current is CellType.Empty or CellType.Road or
                CellType.GoldenRoad or CellType.BuildingRoad;
            if (!isAllowedBase)
            {
                continue;
            }

            _setCell(targetX, targetY, cell);
            packets.Add(new MapRegionPacket(
                targetX,
                targetY,
                0,
                0,
                new[] { cell }));
        }
    }
}
