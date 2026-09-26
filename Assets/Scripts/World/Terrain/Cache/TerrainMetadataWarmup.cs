#nullable enable

using System;
using System.Collections.Generic;
using MinesServer.Data;

namespace Kern.World.Terrain;

// Проверка готовности метаданных перед сборкой клеток.
//
// ЗАЧЕМ. Полная сборка идёт через Parallel.For, а разрешение типа клетки —
// операция главного потока: она читает конфиг мира, пишет большую структуру в
// общий массив и дозаказывает недостающую текстуру. Пока это делалось прямо из
// FillCell, результат полной сборки зависел от того, какой поток успел первым.
//
// Живые map/texture services разрешают захваченные типы в Prepare на главном
// потоке. Здесь проверяется полнота этого снимка до Parallel.For; сама сборка
// после этой точки только читает метаданные.
//
// ЧТО ИМЕННО ПРОВЕРЯЕТСЯ. Передний план берёт метаданные прямо из клетки кэша и
// ни о чём не спрашивает. Фоновый слой использует типы из карты заливки и их
// одноклеточное соседство для tile-group descriptor, плюс две подстановки:
// Road под проходимым блоком здания и Empty под пустой клеткой.
public sealed class TerrainMetadataWarmup
{
    private readonly HashSet<CellType> _types = [];

    public void WarmRect(
        in TerrainCellSources sources,
        int startX,
        int endX,
        int startY,
        int endY)
    {
        _types.Clear();
        _types.Add(CellType.Road);
        _types.Add(CellType.Empty);
        TerrainRingGrid<CellType> background = sources.FloodFill.Buffer;
        int warmStartX = Math.Max(0, startX - 1);
        int warmEndX = Math.Min(background.Width, endX + 1);
        int warmStartY = Math.Max(0, startY - 1);
        int warmEndY = Math.Min(background.Height, endY + 1);
        for (int x = warmStartX; x < warmEndX; x++)
        {
            for (int y = warmStartY; y < warmEndY; y++)
            {
                _types.Add(background[x, y]);
            }
        }

        Verify(sources);
    }

    // Фоновая сборка: разрешать нечем, типы обязаны быть разрешены главным
    // потоком до старта. Промах — дефект подготовки, и он называется здесь,
    // до первой клетки, а не посреди Parallel.For.
    private void Verify(in TerrainCellSources sources)
    {
        foreach (CellType type in _types)
        {
            if (type != CellType.Unloaded && !sources.MetadataLookup.TryGet(type, out _))
            {
                throw new System.InvalidOperationException(
                    $"Terrain metadata for cell type '{type}' was not resolved before the background build.");
            }
        }
    }
}
