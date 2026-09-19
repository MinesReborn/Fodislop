#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kern.World.Terrain;

// Изменённые тексели текстур клетки с прошлой выгрузки.
//
// Несколько прямоугольников, а не один общий: при диагональном шаге полоса
// по x занимает всю высоту, по y — всю ширину, и их объединение было бы всей
// текстурой на каждом таком шаге. Прямоугольники приходят в кольцевых
// координатах клеток и режутся на шве кольца. Отдельный тип без Texture2D —
// чтобы учёт мерился и проверялся вне Unity.
public sealed class TerrainDirtyRegion
{
    private readonly List<RectInt> _rects = new(4);
    private int _cellWidth;
    private int _cellHeight;

    public int Count => _rects.Count;

    public bool IsAll { get; private set; }

    public bool IsEmpty => !IsAll && Count == 0;

    public RectInt this[int index] => _rects[index];

    // Площадь в текселях (по две строки на клетку).
    public long Area
    {
        get
        {
            long area = 0;
            for (int i = 0; i < Count; i++)
            {
                area += (long)_rects[i].width * _rects[i].height;
            }

            return area;
        }
    }

    public void Reset(int cellWidth, int cellHeight)
    {
        _cellWidth = cellWidth;
        _cellHeight = cellHeight;
        MarkAll();
    }

    public void MarkAll()
    {
        IsAll = true;
        _rects.Clear();
    }

    public void Clear()
    {
        IsAll = false;
        _rects.Clear();
    }

    public void MarkCells(int ringX, int ringY, int width, int height)
    {
        if (IsAll || width <= 0 || height <= 0 || _cellWidth <= 0 || _cellHeight <= 0)
        {
            return;
        }

        width = Math.Min(width, _cellWidth);
        height = Math.Min(height, _cellHeight);
        MarkRect(ringX, ringY, width, height, TerrainCellDataPacker.LayersPerCell);
    }

    public void MarkRect(int ringX, int ringY, int width, int height)
    {
        MarkRect(ringX, ringY, width, height, 1);
    }

    private void MarkRect(int ringX, int ringY, int width, int height, int layersPerCell)
    {
        if (IsAll || width <= 0 || height <= 0 || _cellWidth <= 0 || _cellHeight <= 0)
        {
            return;
        }

        width = Math.Min(width, _cellWidth);
        height = Math.Min(height, _cellHeight);
        int rightPart = ringX + width - _cellWidth;
        int topPart = ringY + height - _cellHeight;
        int leftWidth = width - Math.Max(0, rightPart);
        int bottomHeight = height - Math.Max(0, topPart);

        AddRect(ringX, ringY, leftWidth, bottomHeight, layersPerCell);
        if (rightPart > 0)
        {
            AddRect(0, ringY, rightPart, bottomHeight, layersPerCell);
        }

        if (topPart > 0)
        {
            AddRect(ringX, 0, leftWidth, topPart, layersPerCell);
            if (rightPart > 0)
            {
                AddRect(0, 0, rightPart, topPart, layersPerCell);
            }
        }
    }

    private void AddRect(int x, int y, int width, int height, int layersPerCell)
    {
        var rect = new RectInt(
            x,
            y * layersPerCell,
            width,
            height * layersPerCell);
        // Сливается только то, что не раздувает площадь: перекрытие или стык
        // по целой стороне. Раньше сливалось всё соприкасающееся, и полосы x
        // и y диагонального шага, касаясь в углу, давали прямоугольник во всю
        // текстуру — бенчмарк показал 100%.
        for (int i = 0; i < Count; i++)
        {
            if (Waste(_rects[i], rect) <= 0)
            {
                _rects[i] = Union(_rects[i], rect);
                return;
            }
        }

        _rects.Add(rect);
    }

    private static long RectArea(RectInt rect) => (long)rect.width * rect.height;

    // Площадь объединения сверх суммы площадей; для перекрытия может быть
    // отрицательной — тогда слияние только выгоднее.
    private static long Waste(RectInt a, RectInt b) => RectArea(Union(a, b)) - RectArea(a) - RectArea(b);

    private static RectInt Union(RectInt a, RectInt b)
    {
        int xMin = Math.Min(a.xMin, b.xMin);
        int yMin = Math.Min(a.yMin, b.yMin);
        return new RectInt(xMin, yMin, Math.Max(a.xMax, b.xMax) - xMin, Math.Max(a.yMax, b.yMax) - yMin);
    }
}
