#nullable enable

using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.World.Terrain.Background;
using MinesServer.Data;

namespace Kern.World.Terrain;

// Всё, из чего собирается клетка террейна.
public readonly record struct TerrainCellSources(
    TerrainCellCache CellCache,
    TerrainPrecalculator Precalc,
    BackgroundFloodFill FloodFill,
    int WorldWidth,
    int WorldHeight,
    IReadOnlyList<IAtlasDescriptor> Atlases,
    bool UseColorLod,
    MapManager MapManager,
    ITextureService TextureService);
