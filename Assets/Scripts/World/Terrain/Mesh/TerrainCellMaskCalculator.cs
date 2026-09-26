#nullable enable

using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Kern.World.Terrain;

public sealed class TerrainCellMaskCalculator
{
    // A/B runs on a 192×128 window show lower aggregate process CPU with four
    // workers; wall-time throughput varies slightly against the full count.
    private static readonly System.Threading.Tasks.ParallelOptions _FullPassParallelOptions = new()
    {
        MaxDegreeOfParallelism = System.Math.Min(4, System.Environment.ProcessorCount),
    };

    public TerrainRingGrid<int> CellTilingDescriptors { get; } = new();

    public TerrainRingGrid<int> CellCornerVariants { get; } = new();

    public TerrainRingGrid<byte> CellReliefMasks { get; } = new();

    public TerrainRingGrid<byte> CellReliefCornerMasks { get; } = new();

    public TerrainRingGrid<byte> CellSolidBoundaryMasks { get; } = new();

    public void EnsureCapacity(int meshWidth, int meshHeight)
    {
        if (CellTilingDescriptors.Width != meshWidth || CellTilingDescriptors.Height != meshHeight)
        {
            CellTilingDescriptors.EnsureSize(meshWidth, meshHeight);
            CellCornerVariants.EnsureSize(meshWidth, meshHeight);
            CellReliefMasks.EnsureSize(meshWidth, meshHeight);
            CellReliefCornerMasks.EnsureSize(meshWidth, meshHeight);
            CellSolidBoundaryMasks.EnsureSize(meshWidth, meshHeight);
        }
    }

    public void PrecalculateFull(ITerrainCellDataSource cellCache, int meshWidth, int meshHeight)
    {
        EnsureCapacity(meshWidth, meshHeight);

        System.Threading.Tasks.Parallel.For(0, meshWidth, _FullPassParallelOptions, x =>
        {
            CalculateColumn(cellCache, x, 0, meshHeight);
        });
    }

    public void PrecalculateRegion(ITerrainCellDataSource cellCache, int meshWidth, int meshHeight, int startX, int startY, int countX, int countY)
    {
        int cxMin = Mathf.Clamp(startX, 0, meshWidth);
        int cxMax = Mathf.Clamp(startX + countX, 0, meshWidth);
        int cyMin = Mathf.Clamp(startY, 0, meshHeight);
        int cyMax = Mathf.Clamp(startY + countY, 0, meshHeight);

        for (int x = cxMin; x < cxMax; x++)
        {
            CalculateColumn(cellCache, x, cyMin, cyMax - cyMin);
        }
    }

    public void PrecalculateIncremental(ITerrainCellDataSource cellCache, int meshWidth, int meshHeight, int dx, int dy)
    {
        EnsureCapacity(meshWidth, meshHeight);

        CellTilingDescriptors.Scroll(dx, dy);
        CellCornerVariants.Scroll(dx, dy);
        CellReliefMasks.Scroll(dx, dy);
        CellReliefCornerMasks.Scroll(dx, dy);
        CellSolidBoundaryMasks.Scroll(dx, dy);

        // Кайма в одну клетку: маска клетки описывает её восемь соседей, и у
        // клетки на старой границе сосед снаружи только что появился.
        TerrainScrollBands bands = TerrainScrollBands.Resolve(
            meshWidth, meshHeight, dx, dy, neighbourMargin: 1);
        CalculateBand(cellCache, bands.ColumnBand);
        CalculateBand(cellCache, bands.RowBand);
    }

    private void CalculateBand(ITerrainCellDataSource cellCache, RectInt band)
    {
        for (int x = band.xMin; x < band.xMax; x++)
        {
            CalculateColumn(cellCache, x, band.yMin, band.height);
        }
    }

