#nullable enable

using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;

namespace Kern.World.Terrain;

// Правила соседства клетки: автотайл, углы стены пака, кайма и соседи-
// блоки. Шейдер выводит то же из буфера клеток (TerrainCellData.hlsl);
// здесь — CPU-сторона для вершин накладки дверей и эталона тестов.
public static class TerrainCellMaskCalculator
{
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
        if (left.HasTileGroup && left.TileGroupId == data.TileGroupId)
        {
            m |= 1 << 0;
        }

        if (bottomLeft.HasTileGroup && bottomLeft.TileGroupId == data.TileGroupId)
        {
            m |= 1 << 1;
        }

        if (bottom.HasTileGroup && bottom.TileGroupId == data.TileGroupId)
        {
            m |= 1 << 2;
        }

        if (bottomRight.HasTileGroup && bottomRight.TileGroupId == data.TileGroupId)
        {
            m |= 1 << 3;
        }

        if (right.HasTileGroup && right.TileGroupId == data.TileGroupId)
        {
            m |= 1 << 4;
        }

        if (topRight.HasTileGroup && topRight.TileGroupId == data.TileGroupId)
        {
            m |= 1 << 5;
        }

        if (top.HasTileGroup && top.TileGroupId == data.TileGroupId)
        {
            m |= 1 << 6;
        }

        if (topLeft.HasTileGroup && topLeft.TileGroupId == data.TileGroupId)
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
        if (!data.IsBackground && BlockRegistry.Get(data.Type).Outline == CellOutline.Wall)
        {
            if (!left.IsBackground && BlockRegistry.Get(left.Type).Outline == CellOutline.Corner)
            {
                cornerSideMask |= 1;
            }

            if (!right.IsBackground && BlockRegistry.Get(right.Type).Outline == CellOutline.Corner)
            {
                cornerSideMask |= 2;
            }

            if (!top.IsBackground && BlockRegistry.Get(top.Type).Outline == CellOutline.Corner)
            {
                cornerSideMask |= 4;
            }

            if (!bottom.IsBackground && BlockRegistry.Get(bottom.Type).Outline == CellOutline.Corner)
            {
                cornerSideMask |= 8;
            }
        }

        return cornerSideMask;
    }

    // Маска каймы: бит стоит там, где сосед принадлежит той же
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
    public static byte CalculateRimMask(
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
        if (SameRimSurface(data, top))
        {
            rm |= 1;
        }

        if (SameRimSurface(data, left))
        {
            rm |= 2;
        }

        if (SameRimSurface(data, bottom))
        {
            rm |= 4;
        }

        if (SameRimSurface(data, right))
        {
            rm |= 8;
        }

        return rm;
    }

    // Соседи одной ненулевой массы — одно тело без каймы; масса 0 каймы не имеет.
    private static bool SameRimSurface(in CachedCellData first, in CachedCellData second) =>
        !second.IsBackground && second.ReliefGroup != 0 && first.ReliefGroup == second.ReliefGroup;

    internal static void CalculateRimMasks(
        in CachedCellData data,
        in CachedCellData top,
        in CachedCellData left,
        in CachedCellData bottom,
        in CachedCellData right,
        in CachedCellData topLeft,
        in CachedCellData topRight,
        in CachedCellData bottomLeft,
        in CachedCellData bottomRight,
        out byte rimMask,
        out byte rimCornerMask)
    {
        rimMask = 0;
        rimCornerMask = 0;
        if (data.ReliefGroup == 0)
        {
            return;
        }

        bool topSame = SameRimSurface(data, top);
        bool leftSame = SameRimSurface(data, left);
        bool bottomSame = SameRimSurface(data, bottom);
        bool rightSame = SameRimSurface(data, right);

        if (topSame)
        {
            rimMask |= 1;
        }

        if (leftSame)
        {
            rimMask |= 2;
        }

        if (bottomSame)
        {
            rimMask |= 4;
        }

        if (rightSame)
        {
            rimMask |= 8;
        }

        rimCornerMask = CalculateRimCornerMask(
            data,
            top,
            left,
            bottom,
            right,
            topLeft,
            topRight,
            bottomLeft,
            bottomRight);
    }

    // Вогнутый угол силуэта: обе кардинальные клетки принадлежат поверхности,
    // диагональная — нет. Одной маски сторон для такого шаблона недостаточно.
    public static byte CalculateRimCornerMask(
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
        if (SameRimSurface(data, bottom) && SameRimSurface(data, left) &&
            !SameRimSurface(data, bottomLeft))
        {
            cornerMask |= 1 << 0;
        }

        if (SameRimSurface(data, bottom) && SameRimSurface(data, right) &&
            !SameRimSurface(data, bottomRight))
        {
            cornerMask |= 1 << 1;
        }

        if (SameRimSurface(data, top) && SameRimSurface(data, right) &&
            !SameRimSurface(data, topRight))
        {
            cornerMask |= 1 << 2;
        }

        if (SameRimSurface(data, top) && SameRimSurface(data, left) &&
            !SameRimSurface(data, topLeft))
        {
            cornerMask |= 1 << 3;
        }

        return cornerMask;
    }

    public static byte CalculateForegroundSidesMask(
        CachedCellData top,
        CachedCellData left,
        CachedCellData bottom,
        CachedCellData right)
    {
        byte foregroundSides = 0;
        if (CastsShadow(top))
        {
            foregroundSides |= 1;
        }

        if (CastsShadow(left))
        {
            foregroundSides |= 2;
        }

        if (CastsShadow(bottom))
        {
            foregroundSides |= 4;
        }

        if (CastsShadow(right))
        {
            foregroundSides |= 8;
        }

        return foregroundSides;
    }

    // Тень отбрасывает блок, в том числе незагруженная клетка.
    private static bool CastsShadow(in CachedCellData cell) =>
        BlockRegistry.Get(cell.Type).DrawLayer == CellDrawLayer.Foreground;
}
