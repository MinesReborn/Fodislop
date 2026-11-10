#nullable enable

using System.Collections.Generic;
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
    TerrainDistortionSettings Distortion,
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
                IsPopulated = true,
                AtlasIndex = 0,
                AtlasRect = new UnityEngine.Vector4(0f, 0f, 0.0625f, 0.0625f),
                HasTileGroup = index % 5 == 0,
                TileGroupId = index % 4,
                AnimationFrameCount = 1,
                IsTextureReady = true,
            };
        }

        return metadata;
    }
}
