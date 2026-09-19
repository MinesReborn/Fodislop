#nullable enable

using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Kern.World.Terrain;

public sealed class TerrainCellMaskCalculator
{
    public TerrainRingGrid<int> CellTilingDescriptors { get; } = new();

    public TerrainRingGrid<int> CellCornerVariants { get; } = new();

    public TerrainRingGrid<byte> CellReliefMasks { get; } = new();

    public TerrainRingGrid<byte> CellSolidBoundaryMasks { get; } = new();

    public void EnsureCapacity(int meshWidth, int meshHeight)
    {
        if (CellTilingDescriptors.Width != meshWidth || CellTilingDescriptors.Height != meshHeight)
        {
            CellTilingDescriptors.EnsureSize(meshWidth, meshHeight);
            CellCornerVariants.EnsureSize(meshWidth, meshHeight);
            CellReliefMasks.EnsureSize(meshWidth, meshHeight);
            CellSolidBoundaryMasks.EnsureSize(meshWidth, meshHeight);
        }
    }

    public void PrecalculateFull(TerrainCellCache cellCache, int meshWidth, int meshHeight)
    {
        EnsureCapacity(meshWidth, meshHeight);

        System.Threading.Tasks.Parallel.For(0, meshWidth, x =>
        {
            for (int y = 0; y < meshHeight; y++)
            {
                CalculateCellNode(cellCache, x, y);
            }
        });
    }

    public void PrecalculateRegion(TerrainCellCache cellCache, int meshWidth, int meshHeight, int startX, int startY, int countX, int countY)
    {
        int cxMin = Mathf.Clamp(startX, 0, meshWidth);
        int cxMax = Mathf.Clamp(startX + countX, 0, meshWidth);
        int cyMin = Mathf.Clamp(startY, 0, meshHeight);
        int cyMax = Mathf.Clamp(startY + countY, 0, meshHeight);

        for (int x = cxMin; x < cxMax; x++)
        {
            for (int y = cyMin; y < cyMax; y++)
            {
                CalculateCellNode(cellCache, x, y);
            }
        }
    }

    public void PrecalculateIncremental(TerrainCellCache cellCache, int meshWidth, int meshHeight, int dx, int dy)
    {
        EnsureCapacity(meshWidth, meshHeight);

        CellTilingDescriptors.Scroll(dx, dy);
        CellCornerVariants.Scroll(dx, dy);
        CellReliefMasks.Scroll(dx, dy);
        CellSolidBoundaryMasks.Scroll(dx, dy);

        int cxStart = 0;
        int cxLen = 0;
        int cyStart = 0;
        int cyLen = 0;

        if (dx > 0)
        {
            cxStart = Mathf.Max(0, meshWidth - dx - 1);
            cxLen = meshWidth - cxStart;
        }
        else if (dx < 0)
        {
            cxStart = 0;
            cxLen = Mathf.Min(meshWidth, -dx + 1);
        }

        if (dy > 0)
        {
            cyStart = Mathf.Max(0, meshHeight - dy - 1);
            cyLen = meshHeight - cyStart;
        }
        else if (dy < 0)
        {
            cyStart = 0;
            cyLen = Mathf.Min(meshHeight, -dy + 1);
        }

        if (cxLen > 0 || cyLen > 0)
        {
            if (cxLen > 0)
            {
                for (int x = cxStart; x < cxStart + cxLen; x++)
                {
                    for (int y = 0; y < meshHeight; y++)
                    {
                        CalculateCellNode(cellCache, x, y);
                    }
                }
            }

            if (cyLen > 0 && cxLen < meshWidth)
            {
                int xStart = 0;
                int xEnd = meshWidth;

                if (cxLen > 0)
                {
                    if (dx > 0)
                    {
                        xEnd = cxStart;
                    }
                    else
                    {
                        xStart = cxLen;
                    }
                }

                if (xStart < xEnd)
                {
                    for (int y = cyStart; y < cyStart + cyLen; y++)
                    {
                        for (int x = xStart; x < xEnd; x++)
                        {
                            CalculateCellNode(cellCache, x, y);
                        }
                    }
                }
            }
        }
    }

    public void CalculateCellNode(TerrainCellCache cellCache, int x, int y)
    {
        int cx = x + 1;
        int cy = y + 1;
        CachedCellData data = cellCache.GetCellData(cx, cy);

        CachedCellData top = cellCache.GetCellData(cx, cy + 1);
        CachedCellData bottom = cellCache.GetCellData(cx, cy - 1);
        CachedCellData left = cellCache.GetCellData(cx - 1, cy);
        CachedCellData right = cellCache.GetCellData(cx + 1, cy);
        CachedCellData bottomLeft = cellCache.GetCellData(cx - 1, cy - 1);
        CachedCellData bottomRight = cellCache.GetCellData(cx + 1, cy - 1);
        CachedCellData topRight = cellCache.GetCellData(cx + 1, cy + 1);
        CachedCellData topLeft = cellCache.GetCellData(cx - 1, cy + 1);

        CellTilingDescriptors[x, y] = CalculateTilingDescriptor(data, left, bottomLeft, bottom, bottomRight, right, topRight, top, topLeft);
        CellCornerVariants[x, y] = CalculateCornerSideMask(data, left, right, top, bottom);
        CellReliefMasks[x, y] = CalculateReliefMask(data, top, left, bottom, right);
        CellSolidBoundaryMasks[x, y] = CalculateSolidBoundaryMask(top, left, bottom, right, topLeft, topRight, bottomLeft, bottomRight);
    }

