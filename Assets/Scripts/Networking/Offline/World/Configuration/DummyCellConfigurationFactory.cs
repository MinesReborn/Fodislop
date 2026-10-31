#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Kern.AssetPipeline.Cache;
using Kern.AssetPipeline.Loading;
using Kern.Core.Interfaces;
using Kern.World;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;

namespace Kern.Networking.Offline;

internal static class DummyCellConfigurationFactory
{
    public static BlockDefinition GetBlockDefinition(CellType type) =>
        BlockRegistry.Get(type);

    public static byte[][] CreateTileGroups()
    {
        return BlockRegistry.Blocks
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.TileGroup))
            .GroupBy(pair => pair.Value.TileGroup!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group
                .Select(pair => (byte)pair.Key)
                .OrderBy(cellId => cellId)
                .ToArray())
            .ToArray();
    }

    public static CellConfigurationPacket[] CreateCellConfigurations()
    {
        var configs = new CellConfigurationPacket[256];
        for (int i = 0; i < 256; i++)
        {
            var type = (CellType)i;
            BlockDefinition def = BlockRegistry.Get(type);

            configs[i] = new CellConfigurationPacket(
                Passable: def.Passable,
                DrawLayer: (CellRenderLayer)def.DrawLayer,
                Outline: (CellOutlineKind)def.Outline,
                TextureName: def.TextureName,
                DecalTextureName: def.DecalTextureName ?? string.Empty,
                ReliefGroup: def.ReliefGroup,
                AnimationType: def.AnimationType,
                AnimationPalette: def.AnimationPalette,
                FrameAnimationSpeed: def.FrameAnimationSpeed,
                MapColor: MapBlockColors.GetPackedColor(type));
        }

        return configs;
    }

    public static async UniTask<WorldAssetDefinitionPacket[]> CreateAssetManifestAsync(
        ITextureStorageService textureStorage,
        IRuntimeAssetPaths assetPaths,
        CellConfigurationPacket[] cellConfigurations)
    {
        var assets = new Dictionary<string, WorldAssetDefinitionPacket>(StringComparer.Ordinal);

        IReadOnlyList<string> textureFilenames = assetPaths.GetTextureFilenames();
        if (textureFilenames.Count == 0)
        {
            throw new InvalidOperationException(
                "Runtime texture paths must be ready before building the world asset manifest.");
        }

        foreach (string filename in textureFilenames.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!AssetBatchDispatcher.IsTextureFile(filename))
            {
                continue;
            }

            byte[]? contents = assetPaths.GetTextureBytes(filename);
            if (contents is { Length: > 0 })
            {
                assets.Add(filename, CreateAssetDefinition(filename, contents));
            }
        }

        foreach (string filename in await assetPaths.GetAudioBankFilenamesAsync())
        {
            byte[]? contents = await assetPaths.GetAudioBankBytesAsync(filename);
            if (contents is { Length: > 0 })
            {
                assets.Add(filename, CreateAssetDefinition(filename, contents));
            }
        }

        for (int cellId = 0; cellId < cellConfigurations.Length; cellId++)
        {
            CellConfigurationPacket configuration = cellConfigurations[cellId];
            if (!string.IsNullOrWhiteSpace(configuration.TextureName))
            {
                byte[]? contents = await textureStorage.GetTextureData(configuration.TextureName);
                if (contents == null || contents.Length == 0)
                {
                    cellConfigurations[cellId] = configuration with { TextureName = string.Empty };
                }
                else
                {
                    assets[configuration.TextureName] = CreateAssetDefinition(configuration.TextureName, contents);
                }
            }

            if (!string.IsNullOrWhiteSpace(configuration.DecalTextureName))
            {
                byte[]? decalContents = await textureStorage.GetTextureData(configuration.DecalTextureName);
                if (decalContents == null || decalContents.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"Cell '{(CellType)cellId}' requires decal texture '{configuration.DecalTextureName}', but it is missing.");
                }

                assets[configuration.DecalTextureName] =
                    CreateAssetDefinition(configuration.DecalTextureName, decalContents);
            }
        }

        return assets.Values
            .OrderBy(asset => asset.Filename, StringComparer.Ordinal)
            .ToArray();
    }

    private static WorldAssetDefinitionPacket CreateAssetDefinition(string filename, byte[] contents) =>
        new(filename, ETagCalculator.Calculate(contents) ??
            throw new InvalidOperationException($"Could not calculate ETag for world asset '{filename}'."));

    public static Dictionary<CellType, ushort> CreateMovementSpeeds()
    {
        var speeds = new Dictionary<CellType, ushort>(BlockRegistry.Blocks.Count);
        foreach ((CellType type, BlockDefinition def) in BlockRegistry.Blocks)
        {
            speeds[type] = def.MovementCooldownMs;
        }

        return speeds;
    }
}
