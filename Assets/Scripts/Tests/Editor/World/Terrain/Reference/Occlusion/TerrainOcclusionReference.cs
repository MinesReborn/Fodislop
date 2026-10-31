#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.World;
using Kern.World.Terrain;
using MinesServer.Data;
using UnityEngine;

namespace Kern.Tests.World;

// Эталон перекрытия фона: правило, по которому сборка клеток отбрасывала фон
// до переноса этого решения в шейдер (TerrainForegroundOcclusion). Читает
// вершины переднего плана и кэш клеток, а не биты буфера, — поэтому
// независим от шейдерной расшифровки, с которой сверяется.
internal static class TerrainOcclusionReference
{
    public static bool CoversCell(
        int x,
        int y,
        int foregroundAtlas,
        ReadOnlySpan<TerrainVertex> foreground,
        TerrainCellSources sources)
    {
        if (foregroundAtlas < 0 || foregroundAtlas >= sources.Atlases.Count)
        {
            return false;
        }

        TerrainVertex vertex = foreground[0];
        bool hasTexture = vertex.UV1.z > 0.0001f;
        bool roundable = (Mathf.RoundToInt(vertex.UV6.z) & 1) != 0;
        if (!hasTexture || roundable)
        {
            return false;
        }

        // Смещённый квад закрывает клетку, только если лежит внутри
        // массы: его узлы общие с соседями.
        if (vertex.UV5x != 0 && !IsInsideOpaqueMass(x, y, sources))
        {
            return false;
        }

        CellType foregroundType = sources.CellCache.GetCellData(x + 1, y + 1).Type;
        return sources.Atlases[foregroundAtlas].IsFullyOpaque(foregroundType);
    }

    private static bool IsInsideOpaqueMass(int x, int y, TerrainCellSources sources)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if ((dx != 0 || dy != 0) &&
                    !IsOpaqueMassCell(sources.CellCache.GetCellData(x + 1 + dx, y + 1 + dy), sources.Atlases))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsOpaqueMassCell(CachedCellData cell, IReadOnlyList<IAtlasDescriptor> atlases)
    {
        if (cell.State != TerrainCellState.Loaded ||
            cell.IsBackground ||
            cell.Outline != CellOutline.Wavy)
        {
            return false;
        }

        for (int index = 0; index < atlases.Count; index++)
        {
            if (atlases[index].IsFullyOpaque(cell.Type))
            {
                return true;
            }
        }

        return false;
    }
}
