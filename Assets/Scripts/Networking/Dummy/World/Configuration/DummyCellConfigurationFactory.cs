#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using Kern.AssetPipeline.Cache;
using Kern.AssetPipeline.Loading;
using Kern.Core.Interfaces;
using Kern.World;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;

namespace Kern.Networking.Dummy;

internal static class DummyCellConfigurationFactory
{
    private static string WithPngExtension(string? name) =>
        string.IsNullOrWhiteSpace(name) || Path.GetExtension(name).Length > 0 ? name ?? string.Empty : name + ".png";

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
                Properties: ToProperties(def),
                DrawLayer: (CellRenderLayer)def.DrawLayer,
                Outline: (CellOutlineKind)def.Outline,
                ReliefGroup: def.ReliefGroup,
                AnimationType: def.AnimationType,
                AnimationPalette: def.AnimationPalette,
                FrameAnimationSpeed: def.FrameAnimationSpeed,
                MapColor: MapBlockColors.GetPackedColor(type),
                TextureAnchor: (CellTextureAnchorKind)def.TextureAnchor);
        }

        return configs;
    }

    public static CellDecalEntry[] CreateDecalMap()
    {
        return BlockRegistry.Blocks
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.DecalTextureName))
            .GroupBy(pair => pair.Value.DecalTextureName!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new CellDecalEntry(
                WithPngExtension(group.Key),
                group.Select(pair => (byte)pair.Key).OrderBy(cell => cell).ToArray()))
            .ToArray();
    }

    private static CellConfigProperties ToProperties(BlockDefinition definition)
    {
        CellConfigProperties properties = CellConfigProperties.None;
        if (definition.Passable)
        {
            properties |= CellConfigProperties.Passable;
        }

        if (definition.Emission)
        {
            properties |= CellConfigProperties.Glowing;
        }

        return properties;
    }

    public static async UniTask<AssetsDefinitionPacket[]> CreateAssetManifestAsync(
        ITextureStorageService textureStorage,
        IRuntimeAssetPaths assetPaths,
        CellConfigurationPacket[] cellConfigurations,
        CellDecalEntry[] decalMap)
    {
        var assets = new Dictionary<string, AssetsDefinitionPacket>(StringComparer.Ordinal);
        var audioAssets = new List<AssetsDefinitionPacket>();

        IReadOnlyList<string> textureFilenames = assetPaths.GetDummyWorldTextureFilenames();
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
                assets[filename] = CreateAssetDefinition(filename, contents);
            }
        }

        foreach (string filename in await assetPaths.GetAudioBankFilenamesAsync())
        {
            byte[]? contents = await assetPaths.GetAudioBankBytesAsync(filename);
            if (contents is { Length: > 0 })
            {
                audioAssets.Add(CreateAssetDefinition(filename, contents));
            }
        }

        for (int cellId = 0; cellId < cellConfigurations.Length; cellId++)
        {
            // Одно имя на файл: везде с расширением, как в манифесте (клиент сверяет имена точно).
            // Имя основной текстуры выводится из типа клетки, в пакете его нет.
            string textureName = WithPngExtension(BlockRegistry.Get((CellType)cellId).TextureName);
            if (!string.IsNullOrWhiteSpace(textureName))
            {
                byte[]? contents = await textureStorage.GetTextureData(textureName);
                if (contents == null || contents.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"Cell '{(CellType)cellId}' requires texture '{textureName}', but it is missing.");
                }

                assets[textureName] = CreateAssetDefinition(textureName, contents);
            }
        }

        foreach (CellDecalEntry decal in decalMap)
        {
            byte[]? decalContents = await textureStorage.GetTextureData(decal.TextureName);
            if (decalContents == null || decalContents.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Decal texture '{decal.TextureName}' is missing.");
            }

            assets[decal.TextureName] = CreateAssetDefinition(decal.TextureName, decalContents);
        }

        // Сначала визуал, потом аудио: файлы качаются в порядке манифеста
        return assets.Values
            .OrderBy(asset => asset.Filename, StringComparer.Ordinal)
            .Concat(audioAssets.OrderBy(asset => asset.Filename, StringComparer.Ordinal))
            .ToArray();
    }

    private static AssetsDefinitionPacket CreateAssetDefinition(string filename, byte[] contents) =>
        new(filename, ETagCalculator.Calculate(contents) ??
            throw new InvalidOperationException($"Could not calculate ETag for world asset '{filename}'."),
            (uint)contents.Length);

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
