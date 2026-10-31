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

                foreach (byte cellId in tileGroups[i])
                {
                    _cellToTileGroup[(CellType)cellId] = i;
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

    public float GetMinMoveCooldown()
    {
        int min = int.MaxValue;
        foreach (ushort speed in _cellMoveSpeeds.Values)
        {
            if (speed > 0 && speed < min)
            {
                min = speed;
            }
        }

        if (min == int.MaxValue)
        {
            throw new InvalidOperationException("Movement cooldowns were not received from the server.");
        }

        return min / 1000f;
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

    public bool TryGetTileGroup(CellType type, out int groupId)
    {
        return _cellToTileGroup.TryGetValue(type, out groupId);
    }

    /// <summary>
    /// Разрешает цвет клетки для мини-карты, большой карты и fallback-текстуры
    /// террейна.
    ///
    /// Палитра клиента — временный fallback: сервер пока шлёт заглушку в
    /// <c>CellConfigurationPacket.Color</c> (<c>0xFFFFFFFF</c> для всех
    /// 256 типов, поэтому доверять пакету для описанных типов нельзя. Пакет
    /// читается только там, где палитра ничего не знает, — то есть для типа,
    /// которого в клиентской таблице ещё нет.
    ///
    /// Считается в <see cref="Color32"/>: растеризаторы карты пишут эти байты в
    /// текстуру напрямую, а путь через <see cref="Color"/> терял бы единицу в
    /// каналах на обратном касте.
    /// </summary>
    public Color32 GetCellMinimapColor32(CellType type)
    {

        if (MapBlockColors.IsAuthored(type))
        {
            return MapBlockColors.GetColor32(type);
        }

        CellConfigurationPacket config = GetCellConfig(type);
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

            return new Color32(r, g, b, a);
        }

        return MapBlockColors.MissingServerColor;
    }

    public Color GetCellMinimapColor(CellType type) => GetCellMinimapColor32(type);

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
    }
}