    public static int CalculateTilingDescriptor(
        CachedCellData data,
        CachedCellData left,
        CachedCellData bottomLeft,
        CachedCellData bottom,
        CachedCellData bottomRight,
        CachedCellData right,
        CachedCellData topRight,
        CachedCellData top,
        CachedCellData topLeft)
    {
        if (!data.HasTileGroup)
        {
            return 0;
        }

        byte m = 0;
        if (left.HasTileGroup && left.TileGroupID == data.TileGroupID)
        {
            m |= 1 << 0;
        }

        if (bottomLeft.HasTileGroup && bottomLeft.TileGroupID == data.TileGroupID)
        {
            m |= 1 << 1;
        }

        if (bottom.HasTileGroup && bottom.TileGroupID == data.TileGroupID)
        {
            m |= 1 << 2;
        }

        if (bottomRight.HasTileGroup && bottomRight.TileGroupID == data.TileGroupID)
        {
            m |= 1 << 3;
        }

        if (right.HasTileGroup && right.TileGroupID == data.TileGroupID)
        {
            m |= 1 << 4;
        }

        if (topRight.HasTileGroup && topRight.TileGroupID == data.TileGroupID)
        {
            m |= 1 << 5;
        }

        if (top.HasTileGroup && top.TileGroupID == data.TileGroupID)
        {
            m |= 1 << 6;
        }

        if (topLeft.HasTileGroup && topLeft.TileGroupID == data.TileGroupID)
        {
            m |= 1 << 7;
        }

        return TileBitmaskConverter.GetDescriptor(m);
    }

    public static int CalculateCornerSideMask(
        CachedCellData data,
        CachedCellData left,
        CachedCellData right,
        CachedCellData top,
        CachedCellData bottom)
    {
        int cornerSideMask = 0;
        if (data.Type == CellType.BuildingWall)
        {
            if (left.Type == CellType.BuildingCorner)
            {
                cornerSideMask |= 1;
            }

            if (right.Type == CellType.BuildingCorner)
            {
                cornerSideMask |= 2;
            }

            if (top.Type == CellType.BuildingCorner)
            {
                cornerSideMask |= 4;
            }

            if (bottom.Type == CellType.BuildingCorner)
            {
                cornerSideMask |= 8;
            }
        }

        return cornerSideMask;
    }

    public static byte CalculateReliefMask(
        CachedCellData data,
        CachedCellData top,
        CachedCellData left,
        CachedCellData bottom,
        CachedCellData right)
    {
        byte rm = 0;
        if (top.ReliefGroup >= data.ReliefGroup)
        {
            rm |= 1;
        }

        if (left.ReliefGroup >= data.ReliefGroup)
        {
            rm |= 2;
        }

        if (bottom.ReliefGroup >= data.ReliefGroup)
        {
            rm |= 4;
        }

        if (right.ReliefGroup >= data.ReliefGroup)
        {
            rm |= 8;
        }

        return rm;
    }

    public static byte CalculateSolidBoundaryMask(
        CachedCellData top,
        CachedCellData left,
        CachedCellData bottom,
        CachedCellData right,
        CachedCellData topLeft,
        CachedCellData topRight,
        CachedCellData bottomLeft,
        CachedCellData bottomRight)
    {
        byte solidMask = 0;
        if ((top.Properties & CellConfigProperties.DropsShadow) != 0)
        {
            solidMask |= 1;
        }

        if ((left.Properties & CellConfigProperties.DropsShadow) != 0)
        {
            solidMask |= 2;
        }

        if ((bottom.Properties & CellConfigProperties.DropsShadow) != 0)
        {
            solidMask |= 4;
        }

        if ((right.Properties & CellConfigProperties.DropsShadow) != 0)
        {
            solidMask |= 8;
        }

        if ((topLeft.Properties & CellConfigProperties.DropsShadow) != 0)
        {
            solidMask |= 16;
        }

        if ((topRight.Properties & CellConfigProperties.DropsShadow) != 0)
        {
            solidMask |= 32;
        }

        if ((bottomLeft.Properties & CellConfigProperties.DropsShadow) != 0)
        {
            solidMask |= 64;
        }

        if ((bottomRight.Properties & CellConfigProperties.DropsShadow) != 0)
        {
            solidMask |= 128;
        }

        return solidMask;
    }
}
