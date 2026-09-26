#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using Kern.World.Terrain;
using UnityEngine;

namespace Kern.World.Terrain.Background;
public sealed class BackgroundFloodFill
{
    // Full-window A/B on a 192×128 fixture used less process CPU with four
    // workers; wall-time throughput was similar to using all processors.
    private readonly ParallelOptions _fullPassParallelOptions;

    private int[] _fbpwGeneration = Array.Empty<int>();
    private int _fbpwCurrentGen = 1;
    private readonly List<(int X, int Y)> _fbpwFrontier = new(64);
    private readonly List<(int X, int Y)> _fbpwNextFrontier = new(64);

    private readonly TerrainRingGrid<CellType> _bgMapBuffer = new();
    private readonly TerrainRingGrid<CachedCellInfo> _sourceCells = new();
    private int _width;
    private int _height;

    // One frontier list per column, so the seed scan can run in parallel and
    // still produce the exact sequential frontier when concatenated in
    // column order. Allocated once per resize rather than per rebuild.
    private List<(int X, int Y)>[] _columnFrontiers = Array.Empty<List<(int X, int Y)>>();

    public BackgroundFloodFill()
        : this(Math.Min(4, Environment.ProcessorCount))
    {
    }

    internal BackgroundFloodFill(int maxDegreeOfParallelism)
    {
        _fullPassParallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism,
        };
    }

    public void Allocate(int width, int height)
    {
        if (_width == width && _height == height && _bgMapBuffer.IsAllocated)
        {
            return;
        }

        _width = width;
        _height = height;
        _bgMapBuffer.EnsureSize(width, height);
        _sourceCells.EnsureSize(width, height);
        _fbpwGeneration = new int[width * height];
        _fbpwCurrentGen = 1;
        _columnFrontiers = new List<(int X, int Y)>[width];
        for (int x = 0; x < width; x++)
        {
            _columnFrontiers[x] = new List<(int X, int Y)>(height);
        }
    }

    public TerrainRingGrid<CellType> Buffer => _bgMapBuffer;

    private static bool IsFloorCell(CellType type, CellConfigProperties properties)
    {
        return (properties & CellConfigProperties.Passable) != 0 &&
            type != CellType.Unloaded &&
            type != CellType.BuildingDoor;
    }

    public void ComputeFull(ICachedCellDataProvider cellCache)
    {
        int w = _width, h = _height;
        var frontier = _fbpwFrontier;
        frontier.Clear();

        Parallel.For(
            0,
            w,
            _fullPassParallelOptions,
            x =>
            {
                for (int y = 0; y < h; y++)
                {
                    _sourceCells[x, y] = cellCache.GetCell(x + 1, y + 1);
                }
            });

        // The seed scan writes only its own cell and appends to its own
        // column list, so it parallelises cleanly. A full populate remains a
        // fallback path; ordinary camera movement uses ComputeScrolled.
        //
        // Concatenating the column lists in x order reproduces the sequential
        // frontier exactly, which matters: FBPWPropagate fills each Unloaded
        // cell from whichever seed reaches it first, so a different frontier
        // order would be a different background map.
        Parallel.For(
            0,
            w,
            _fullPassParallelOptions,
            x =>
            {
                List<(int X, int Y)> columnFrontier = _columnFrontiers[x];
                columnFrontier.Clear();
                for (int y = 0; y < h; y++)
                {
                    SeedCell(x, y, columnFrontier);
                }
            });

        for (int x = 0; x < w; x++)
        {
            frontier.AddRange(_columnFrontiers[x]);
        }

        FBPWPropagate(frontier);
        ReplaceUnloadedWithEmpty(0, w, 0, h);
    }

    public void ComputeScrolled(int dx, int dy, ICachedCellDataProvider cellCache)
    {
        int w = _width;
        int h = _height;
        if (w <= 0 || h <= 0 || _bgMapBuffer == null)
        {
            return;
        }

        // Сдвиг больше окна не оставляет ничего годного для переноса.
        if (Math.Abs(dx) >= w || Math.Abs(dy) >= h)
        {
            ComputeFull(cellCache);
            return;
        }

        if (dx == 0 && dy == 0)
        {
            return;
        }

        _bgMapBuffer.Scroll(dx, dy);
        _sourceCells.Scroll(dx, dy);

        var frontier = _fbpwFrontier;
        frontier.Clear();

        // Полосы считает TerrainScrollBands: кайма по x во всю высоту, кайма
        // по y только на оставшейся ширине. Угол иначе был бы посеян дважды
        // и попал бы в волну двумя записями — это не лишняя работа, а другой
        // результат заливки.
        //
        // Кайма нулевая: сеется ровно то, что вошло. Клетки старой границы
        // пересевать не нужно — их значение уже разрешено, и ниже они входят
        // в волну как источники.
        TerrainScrollBands bands = TerrainScrollBands.Resolve(w, h, dx, dy);
        RectInt column = bands.ColumnBand;
        RectInt row = bands.RowBand;

        if (column.width > 0)
        {
            CacheRegion(column.xMin, column.yMin, column.width, column.height, cellCache);
            SeedBorderRegion(column.xMin, column.width, column.yMin, column.height, frontier);
        }

        if (row.width > 0 && row.height > 0)
        {
            CacheRegion(row.xMin, row.yMin, row.width, row.height, cellCache);
            SeedBorderRegion(row.xMin, row.width, row.yMin, row.height, frontier);
        }

        // Линия уже разрешённой внутренности вплотную к кайме — тоже
        // источник. Без неё кайма, идущая сквозь сплошную породу, не имела
        // бы во фронте ни одной клетки: у её клеток нет проходимого соседа,
        // а внутренность источником не была. Волна тогда не доходила вовсе,
        // и кайма целиком уходила в Empty — на каждом сдвиге по полосе,
        // пока фон не становился пустым по всему экрану.
        if (column.width > 0)
        {
            SeedResolvedColumn(dx > 0 ? column.xMin - 1 : column.xMax, 0, h, frontier);
        }

        if (row.width > 0 && row.height > 0)
        {
            SeedResolvedRow(dy > 0 ? row.yMin - 1 : row.yMax, row.xMin, row.width, frontier);
        }

        // Волна заливает только неразрешённые клетки каймы. Раньше она
        // перезаливала всю связную породу окна: 73% стоимости шага камеры, а
        // внутренность при этом перещёлкивалась на ничьих ~10% клеток.
        FBPWPropagate(frontier, onlyUnresolved: true);

        if (column.width > 0)
        {
            ReplaceUnloadedWithEmpty(column.xMin, column.width, column.yMin, column.height);
        }

        if (row.width > 0 && row.height > 0)
        {
            ReplaceUnloadedWithEmpty(row.xMin, row.width, row.yMin, row.height);
        }
    }

    public void UpdateLocalRegion(int startX, int startY, int countX, int countY, ICachedCellDataProvider cellCache)
    {
        int w = _width;
        int h = _height;
        int endX = Math.Min(startX + countX, w);
        int endY = Math.Min(startY + countY, h);
        int clampedStartX = Math.Max(0, startX);
        int clampedStartY = Math.Max(0, startY);

        CacheRegion(
            clampedStartX,
            clampedStartY,
            endX - clampedStartX,
            endY - clampedStartY,
            cellCache);

        for (int x = clampedStartX; x < endX; x++)
        {
            for (int y = clampedStartY; y < endY; y++)
            {
                CachedCellInfo cell = _sourceCells[x, y];

                if (IsFloorCell(cell.Type, cell.Properties))
                {
                    _bgMapBuffer[x, y] = cell.Type;
                }
                else
                {
                    CellType neighbor = FindMostFrequentPassableNeighbor(x, y, w, h);
                    _bgMapBuffer[x, y] = neighbor != CellType.Unloaded ? neighbor : CellType.Empty;
                }
            }
        }
    }
    private void SeedResolvedColumn(int x, int startY, int countY, List<(int, int)> frontier)
    {
        if (x < 0 || x >= _width)
        {
            return;
        }

        int endY = Math.Min(startY + countY, _height);
        for (int y = Math.Max(0, startY); y < endY; y++)
        {
            if (_bgMapBuffer[x, y] != CellType.Unloaded)
            {
                frontier.Add((x, y));
            }
        }
    }

    private void SeedResolvedRow(int y, int startX, int countX, List<(int, int)> frontier)
    {
        if (y < 0 || y >= _height)
        {
            return;
        }

        int endX = Math.Min(startX + countX, _width);
        for (int x = Math.Max(0, startX); x < endX; x++)
        {
            if (_bgMapBuffer[x, y] != CellType.Unloaded)
            {
                frontier.Add((x, y));
            }
        }
    }

    private void SeedBorderRegion(int startX, int countX, int startY, int countY, List<(int, int)> frontier)
    {
        for (int x = startX; x < startX + countX; x++)
        {
            for (int y = startY; y < startY + countY; y++)
            {
                SeedCell(x, y, frontier);
            }
        }
    }

    private void ReplaceUnloadedWithEmpty(int startX, int countX, int startY, int countY)
    {
        for (int x = startX; x < startX + countX; x++)
        {
            for (int y = startY; y < startY + countY; y++)
            {
                if (_bgMapBuffer[x, y] == CellType.Unloaded)
                {
                    _bgMapBuffer[x, y] = CellType.Empty;
                }
            }
        }
    }

    private void SeedCell(int x, int y, List<(int, int)> frontier)
    {
        CachedCellInfo cell = _sourceCells[x, y];
        if (IsFloorCell(cell.Type, cell.Properties))
        {
            _bgMapBuffer[x, y] = cell.Type;
            // A floor cell is already resolved and is never overwritten by
            // propagation. Only unresolved cells adjacent to a floor need a
            // seed; launching a wave from every floor cell repeats the same
            // eight-neighbour scan and made full populates unnecessarily
            // expensive.
        }
        else
        {
            CellType neighbor = FindMostFrequentPassableNeighbor(x, y, _width, _height);
            _bgMapBuffer[x, y] = neighbor;
            if (neighbor != CellType.Unloaded)
            {
                frontier.Add((x, y));
            }
        }
    }

    private CellType FindMostFrequentPassableNeighbor(
        int x,
        int y,
        int w,
        int h)
    {
        Span<TypeCount> typeCounts = stackalloc TypeCount[8];
        int distinctCount = 0;

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                int nx = x + dx;
                int ny = y + dy;
                if (nx < 0 || nx >= w || ny < 0 || ny >= h)
                {
                    continue;
                }

                CachedCellInfo n = _sourceCells[nx, ny];
                if (!IsFloorCell(n.Type, n.Properties))
                {
                    continue;
                }

                bool found = false;
                for (int i = 0; i < distinctCount; i++)
                {
                    if (typeCounts[i].Type == n.Type)
                    {
                        typeCounts[i].Count++;
                        found = true;
                        break;
                    }
                }

                if (!found && distinctCount < 8)
                {
                    typeCounts[distinctCount++] = new TypeCount { Type = n.Type, Count = 1 };
                }
            }
        }

        if (distinctCount == 0)
        {
            return CellType.Unloaded;
        }

        CellType mostFrequent = typeCounts[0].Type;
        int maxC = typeCounts[0].Count;
        for (int i = 1; i < distinctCount; i++)
        {
            if (typeCounts[i].Count > maxC)
            {
                maxC = typeCounts[i].Count;
                mostFrequent = typeCounts[i].Type;
            }
        }

        return mostFrequent;
    }

    private void FBPWPropagate(
        List<(int, int)> frontier,
        bool onlyUnresolved = false)
    {
        if (frontier.Count == 0)
        {
            return;
        }

        int w = _width, h = _height;
        var current = frontier;
        var next = _fbpwNextFrontier;

        // Одно поколение на весь вызов, а не на волну. С поколением на волну
        // клетка прошлой волны проходила проверку `>= gen` заново, и две соседние
        // незагруженные клетки переписывали друг друга бесконечно — кадр
        // зависал в LateUpdate. Затравка помечается сразу, чтобы волна не
        // возвращалась в неё.
        if (_fbpwCurrentGen >= int.MaxValue - 1)
        {
            Array.Clear(_fbpwGeneration, 0, _fbpwGeneration.Length);
            _fbpwCurrentGen = 1;
        }

        int gen = _fbpwCurrentGen++;
        for (int i = 0; i < current.Count; i++)
        {
            var (seedX, seedY) = current[i];
            _fbpwGeneration[seedX + (seedY * w)] = gen;
        }

        while (current.Count > 0)
        {
            next.Clear();
            int currentCount = current.Count;
            for (int i = 0; i < currentCount; i++)
            {
                var (x, y) = current[i];
                CellType bg = _bgMapBuffer[x, y];
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                        {
                            continue;
                        }

                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || nx >= w || ny < 0 || ny >= h)
                        {
                            continue;
                        }

                        CachedCellInfo n = _sourceCells[nx, ny];
                        if (IsFloorCell(n.Type, n.Properties))
                        {
                            continue;
                        }

                        int idx = nx + (ny * w);
                        if (_fbpwGeneration[idx] >= gen ||
                            (onlyUnresolved && _bgMapBuffer[nx, ny] != CellType.Unloaded))
                        {
                            continue;
                        }

                        _fbpwGeneration[idx] = gen;
                        _bgMapBuffer[nx, ny] = bg;
                        next.Add((nx, ny));
                    }
                }
            }

            current.Clear();
            var temp = current;
            current = next;
            next = temp;
        }
    }

    private void CacheRegion(
        int startX,
        int startY,
        int width,
        int height,
        ICachedCellDataProvider cellCache)
    {
        for (int x = startX; x < startX + width; x++)
        {
            for (int y = startY; y < startY + height; y++)
            {
                _sourceCells[x, y] = cellCache.GetCell(x + 1, y + 1);
            }
        }
    }

    private struct TypeCount
    {
        public CellType Type;
        public int Count;
    }
}
public struct CachedCellInfo
{
    public CellType Type;
    public CellConfigProperties Properties;
}

public interface ICachedCellDataProvider
{
    CachedCellInfo GetCell(int x, int y);
}
