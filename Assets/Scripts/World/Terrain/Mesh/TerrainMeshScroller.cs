#nullable enable

using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Kern.World.Terrain;

internal static class TerrainMeshScroller
{
    /// <param name="buffer">Плоский буфер, разложенный по-столбцовому.</param>
    /// <param name="width">Ширина сетки в клетках.</param>
    /// <param name="height">Высота сетки в клетках.</param>
    /// <param name="elementsPerCell">Сколько элементов буфера приходится на клетку.</param>
    /// <param name="dx">Сдвиг окна по x в клетках.</param>
    /// <param name="dy">Сдвиг окна по y в клетках.</param>
    public static void Scroll<T>(
        T[] buffer,
        int width,
        int height,
        int elementsPerCell,
        int dx,
        int dy)
    {
        if (dx == 0 && dy == 0)
        {
            return;
        }

        int keptWidth = width - Math.Abs(dx);
        int keptHeight = height - Math.Abs(dy);
        if (keptWidth <= 0 || keptHeight <= 0)
        {
            return;
        }

        int sourceY = dy > 0 ? dy : 0;
        int targetY = dy > 0 ? 0 : -dy;
        int runLength = keptHeight * elementsPerCell;

        // Столбцы перекрываются, и порядок обхода решает, не затрёт ли
        // приёмник ещё не прочитанный источник: при сдвиге вправо приёмник
        // левее источника, поэтому идём слева направо, и наоборот.
        // Внутри столбца перекрытие безопасно само по себе: Array.Copy для
        // одного массива ведёт себя как memmove.
        if (dx >= 0)
        {
            int firstTargetX = 0;
            for (int x = firstTargetX; x < keptWidth; x++)
            {
                CopyColumn(buffer, x, x + dx, height, elementsPerCell, sourceY, targetY, runLength);
            }
        }
        else
        {
            int firstTargetX = -dx;
            for (int x = firstTargetX + keptWidth - 1; x >= firstTargetX; x--)
            {
                CopyColumn(buffer, x, x + dx, height, elementsPerCell, sourceY, targetY, runLength);
            }
        }
    }

    public static void GetBandExtents(int size, int delta, out int start, out int length)
    {
        if (delta > 0)
        {
            start = Mathf.Max(0, size - delta - 1);
            length = size - start;
        }
        else if (delta < 0)
        {
            start = 0;
            length = Mathf.Min(size, -delta + 1);
        }
        else
        {
            start = 0;
            length = 0;
        }
    }

    private static void CopyColumn<T>(
        T[] buffer,
        int targetX,
        int sourceX,
        int height,
        int elementsPerCell,
        int sourceY,
        int targetY,
        int runLength)
    {
        int sourceOffset = ((sourceX * height) + sourceY) * elementsPerCell;
        int targetOffset = ((targetX * height) + targetY) * elementsPerCell;
        Array.Copy(buffer, sourceOffset, buffer, targetOffset, runLength);
    }
}
