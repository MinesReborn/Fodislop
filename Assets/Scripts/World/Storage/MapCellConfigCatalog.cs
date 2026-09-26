#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Kern.Core;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using UnityEngine;

namespace Kern.World;

public sealed class MapCellConfigCatalog
{
    private CellConfigurationPacket[]? _cellConfigurations;
    private readonly Dictionary<CellType, int> _cellToTileGroup = new();
    private readonly Dictionary<CellType, ushort> _cellMoveSpeeds = new();

    public static CellVisualProperties GetVisualProperties(CellType type) =>
        CellVisualProtocolRegistry.Current.Get(type);

    // Совместимый фасад для старых статических потребителей. Новые системы
    // должны получать профиль через MapCellConfigCatalog и не знать legacy-таблицу.
    public static bool IsRoundableLoose(CellType type) =>
        CellVisualProtocolRegistry.Current.Get(type).IsRoundableLoose;

    public static bool IsRoad(CellType type) =>
        CellVisualProtocolRegistry.Current.Get(type).IsRoad;

    public static bool IsContinuousSheet(CellType type) =>
        CellVisualProtocolRegistry.Current.Get(type).IsContinuousSheet;

    public void LoadConfigurations(CellConfigurationPacket[]? configurations, byte[][]? tileGroups)
    {
        ValidateCellConfigurations(configurations);

        _cellConfigurations = configurations;

        _cellToTileGroup.Clear();
        if (tileGroups != null)
        {
            for (int i = 0; i < tileGroups.Length; i++)
            {
                if (tileGroups[i] == null)
                {
                    continue;
                }

                foreach (byte cellID in tileGroups[i])
                {
                    _cellToTileGroup[(CellType)cellID] = i;
                }
            }
        }
    }

    public void UpdateMovementSpeeds(MovementSpeedPacket packet)
    {
        if (packet.CooldownMap == null)
        {
            return;
        }

        foreach (var entry in packet.CooldownMap)
        {
            _cellMoveSpeeds[entry.Key] = entry.Value;
        }
    }

    public float GetMoveCooldown(CellType cellType)
    {
        if (!_cellMoveSpeeds.TryGetValue(cellType, out ushort speed))
        {
            throw new InvalidOperationException(
                $"Movement cooldown for cell type '{cellType}' was not received from the server.");
        }

        if (speed == 0)
        {
            throw new InvalidDataException(
                $"Movement cooldown for cell type '{cellType}' must be greater than zero.");
        }

        return speed / 1000f;
    }

    public CellConfigurationPacket GetCellConfig(CellType type)
    {
        if (_cellConfigurations == null)
        {
            throw new InvalidOperationException(
                $"Cell configuration requested for '{type}' before WorldInitPacket was loaded.");
        }

        if ((int)type < 0 || (int)type >= _cellConfigurations.Length)
        {
            throw new InvalidOperationException(
                $"Cell type '{type}' has no server configuration. Config count: {_cellConfigurations.Length}.");
        }

        return _cellConfigurations[(int)type];
    }

    public bool TryGetTileGroup(CellType type, out int groupID)
    {
        return _cellToTileGroup.TryGetValue(type, out groupID);
    }

    public Color GetCellMinimapColor(CellType type)
    {
        CellConfigurationPacket[] configurations = _cellConfigurations ??
            throw new InvalidOperationException(
                $"Cell configuration requested for '{type}' before WorldInitPacket was loaded.");
        int typeIndex = (int)type;
        if (typeIndex < 0 || typeIndex >= configurations.Length)
        {
            return MapBlockColors.GetColor(type);
        }

        CellConfigurationPacket config = configurations[typeIndex];
        if (config.Color != 0)
        {
            int argb = config.Color;
            byte a = (byte)((argb >> 24) & 0xFF);
            if (a == 0)
            {
                a = 255;
            }

            byte r = (byte)((argb >> 16) & 0xFF);
            byte g = (byte)((argb >> 8) & 0xFF);
            byte b = (byte)(argb & 0xFF);

            return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
        }

        return MapBlockColors.GetColor(type);
    }

    public int GetAnimationFrameHeight(CellType cellType)
    {
        var config = GetCellConfig(cellType);
        return (int)config.FrameOffset * RenderingConstants.CELL_SIZE;
    }

    public byte GetAnimationSpeed(CellType cellType)
    {
        var config = GetCellConfig(cellType);
        return config.AnimationSpeed;
    }

    public bool HasAnimation(CellType cellType)
    {
        var config = GetCellConfig(cellType);
        return config.Animation != CellAnimationType.None;
    }

    public void Reset()
    {
        _cellConfigurations = null;
        _cellToTileGroup.Clear();
        _cellMoveSpeeds.Clear();
    }

    private static void ValidateCellConfigurations(CellConfigurationPacket[]? configurations)
    {
        if (configurations == null || configurations.Length == 0)
        {
            throw new InvalidDataException(
                "WorldInitPacket.Cells is missing or empty; terrain cannot be initialized.");
        }

        for (int index = 0; index < configurations.Length; index++)
        {
            CellConfigurationPacket configuration = configurations[index];
            if (configuration.Animation == CellAnimationType.None)
            {
                continue;
            }

            if (configuration.AnimationSpeed == 0)
            {
                throw new InvalidDataException(
                    $"WorldInitPacket.Cells[{index}] ({(CellType)index}) declares " +
                    "an animated texture with AnimationSpeed=0.");
            }
        }
    }
}
