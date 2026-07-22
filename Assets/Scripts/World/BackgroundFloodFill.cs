using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;

// SA1503/SA1519: допустимо в hot-циклах (FBPW flood fill)
#pragma warning disable SA1503
#pragma warning disable SA1519

namespace Fodinae.Scripts.World
{
    /// <summary>
    /// Frontier-Based Parallel Wavefront (FBPW) flood fill for background map.
    ///
    /// Given a cell cache, computes a background map where passable cells propagate
    /// their type to neighbors via wavefront expansion. Fully isolated — no Unity
    /// dependencies except CellType.
    /// </summary>
    public sealed class BackgroundFloodFill
    {
        private int[] _fbpwGeneration;
        private int _fbpwCurrentGen = 1;
        private readonly List<(int X, int Y)> _fbpwFrontier = new(64);
        private readonly List<(int X, int Y)> _fbpwNextFrontier = new(64);
        private readonly object _fbpwLock = new();

        private CellType[,] _bgMapBuffer;
        private int _width;
        private int _height;

        public void Allocate(int width, int height)
        {
            if (_width == width && _height == height && _bgMapBuffer != null)
            {
                return;
            }

            _width = width;
            _height = height;
            _bgMapBuffer = new CellType[width, height];
            _fbpwGeneration = new int[width * height];
            _fbpwCurrentGen = 1;
        }

        public CellType[,] Buffer => _bgMapBuffer;

