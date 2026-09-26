#nullable enable

using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.World.Terrain.Background;
using MinesServer.Data;

namespace Kern.World.Terrain;

// Всё, из чего собирается клетка террейна.
public readonly record struct TerrainCellSources(
    ITerrainCellDataSource CellCache,
    TerrainPrecalculator Precalc,
    BackgroundFloodFill FloodFill,
    int WorldWidth,
    int WorldHeight,
    IReadOnlyList<IAtlasDescriptor> Atlases)
{
    // All cell metadata is resolved before the worker starts. Cell stages only
    // read this lookup and never call the live map or texture services.
    public ITerrainMetadataLookup MetadataLookup => CellCache.MetadataLookup;
}
