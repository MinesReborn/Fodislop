#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using Fodinae.Networking.Buildings;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Inventory;
using MinesServer.Networking.Server.Packets.World;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyInventoryResponder
{
    private readonly Action<ServerPacket> _sendPacket;
    private readonly Action<string, int, System.Drawing.Color, string> _activateBuff;
    private readonly List<(ushort X, ushort Y)> _teleportPositions;
    private readonly Action<int> _setHealth;
    private readonly Func<ushort, ushort, CellType> _getCell;
    private readonly Action<ushort, ushort, CellType> _setCell;
    private ItemType? _selectedItemType;

    public DummyInventoryResponder(
        Action<ServerPacket> sendPacket,
        Action<string, int, System.Drawing.Color, string> activateBuff,
        List<(ushort X, ushort Y)> teleportPositions,
        Action<int> setHealth,
        Func<ushort, ushort, CellType> getCell,
        Action<ushort, ushort, CellType> setCell)
    {
        _sendPacket = sendPacket ?? throw new ArgumentNullException(nameof(sendPacket));
        _activateBuff = activateBuff ?? throw new ArgumentNullException(nameof(activateBuff));
        _teleportPositions = teleportPositions ??
            throw new ArgumentNullException(nameof(teleportPositions));
        _setHealth = setHealth ?? throw new ArgumentNullException(nameof(setHealth));
        _getCell = getCell ?? throw new ArgumentNullException(nameof(getCell));
        _setCell = setCell ?? throw new ArgumentNullException(nameof(setCell));
    }

    public Dictionary<ItemType, long> Items { get; } = new();

    public void ReplaceItems(IEnumerable<KeyValuePair<ItemType, long>> items)
    {
        Items.Clear();
        foreach (KeyValuePair<ItemType, long> item in items)
        {
            Items[item.Key] = item.Value;
        }
    }

    public void Select(ItemType item)
    {
        _selectedItemType = item;
        var (name, description) = DummyItemInfo.GetItemInfo(item);
        _sendPacket(new ServerPacket(
            new MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket(
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

        if (selectedType == ItemType.Rem)
        {
            _setHealth(500);
            _sendPacket(new ServerPacket(new HealthPacket(500, 500)));
        }
        else if (selectedType == ItemType.UpgradeBooster)
        {
            _activateBuff("xp3", 86400, System.Drawing.Color.FromArgb(0, 200, 0), "Прокачка x3");
        }
        else if (selectedType == ItemType.FreeUp)
        {
            _activateBuff("freeup", 43200, System.Drawing.Color.Cyan, "Freeup");
        }
        else if (selectedType == ItemType.MineBooster)
        {
            _activateBuff("x4", 43200, System.Drawing.Color.FromArgb(255, 165, 0), "Добыча x4");
        }
        else if (selectedType == ItemType.Battery)
        {
            _activateBuff("battery", 3600, System.Drawing.Color.FromArgb(65, 105, 225), "Аккумулятор");
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
        List<IHBPacket> placementPackets = new()
        {
            new PackPacket(anchorX, anchorY, packType, 0, 0),
        };
        PlaceBuildingCells(placementPackets, anchorX, anchorY, packType);
        _sendPacket(new ServerPacket(new HBPacket(placementPackets.ToArray())));
        if (packType == PackType.Teleport)
        {
            _teleportPositions.Add((anchorX, anchorY));
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
                [cell]));
        }
    }
}
