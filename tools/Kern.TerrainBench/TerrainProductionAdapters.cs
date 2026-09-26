#nullable enable

using System.Collections.Generic;
using Kern.World.Terrain.Background;
using MinesServer.Data;

namespace Kern.World.Terrain;

public interface ITerrainMetadataLookup
{
    bool TryGet(CellType type, out CellMetadata metadata);
}

// This adapter mirrors the production input shape while keeping Unity service
// interfaces and asset-backed atlas implementations out of the benchmark.
public readonly record struct TerrainCellSources(
    ITerrainCellDataSource CellCache,
    TerrainPrecalculator Precalc,
    BackgroundFloodFill FloodFill,
    int WorldWidth,
    int WorldHeight,
    IReadOnlyList<Kern.Core.Interfaces.IAtlasDescriptor> Atlases,
    ITerrainMetadataLookup MetadataLookup);

public sealed class BenchTerrainMetadataLookup : ITerrainMetadataLookup
{
    private readonly CellMetadata[] _metadata = CreateMetadata();

    public bool TryGet(CellType type, out CellMetadata metadata)
    {
        int index = (byte)type;
        metadata = _metadata[index];
        return metadata.IsPopulated;
    }

    private static CellMetadata[] CreateMetadata()
    {
        var metadata = new CellMetadata[256];
        for (int index = 0; index < metadata.Length; index++)
        {
            metadata[index] = new CellMetadata
            {
                Properties = 0,
                HasTileGroup = index % 5 == 0,
                TileGroupID = index % 4,
                MinimapColor = new UnityEngine.Color32(128, 128, 128, 255),
                AtlasRect = new UnityEngine.Vector4(0f, 0f, 0.0625f, 0.0625f),
                AtlasIndex = 0,
                UVTileSize = 1f / 1024f,
                AnimationFrameCount = 1,
                FrameHeightTiles = 1f,
                IsTextureReady = true,
                IsPopulated = true,
            };
        }

        return metadata;
    }
}
