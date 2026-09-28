#nullable enable

using System;
using Kern.World.Terrain.Background;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;

namespace Kern.World.Terrain;

internal static class TerrainBackgroundTileResolver
{
    public static int ResolveDescriptor(
        in TerrainCellSources sources,
        int x,
        int y,
        bool hasTileGroup,
        int tileGroupId)
    {
        if (!hasTileGroup)
        {
            return 0;
        }

        byte mask = 0;
        if (Matches(sources, x - 1, y, tileGroupId)) mask |= 1 << 0;
        if (Matches(sources, x - 1, y - 1, tileGroupId)) mask |= 1 << 1;
        if (Matches(sources, x, y - 1, tileGroupId)) mask |= 1 << 2;
        if (Matches(sources, x + 1, y - 1, tileGroupId)) mask |= 1 << 3;
        if (Matches(sources, x + 1, y, tileGroupId)) mask |= 1 << 4;
        if (Matches(sources, x + 1, y + 1, tileGroupId)) mask |= 1 << 5;
        if (Matches(sources, x, y + 1, tileGroupId)) mask |= 1 << 6;
        if (Matches(sources, x - 1, y + 1, tileGroupId)) mask |= 1 << 7;
        return TileBitmaskConverter.GetDescriptor(mask);
    }

    private static bool Matches(
        in TerrainCellSources sources,
        int x,
        int y,
        int tileGroupId)
    {
        var floodFill = sources.FloodFill.Buffer;
        if ((uint)x >= (uint)floodFill.Width || (uint)y >= (uint)floodFill.Height)
        {
            return false;
        }

        CachedCellData foreground = sources.CellCache.GetCellData(x + 1, y + 1);
        CellType background = TerrainCellLayers.ResolveBackground(
            foreground.Type,
            floodFill[x, y],
            foreground.Properties);
        if (!sources.MetadataLookup.TryGet(background, out CellMetadata metadata))
        {
            throw new InvalidOperationException(
                $"Terrain background metadata for cell type '{background}' was not warmed before tile-group resolution.");
        }

        return metadata.HasTileGroup && metadata.TileGroupID == tileGroupId;
    }
}