    public void CalculateCellNode(ITerrainCellDataSource cellCache, int x, int y)
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
        CalculateCellNode(x, y, data, top, left, bottom, right, topLeft, topRight, bottomLeft, bottomRight);
    }

    internal void CalculateColumn(ITerrainCellDataSource cellCache, int x, int startY, int countY)
    {
        if (countY <= 0)
        {
            return;
        }

        int cx = x + 1;
        int cy = startY + 1;
        CachedCellData bottomLeft = cellCache.GetCellData(cx - 1, cy - 1);
        CachedCellData bottom = cellCache.GetCellData(cx, cy - 1);
        CachedCellData bottomRight = cellCache.GetCellData(cx + 1, cy - 1);
        CachedCellData left = cellCache.GetCellData(cx - 1, cy);
        CachedCellData data = cellCache.GetCellData(cx, cy);
        CachedCellData right = cellCache.GetCellData(cx + 1, cy);
        CachedCellData topLeft = cellCache.GetCellData(cx - 1, cy + 1);
        CachedCellData top = cellCache.GetCellData(cx, cy + 1);
        CachedCellData topRight = cellCache.GetCellData(cx + 1, cy + 1);

        for (int row = 0; row < countY; row++)
        {
            int y = startY + row;
            CalculateCellNode(x, y, data, top, left, bottom, right, topLeft, topRight, bottomLeft, bottomRight);
            if (row + 1 == countY)
            {
                continue;
            }

            bottomLeft = left;
            bottom = data;
            bottomRight = right;
            left = topLeft;
            data = top;
            right = topRight;
            int nextTopY = cy + row + 2;
            topLeft = cellCache.GetCellData(cx - 1, nextTopY);
            top = cellCache.GetCellData(cx, nextTopY);
            topRight = cellCache.GetCellData(cx + 1, nextTopY);
        }
    }

    private void CalculateCellNode(
        int x,
        int y,
        in CachedCellData data,
        in CachedCellData top,
        in CachedCellData left,
        in CachedCellData bottom,
        in CachedCellData right,
        in CachedCellData topLeft,
        in CachedCellData topRight,
        in CachedCellData bottomLeft,
        in CachedCellData bottomRight)
    {
        CellTilingDescriptors[x, y] = CalculateTilingDescriptor(data, left, bottomLeft, bottom, bottomRight, right, topRight, top, topLeft);
        CellCornerVariants[x, y] = CalculateCornerSideMask(data, left, right, top, bottom);
        CalculateReliefMasks(
            data,
            top,
            left,
            bottom,
            right,
            topLeft,
            topRight,
            bottomLeft,
            bottomRight,
            out byte reliefMask,
            out byte reliefCornerMask);
        CellReliefMasks[x, y] = reliefMask;
        CellReliefCornerMasks[x, y] = reliefCornerMask;
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

    // Рельефная маска: бит стоит там, где сосед принадлежит той же рельефной
    // поверхности. Обычно это ненулевая серверная группа. Непрерывные листы
    // также объединяются по семейству; зелёный и синий кристаллы с пустоскалом
    // образуют собственное исключительное семейство и не сливаются с ними.
    //
    // Сравнение именно на равенство, а не «сосед не ниже». Кайма рисуется по
    // сторонам, где сосед чужой, и порядковое сравнение делало её
    // односторонней: кристалл (группа 3) рядом с неразрушимой породой
    // (группа 4) считал соседа своим и сливался с ним, а порода рядом с
    // кристаллом — чужим и обводилась. Шов получался у одной клетки из двух.
    // В оригинале сравнение равенством, и обе стороны обводят друг друга.
    public static byte CalculateReliefMask(
        CachedCellData data,
        CachedCellData top,
        CachedCellData left,
        CachedCellData bottom,
        CachedCellData right)
    {
        if (data.ReliefGroup == 0)
        {
            return 0;
        }

        byte rm = 0;
        if (SameReliefSurface(data, top))
        {
            rm |= 1;
        }

        if (SameReliefSurface(data, left))
        {
            rm |= 2;
        }

        if (SameReliefSurface(data, bottom))
        {
            rm |= 4;
        }

        if (SameReliefSurface(data, right))
        {
            rm |= 8;
        }

        return rm;
    }

    private static bool SameReliefSurface(in CachedCellData first, in CachedCellData second)
    {
        if (second.ReliefGroup == 0)
        {
            return false;
        }

        TerrainRimFamily firstFamily = TerrainReliefRimCatalog.GetFamily(first.Type);
        return SameReliefSurface(first, firstFamily, second);
    }

    private static bool SameReliefSurface(
        in CachedCellData first,
        TerrainRimFamily firstFamily,
        in CachedCellData second)
    {
        if (second.ReliefGroup == 0)
        {
            return false;
        }

        TerrainRimFamily secondFamily = TerrainReliefRimCatalog.GetFamily(second.Type);
        bool firstIsExclusiveFamily = firstFamily == TerrainRimFamily.GreenBlueRock;
        bool secondIsExclusiveFamily = secondFamily == TerrainRimFamily.GreenBlueRock;
        if (firstIsExclusiveFamily || secondIsExclusiveFamily)
        {
            return firstIsExclusiveFamily && secondIsExclusiveFamily;
        }

        if (first.ReliefGroup == second.ReliefGroup)
        {
            return true;
        }

        return firstFamily != TerrainRimFamily.None && firstFamily == secondFamily;
    }

    internal static void CalculateReliefMasks(
        in CachedCellData data,
        in CachedCellData top,
        in CachedCellData left,
        in CachedCellData bottom,
        in CachedCellData right,
        in CachedCellData topLeft,
        in CachedCellData topRight,
        in CachedCellData bottomLeft,
        in CachedCellData bottomRight,
        out byte reliefMask,
        out byte reliefCornerMask)
    {
        reliefMask = 0;
        reliefCornerMask = 0;
        if (data.ReliefGroup == 0)
        {
            return;
        }

        TerrainRimFamily family = TerrainReliefRimCatalog.GetFamily(data.Type);
        bool topSame = SameReliefSurface(data, family, top);
        bool leftSame = SameReliefSurface(data, family, left);
        bool bottomSame = SameReliefSurface(data, family, bottom);
        bool rightSame = SameReliefSurface(data, family, right);

        if (topSame)
        {
            reliefMask |= 1;
        }

        if (leftSame)
        {
            reliefMask |= 2;
        }

        if (bottomSame)
        {
            reliefMask |= 4;
        }

        if (rightSame)
        {
            reliefMask |= 8;
        }

        if (bottomSame && leftSame && !SameReliefSurface(data, family, bottomLeft))
        {
            reliefCornerMask |= 1 << 0;
        }

        if (bottomSame && rightSame && !SameReliefSurface(data, family, bottomRight))
        {
            reliefCornerMask |= 1 << 1;
        }

        if (topSame && rightSame && !SameReliefSurface(data, family, topRight))
        {
            reliefCornerMask |= 1 << 2;
        }

        if (topSame && leftSame && !SameReliefSurface(data, family, topLeft))
        {
            reliefCornerMask |= 1 << 3;
        }
    }

    // Вогнутый угол силуэта: обе кардинальные клетки принадлежат поверхности,
    // диагональная — нет. Одной маски сторон для такого шаблона недостаточно.
    public static byte CalculateReliefCornerMask(
        CachedCellData data,
        CachedCellData top,
        CachedCellData left,
        CachedCellData bottom,
        CachedCellData right,
        CachedCellData topLeft,
        CachedCellData topRight,
        CachedCellData bottomLeft,
        CachedCellData bottomRight)
    {
        if (data.ReliefGroup == 0)
        {
            return 0;
        }

        byte cornerMask = 0;
        if (SameReliefSurface(data, bottom) && SameReliefSurface(data, left) &&
            !SameReliefSurface(data, bottomLeft))
        {
            cornerMask |= 1 << 0;
        }

        if (SameReliefSurface(data, bottom) && SameReliefSurface(data, right) &&
            !SameReliefSurface(data, bottomRight))
        {
            cornerMask |= 1 << 1;
        }

        if (SameReliefSurface(data, top) && SameReliefSurface(data, right) &&
            !SameReliefSurface(data, topRight))
        {
            cornerMask |= 1 << 2;
        }

        if (SameReliefSurface(data, top) && SameReliefSurface(data, left) &&
            !SameReliefSurface(data, topLeft))
        {
            cornerMask |= 1 << 3;
        }

        return cornerMask;
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
