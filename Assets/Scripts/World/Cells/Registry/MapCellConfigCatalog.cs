#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kern.Core;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using Kern.World.Terrain;
using Kern.World.Textures;
using UnityEngine;

namespace Kern.World;

public sealed class MapCellConfigCatalog
{
    private CellConfigurationPacket[]? _cellConfigurations;
    private readonly Dictionary<CellType, int> _cellToTileGroup = new();
    private readonly Dictionary<CellType, ushort> _cellMoveSpeeds = new();
    private readonly Dictionary<string, AssetsDefinitionPacket> _assetsByName = new(StringComparer.Ordinal);

    public void LoadConfigurations(
        CellConfigurationPacket[]? configurations,
        byte[][]? tileGroups,
        AssetsDefinitionPacket[]? assetManifest = null)
    {
        ValidateCellConfigurations(configurations);

        // Всё проверяется на локальных копиях, а состояние подменяется одним куском в конце: обновление
        // посреди игры, упавшее на проверке, не должно оставить каталог наполовину новым
        var assetsByName = new Dictionary<string, AssetsDefinitionPacket>(StringComparer.Ordinal);
        if (assetManifest != null)
        {
            foreach (AssetsDefinitionPacket asset in assetManifest)
            {
                ValidateAssetDefinition(asset);
                if (!assetsByName.TryAdd(asset.Filename, asset))
                {
                    throw new InvalidDataException($"WorldInitPacket.AssetsManifest contains duplicate filename '{asset.Filename}'.");
                }
            }
        }

        var decalTextureNames = configurations!
            .Select(configuration => configuration.DecalTextureName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        long textureArrayBytes = (long)TerrainDecalFactory.TextureWidth *
            TerrainDecalFactory.TextureHeight * (decalTextureNames.Length + 1) * 4;
        if (textureArrayBytes > 64L * 1024 * 1024)
        {
            throw new InvalidDataException("World decal texture array exceeds the 64 MiB client limit.");
        }

        foreach (CellConfigurationPacket configuration in configurations!)
        {
            if (!string.IsNullOrEmpty(configuration.TextureName) &&
                !assetsByName.ContainsKey(configuration.TextureName))
            {
                throw new InvalidDataException(
                    $"Cell configuration references texture '{configuration.TextureName}', which is absent from WorldInitPacket.AssetsManifest.");
            }

            if (!string.IsNullOrEmpty(configuration.DecalTextureName) &&
                !assetsByName.ContainsKey(configuration.DecalTextureName))
            {
                throw new InvalidDataException(
                    $"Cell configuration references decal texture '{configuration.DecalTextureName}', which is absent from WorldInitPacket.AssetsManifest.");
            }
        }

        _cellConfigurations = configurations;
        _assetsByName.Clear();
        foreach (AssetsDefinitionPacket asset in assetsByName.Values)
        {
            _assetsByName.Add(asset.Filename, asset);
        }

        TerrainDecalCatalog.Configure(decalTextureNames);

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

    public bool TryGetAssetDefinition(string? filename, out AssetsDefinitionPacket definition)
    {
        if (string.IsNullOrEmpty(filename))
        {
            definition = default;
            return false;
        }

        return _assetsByName.TryGetValue(filename, out definition);
    }

    public bool TryGetAudioBanks(out IReadOnlyList<AssetsDefinitionPacket> banks)
    {
        if (_cellConfigurations == null)
        {
            banks = Array.Empty<AssetsDefinitionPacket>();
            return false;
        }

        banks = _assetsByName.Values
            .Where(asset => Path.GetExtension(asset.Filename) == ".bank")
            .OrderBy(asset => asset.Filename, StringComparer.Ordinal)
            .ToArray();
        return true;
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
    /// <c>CellConfigurationPacket.MapColor</c> (<c>0xFFFFFFFF</c> для всех
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
        if (config.MapColor != 0)
        {
            int argb = config.MapColor;
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
        _assetsByName.Clear();
        TerrainDecalCatalog.Configure(Array.Empty<string>());
    }

    private static void ValidateCellConfigurations(CellConfigurationPacket[]? configurations)
    {
        if (configurations == null || configurations.Length == 0)
        {
            throw new InvalidDataException(
                "WorldInitPacket.Cells is missing or empty; terrain cannot be initialized.");
        }
    }

    private static void ValidateAssetDefinition(in AssetsDefinitionPacket asset)
    {
        if (string.IsNullOrWhiteSpace(asset.Filename) || asset.Filename.Length > byte.MaxValue)
        {
            throw new InvalidDataException($"World asset '{asset.Filename}' has an invalid filename.");
        }

        string normalizedPath = asset.Filename.Replace('\\', '/');
        if (normalizedPath.StartsWith('/') || normalizedPath.Split('/').Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException($"World asset '{asset.Filename}' must be a safe relative filename.");
        }

        foreach (char character in asset.Filename)
        {
            if (character > 0x7F)
            {
                throw new InvalidDataException($"World asset '{asset.Filename}' must contain ASCII characters only.");
            }
        }

        if (asset.ETag.Length != 0 && asset.ETag.Length != 32)
        {
            throw new InvalidDataException($"World asset '{asset.Filename}' ETag must be a 32-character MD5 value or empty.");
        }

        foreach (char character in asset.ETag)
        {
            if (!Uri.IsHexDigit(character))
            {
                throw new InvalidDataException($"World asset '{asset.Filename}' ETag contains a non-hex character.");
            }
        }
    }
}