        /// <summary>
        /// Full rebuild: parallel scan + FBPW wavefront + safety sweep.
        /// </summary>
        public void ComputeFull(ICachedCellDataProvider cellCache)
        {
            int w = _width, h = _height;
            Array.Clear(_bgMapBuffer, 0, _bgMapBuffer.Length);

            var frontier = _fbpwFrontier;
            frontier.Clear();

            Parallel.For(0, w, x =>
            {
                var localFrontier = new List<(int, int)>(32);
                Span<TypeCount> typeCounts = stackalloc TypeCount[8];

                for (int y = 0; y < h; y++)
                {
                    int cx = x + 1, cy = y + 1;
                    var cell = cellCache.GetCell(cx, cy);

                    if ((cell.Properties & CellConfigProperties.Passable) != 0)
                    {
                        _bgMapBuffer[x, y] = cell.Type;
                        localFrontier.Add((x, y));
                    }
                    else
                    {
                        int distinctCount = 0;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int nx = x + dx, ny = y + dy;
                                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;

                                var n = cellCache.GetCell(nx + 1, ny + 1);
                                if ((n.Properties & CellConfigProperties.Passable) != 0)
                                {
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
                        }

                        if (distinctCount > 0)
                        {
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

                            _bgMapBuffer[x, y] = mostFrequent;
                            localFrontier.Add((x, y));
                        }
                    }
                }

                if (localFrontier.Count > 0)
                {
                    lock (_fbpwLock)
                    {
                        frontier.AddRange(localFrontier);
                    }
                }
            });

            FBPWPropagate(frontier, useParallel: true);

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    if (_bgMapBuffer[x, y] == CellType.Unloaded)
                    {
                        _bgMapBuffer[x, y] = CellType.Empty;
                    }
                }
            }
        }

        /// <summary>
        /// Incremental rebuild: scroll existing buffer, then process border only.
        /// </summary>
        public void ComputeIncremental(int dx, int dy, ICachedCellDataProvider cellCache)
        {
            int w = _width, h = _height;
            Scroll2DArray(_bgMapBuffer, w, h, dx, dy);

            int xStart = 0, xLen = 0, yStart = 0, yLen = 0;
            if (dx > 0)
            {
                xStart = w - dx;
                xLen = dx;
            }
            else if (dx < 0)
            {
                xStart = 0;
                xLen = -dx;
            }

            if (dy > 0)
            {
                yStart = h - dy;
                yLen = dy;
            }
            else if (dy < 0)
            {
                yStart = 0;
                yLen = -dy;
            }

            bool hasXBorder = xLen > 0;
            bool hasYBorder = yLen > 0;
            if (!hasXBorder && !hasYBorder) return;

            var frontier = _fbpwFrontier;
            frontier.Clear();

            if (hasXBorder)
            {
                for (int x = xStart; x < xStart + xLen; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        SeedBorderCell(x, y, cellCache, frontier);
                    }
                }
            }

            if (hasYBorder)
            {
                int x2Start = hasXBorder ? xStart + xLen : 0;
                for (int y = yStart; y < yStart + yLen; y++)
                {
                    for (int x = x2Start; x < w; x++)
                    {
                        SeedBorderCell(x, y, cellCache, frontier);
                    }
                }
            }

            FBPWPropagate(frontier, useParallel: false);

            if (hasXBorder)
            {
                for (int x = xStart; x < xStart + xLen; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        if (_bgMapBuffer[x, y] == CellType.Unloaded)
                        {
                            _bgMapBuffer[x, y] = CellType.Empty;
                        }
                    }
                }
            }

            if (hasYBorder)
            {
                int xSweepStart = hasXBorder ? xStart + xLen : 0;
                for (int y = yStart; y < yStart + yLen; y++)
                {
                    for (int x = xSweepStart; x < w; x++)
                    {
                        if (_bgMapBuffer[x, y] == CellType.Unloaded)
                        {
                            _bgMapBuffer[x, y] = CellType.Empty;
                        }
                    }
                }
            }
        }

        private void SeedBorderCell(int x, int y, ICachedCellDataProvider cellCache, List<(int, int)> frontier)
        {
            var cell = cellCache.GetCell(x + 1, y + 1);
            if ((cell.Properties & CellConfigProperties.Passable) != 0)
            {
                _bgMapBuffer[x, y] = cell.Type;
                lock (_fbpwLock)
                {
                    frontier.Add((x, y));
                }
            }
            else
            {
                Span<TypeCount> typeCounts = stackalloc TypeCount[8];
                int distinctCount = 0;
                int w = _width, h = _height;

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;

                        var n = cellCache.GetCell(nx + 1, ny + 1);
                        if ((n.Properties & CellConfigProperties.Passable) != 0)
                        {
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
                }

                if (distinctCount > 0)
                {
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

                    _bgMapBuffer[x, y] = mostFrequent;
                    lock (_fbpwLock)
                    {
                        frontier.Add((x, y));
                    }
                }
            }
        }

        private void FBPWPropagate(List<(int, int)> frontier, bool useParallel = false)
        {
            if (frontier.Count == 0) return;
            int w = _width, h = _height;

            while (frontier.Count > 0)
            {
                _fbpwNextFrontier.Clear();
                int gen = _fbpwCurrentGen++;

                if (_fbpwCurrentGen >= int.MaxValue - 1)
                {
                    Array.Clear(_fbpwGeneration, 0, _fbpwGeneration.Length);
                    _fbpwCurrentGen = 1;
                }

                if (useParallel)
                {
                    Parallel.For(0, frontier.Count,
                        () => new List<(int, int)>(16),
                        (i, state, local) =>
                        {
                            var (x, y) = frontier[i];
                            CellType bg = _bgMapBuffer[x, y];
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    if (dx == 0 && dy == 0) continue;
                                    int nx = x + dx, ny = y + dy;
                                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                                    if (_bgMapBuffer[nx, ny] != CellType.Unloaded) continue;
                                    int idx = nx + (ny * w);
                                    if (Interlocked.CompareExchange(ref _fbpwGeneration[idx], gen, gen - 1) != gen - 1) continue;
                                    _bgMapBuffer[nx, ny] = bg;
                                    local.Add((nx, ny));
                                }
                            }

                            return local;
                        },
                        local =>
                        {
                            if (local.Count > 0)
                            {
                                lock (_fbpwLock)
                                {
                                    _fbpwNextFrontier.AddRange(local);
                                }
                            }
                        });
                }
                else
                {
                    foreach (var (x, y) in frontier)
                    {
                        CellType bg = _bgMapBuffer[x, y];
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int nx = x + dx, ny = y + dy;
                                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                                if (_bgMapBuffer[nx, ny] != CellType.Unloaded) continue;
                                int idx = nx + (ny * w);
                                if (_fbpwGeneration[idx] >= gen) continue;
                                _fbpwGeneration[idx] = gen;
                                _bgMapBuffer[nx, ny] = bg;
                                _fbpwNextFrontier.Add((nx, ny));
                            }
                        }
                    }
                }

                var temp = frontier;
                frontier.Clear();
                frontier.AddRange(_fbpwNextFrontier);
                _fbpwNextFrontier.Clear();
            }
        }

        private static void Scroll2DArray<T>(T[,] array, int w, int h, int dx, int dy)
        {
            if (dx == 0 && dy == 0) return;
            if (dx > 0)
            {
                for (int x = 0; x < w - dx; x++)
                {
                    if (dy > 0)
                    {
                        for (int y = 0; y < h - dy; y++) array[x, y] = array[x + dx, y + dy];
                    }
                    else if (dy < 0)
                    {
                        for (int y = h - 1; y >= -dy; y--) array[x, y] = array[x + dx, y + dy];
                    }
                    else
                    {
                        for (int y = 0; y < h; y++) array[x, y] = array[x + dx, y];
                    }
                }
            }
            else if (dx < 0)
            {
                for (int x = w - 1; x >= -dx; x--)
                {
                    if (dy > 0)
                    {
                        for (int y = 0; y < h - dy; y++) array[x, y] = array[x + dx, y + dy];
                    }
                    else if (dy < 0)
                    {
                        for (int y = h - 1; y >= -dy; y--) array[x, y] = array[x + dx, y + dy];
                    }
                    else
                    {
                        for (int y = 0; y < h; y++) array[x, y] = array[x + dx, y];
                    }
                }
            }
            else
            {
                if (dy > 0)
                {
                    for (int y = 0; y < h - dy; y++)
                        for (int x = 0; x < w; x++) array[x, y] = array[x, y + dy];
                }
                else if (dy < 0)
                {
                    for (int y = h - 1; y >= -dy; y--)
                        for (int x = 0; x < w; x++) array[x, y] = array[x, y + dy];
                }
            }
        }

        private struct TypeCount
        {
            public CellType Type;
            public int Count;
        }
    }

    /// <summary>
    /// Interface used by BackgroundFloodFill to read cell data without coupling to the full
    /// SingleMeshTerrainRenderer cell cache.
    /// </summary>
    public struct CachedCellInfo
    {
        public CellType Type;
        public CellConfigProperties Properties;
    }

    public interface ICachedCellDataProvider
    {
        CachedCellInfo GetCell(int x, int y);
    }
}
