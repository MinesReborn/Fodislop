#nullable enable

using System;
using System.Collections.Generic;
using Kern.Diagnostics.Frames;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

internal readonly record struct TerrainVolumeEdge(int X, int Y, Vector2 Start, Vector2 End);

// Main-thread committed topology. Never reads the worker's mutable buffers
// between publications. Camera movement shifts already-built edges and
// rebuilds only the band that entered the window; a resize, a type-shape
// change or distortion still rebuilds everything.
internal sealed class TerrainVolumeTopology
{
    private static readonly Vector2[] s_corners = { new(0, 0), new(1, 0), new(1, 1), new(0, 1) };
    private static readonly int[] s_sideBits = { 4, 8, 1, 2 };
    private static readonly TerrainVolumeEdge[][] s_roundEdges = BuildRoundEdges();
    private readonly List<TerrainVolumeEdge> _edges = new();
    private readonly List<TerrainVolumeEdge> _cellEdgesScratch = new();
    private TerrainVolumeEdge[][] _edgesByCell = Array.Empty<TerrainVolumeEdge[]>();
    private bool[] _dirtyCells = Array.Empty<bool>();
    private Vector2[] _nodes = Array.Empty<Vector2>();
    private byte[] _snapshotCells = Array.Empty<byte>();
    private readonly uint[] _snapshotLooks = new uint[256];
    private Vector2Int _snapshotOrigin;
    private int _snapshotWidth;
    private int _snapshotHeight;
    private bool _snapshotDistortion;
    private bool _hasSnapshot;

    public IReadOnlyList<TerrainVolumeEdge> Edges => _edges;

    public ulong Version { get; private set; }

    internal int LastRebuiltCellCount { get; private set; }

    public void Publish(TerrainCellBuffers buffers, Vector2Int origin, int width, int height, bool distortion)
    {
        int scrollX = origin.x - _snapshotOrigin.x;
        int scrollY = origin.y - _snapshotOrigin.y;
        bool scrolled = CanScroll(scrollX, scrollY, width, height, distortion) &&
            ShiftForScroll(scrollX, scrollY, width, height, distortion);

        if (!CaptureShapeChanges(buffers, origin, width, height, distortion, scrolled))
        {
            return;
        }
        bool edgesChanged = !_hasSnapshot || origin != _snapshotOrigin ||
            width != _snapshotWidth || height != _snapshotHeight || scrolled;
        _hasSnapshot = false;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        int cellCount = checked(width * height);
        if (_edgesByCell.Length != cellCount)
        {
            _edgesByCell = new TerrainVolumeEdge[cellCount][];
        }
        int stride = width + 1;
        if (_nodes.Length != stride * (height + 1))
        {
            _nodes = new Vector2[stride * (height + 1)];
        }
        if (scrolled)
        {
            // Вошедшая полоса не имеет перенесённых рёбер: её клетки и их
            // соседи пересчитываются целиком, остальное остаётся на месте.
            MarkEnteringCellsDirty(scrollX, scrollY, width, height);
        }
        if (distortion)
        {
            for (int y = 0; y <= height; y++)
            {
                for (int x = 0; x <= width; x++)
                {
                    // Each dirty cell uses its four grid nodes. The dirty halo
                    // includes every cell sharing a changed source node.
                    if ((x < width && y < height && _dirtyCells[(y * width) + x]) ||
                        (x > 0 && y < height && _dirtyCells[(y * width) + x - 1]) ||
                        (x < width && y > 0 && _dirtyCells[((y - 1) * width) + x]) ||
                        (x > 0 && y > 0 && _dirtyCells[((y - 1) * width) + x - 1]))
                    {
                        int wx = origin.x + x;
                        int wy = origin.y + y;
                        TerrainVertexOffset offset = TerrainVertexDistortionCalculator.ComputeOffset(
                            Cell(buffers, wx - 1, wy), Cell(buffers, wx, wy),
                            Cell(buffers, wx - 1, wy - 1), Cell(buffers, wx, wy - 1), wx - 1, wy - 1);
                        _nodes[(y * stride) + x] = new Vector2(
                            Mathf.Round(offset.XSteps) / 32f, Mathf.Round(offset.YSteps) / 32f);
                    }
                }
            }
        }
        LastRebuiltCellCount = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int cellIndex = (y * width) + x;
                if (!_dirtyCells[cellIndex])
                {
                    continue;
                }
                LastRebuiltCellCount++;
                _cellEdgesScratch.Clear();
                int wx = origin.x + x;
                int wy = origin.y + y;
                CellType type = TerrainCellData.TypeOf(buffers.GetCell(wx, wy));
                if ((byte)type == 0 || !Solid(buffers, wx, wy))
                {
                    edgesChanged |= StoreCellEdges(cellIndex);
                    continue;
                }
                int occupied = (Solid(buffers, wx, wy + 1) ? 1 : 0) |
                    (Solid(buffers, wx - 1, wy) ? 2 : 0) |
                    (Solid(buffers, wx, wy - 1) ? 4 : 0) |
                    (Solid(buffers, wx + 1, wy) ? 8 : 0);
                if (occupied == 15)
                {
                    edgesChanged |= StoreCellEdges(cellIndex);
                    continue;
                }
                uint outline = (buffers.GetTypeRow(type).Look >> TerrainCellFormat.TypeOutlineShift) & 7u;
                if (outline == 2u)
                {
                    foreach (TerrainVolumeEdge edge in s_roundEdges[occupied])
                    {
                        _cellEdgesScratch.Add(new TerrainVolumeEdge(x, y, edge.Start, edge.End));
                    }
                    edgesChanged |= StoreCellEdges(cellIndex);
                    continue;
                }
                for (int side = 0; side < 4; side++)
                {
                    if ((occupied & s_sideBits[side]) != 0)
                    {
                        continue;
                    }
                    int next = (side + 1) % 4;
                    Vector2 a = s_corners[side];
                    Vector2 b = s_corners[next];
                    if (distortion && outline == 1u)
                    {
                        a += _nodes[((y + (int)a.y) * stride) + x + (int)a.x];
                        b += _nodes[((y + (int)b.y) * stride) + x + (int)b.x];
                    }
                    _cellEdgesScratch.Add(new TerrainVolumeEdge(x, y, a, b));
                }
                edgesChanged |= StoreCellEdges(cellIndex);
            }
        }
        if (edgesChanged)
        {
            _edges.Clear();
            for (int i = 0; i < _edgesByCell.Length; i++)
            {
                TerrainVolumeEdge[]? cellEdges = _edgesByCell[i];
                if (cellEdges != null)
                {
                    _edges.AddRange(cellEdges);
                }
            }
            Version++;
        }
        _snapshotOrigin = origin;
        _snapshotWidth = width;
        _snapshotHeight = height;
        _snapshotDistortion = distortion;
        _hasSnapshot = true;
        double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 /
            System.Diagnostics.Stopwatch.Frequency;
        FrameEventLog.Record($"объём террейна: публикация {Version}, клеток {LastRebuiltCellCount}/{width * height}, граней {_edges.Count}, {elapsedMs:F2} мс");
    }

    private bool StoreCellEdges(int index)
    {
        TerrainVolumeEdge[]? previous = _edgesByCell[index];
        if (previous != null && previous.Length == _cellEdgesScratch.Count)
        {
            bool equal = true;
            for (int i = 0; i < previous.Length; i++)
            {
                if (previous[i] != _cellEdgesScratch[i])
                {
                    equal = false;
                    break;
                }
            }
            if (equal)
            {
                return false;
            }
        }

        _edgesByCell[index] = _cellEdgesScratch.Count == 0
            ? Array.Empty<TerrainVolumeEdge>()
            : _cellEdgesScratch.ToArray();
        return true;
    }

    private bool CaptureShapeChanges(
        TerrainCellBuffers buffers,
        Vector2Int origin,
        int width,
        int height,
        bool distortion,
        bool scrolled)
    {
        bool originChanged = origin != _snapshotOrigin;
        bool layoutChanged = !_hasSnapshot || width != _snapshotWidth || height != _snapshotHeight ||
            distortion != _snapshotDistortion;
        int cellCount = checked(width * height);
        if (_dirtyCells.Length != cellCount)
        {
            _dirtyCells = new bool[cellCount];
            layoutChanged = true;
        }
        else
        {
            Array.Clear(_dirtyCells, 0, _dirtyCells.Length);
        }
        int count = checked((width + 2) * (height + 2));
        if (_snapshotCells.Length != count)
        {
            _snapshotCells = new byte[count];
            layoutChanged = true;
        }
        bool typeShapesChanged = false;
        for (int type = 0; type < 256; type++)
        {
            uint shape = buffers.GetTypeRow((CellType)type).Look &
                (TerrainCellFormat.TypeBackground | (7u << TerrainCellFormat.TypeOutlineShift));
            typeShapesChanged |= _snapshotLooks[type] != shape;
            _snapshotLooks[type] = shape;
        }
        // При сдвиге окна рёбра и снимок уже перенесены: смена origin сама по
        // себе полный пересчёт больше не требует.
        bool fullRefresh = layoutChanged || typeShapesChanged || (originChanged && !scrolled);
        bool changed = layoutChanged || typeShapesChanged || originChanged;
        int index = 0;
        for (int y = -1; y <= height; y++)
        {
            for (int x = -1; x <= width; x++)
            {
                byte type = buffers.GetCell(origin.x + x, origin.y + y).Bits;
                if (_snapshotCells[index] != type)
                {
                    changed = true;
                    for (int dy = -1; !fullRefresh && dy <= 1; dy++)
                    {
                        int dirtyY = y + dy;
                        if (dirtyY < 0 || dirtyY >= height)
                        {
                            continue;
                        }
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int dirtyX = x + dx;
                            if (dirtyX >= 0 && dirtyX < width)
                            {
                                _dirtyCells[(dirtyY * width) + dirtyX] = true;
                            }
                        }
                    }
                }
                _snapshotCells[index++] = type;
            }
        }
        if (fullRefresh)
        {
            Array.Fill(_dirtyCells, true);
        }
        return changed;
    }

    private bool CanScroll(int dx, int dy, int width, int height, bool distortion) =>
        _hasSnapshot &&
        distortion == _snapshotDistortion &&
        width == _snapshotWidth &&
        height == _snapshotHeight &&
        (dx != 0 || dy != 0) &&
        Math.Abs(dx) < width &&
        Math.Abs(dy) < height &&
        _edgesByCell.Length == width * height &&
        _snapshotCells.Length == (width + 2) * (height + 2) &&
        (!distortion || _nodes.Length == (width + 1) * (height + 1));

    // Переносит уже посчитанные рёбра и снимок клеток на новое начало окна.
    // Локальная координата клетки — это world - origin, поэтому при сдвиге
    // origin на (dx, dy) координаты рёбер уменьшаются на ту же величину.
    private bool ShiftForScroll(int dx, int dy, int width, int height, bool distortion)
    {
        int cellCount = width * height;
        var shiftedEdges = new TerrainVolumeEdge[cellCount][];
        for (int y = 0; y < height; y++)
        {
            int sourceY = y + dy;
            if (sourceY < 0 || sourceY >= height)
            {
                continue;
            }
            for (int x = 0; x < width; x++)
            {
                int sourceX = x + dx;
                if (sourceX < 0 || sourceX >= width)
                {
                    continue;
                }
                TerrainVolumeEdge[]? source = _edgesByCell[(sourceY * width) + sourceX];
                if (source == null || source.Length == 0)
                {
                    shiftedEdges[(y * width) + x] = Array.Empty<TerrainVolumeEdge>();
                    continue;
                }
                var copy = new TerrainVolumeEdge[source.Length];
                for (int i = 0; i < source.Length; i++)
                {
                    TerrainVolumeEdge edge = source[i];
                    copy[i] = new TerrainVolumeEdge(edge.X - dx, edge.Y - dy, edge.Start, edge.End);
                }
                shiftedEdges[(y * width) + x] = copy;
            }
        }
        int stride = width + 2;
        var shiftedSnapshot = new byte[_snapshotCells.Length];
        Array.Fill(shiftedSnapshot, (byte)0xFF);
        for (int y = -1; y <= height; y++)
        {
            int sourceY = y + dy;
            if (sourceY < -1 || sourceY > height)
            {
                continue;
            }
            for (int x = -1; x <= width; x++)
            {
                int sourceX = x + dx;
                if (sourceX < -1 || sourceX > width)
                {
                    continue;
                }
                shiftedSnapshot[((y + 1) * stride) + x + 1] =
                    _snapshotCells[((sourceY + 1) * stride) + sourceX + 1];
            }
        }
        if (distortion)
        {
            // Смещения узлов зависят от четырёх клеток вокруг узла и едут
            // вместе с содержимым. Переносим их на новые локальные адреса;
            // узлы вошедшей полосы досчитает обычный проход по dirty-клеткам.
            int nodeStride = width + 1;
            var shiftedNodes = new Vector2[_nodes.Length];
            for (int y = 0; y <= height; y++)
            {
                int sourceY = y + dy;
                if (sourceY < 0 || sourceY > height)
                {
                    continue;
                }
                for (int x = 0; x <= width; x++)
                {
                    int sourceX = x + dx;
                    if (sourceX < 0 || sourceX > width)
                    {
                        continue;
                    }
                    shiftedNodes[(y * nodeStride) + x] = _nodes[(sourceY * nodeStride) + sourceX];
                }
            }
            _nodes = shiftedNodes;
        }
        _edgesByCell = shiftedEdges;
        _snapshotCells = shiftedSnapshot;
        return true;
    }

    private void MarkEnteringCellsDirty(int dx, int dy, int width, int height)
    {
        if (dx != 0)
        {
            int x0 = dx > 0 ? width - dx : 0;
            int x1 = dx > 0 ? width - 1 : -dx - 1;
            MarkDirtyRect(x0 - 1, 0, x1 + 1, height - 1, width, height);
        }
        if (dy != 0)
        {
            int y0 = dy > 0 ? height - dy : 0;
            int y1 = dy > 0 ? height - 1 : -dy - 1;
            MarkDirtyRect(0, y0 - 1, width - 1, y1 + 1, width, height);
        }
    }

    private void MarkDirtyRect(int x0, int y0, int x1, int y1, int width, int height)
    {
        x0 = Math.Max(0, x0);
        y0 = Math.Max(0, y0);
        x1 = Math.Min(width - 1, x1);
        y1 = Math.Min(height - 1, y1);
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                _dirtyCells[(y * width) + x] = true;
            }
        }
    }

    private static bool Solid(TerrainCellBuffers buffers, int x, int y) =>
        (buffers.GetTypeRow(TerrainCellData.TypeOf(buffers.GetCell(x, y))).Look & TerrainCellFormat.TypeBackground) == 0;

    private static CachedCellData Cell(TerrainCellBuffers buffers, int x, int y) => new()
    {
        Outline = (CellOutline)((buffers.GetTypeRow(TerrainCellData.TypeOf(buffers.GetCell(x, y))).Look >>
            TerrainCellFormat.TypeOutlineShift) & 7u),
    };

    private static TerrainVolumeEdge[][] BuildRoundEdges()
    {
        var result = new TerrainVolumeEdge[16][];
        for (int mask = 0; mask < 16; mask++)
        {
            var edges = new List<TerrainVolumeEdge>();
            for (int side = 0; side < 4; side++)
            {
                bool horizontal = side == 0 || side == 2;
                for (int line = 0; line < 32; line++)
                {
                    int runStart = -1;
                    for (int step = 0; step <= 32; step++)
                    {
                        int x = horizontal ? step : line;
                        int y = horizontal ? line : step;
                        bool exposed = step < 32 && RoundEdgeExposed(x, y, side, mask);
                        if (exposed && runStart < 0)
                        {
                            runStart = step;
                        }
                        if (exposed || runStart < 0)
                        {
                            continue;
                        }
                        float fixedPosition = (line + (side == 1 || side == 2 ? 1 : 0)) / 32f;
                        Vector2 start = horizontal
                            ? new Vector2(runStart / 32f, fixedPosition)
                            : new Vector2(fixedPosition, runStart / 32f);
                        Vector2 end = horizontal
                            ? new Vector2(step / 32f, fixedPosition)
                            : new Vector2(fixedPosition, step / 32f);
                        edges.Add(side == 2 || side == 3
                            ? new TerrainVolumeEdge(0, 0, end, start)
                            : new TerrainVolumeEdge(0, 0, start, end));
                        runStart = -1;
                    }
                }
            }
            result[mask] = edges.ToArray();
        }
        return result;
    }

    private static bool RoundEdgeExposed(int x, int y, int side, int mask)
    {
        if (!RoundPixel(x, y, mask))
        {
            return false;
        }
        int nx = x + (side == 1 ? 1 : side == 3 ? -1 : 0);
        int ny = y + (side == 2 ? 1 : side == 0 ? -1 : 0);
        bool boundary = nx < 0 || nx >= 32 || ny < 0 || ny >= 32;
        return boundary ? (mask & s_sideBits[side]) == 0 : !RoundPixel(nx, ny, mask);
    }

    internal static bool RoundPixel(int x, int y, int mask)
    {
        float px = (x + 0.5f) / 32f - 0.5f;
        float py = (y + 0.5f) / 32f - 0.5f;
        int quadrant = (px >= 0 ? 8 : 2) | (py >= 0 ? 1 : 4);
        return (px * px) + (py * py) <= TerrainConfigHolder.RoundableCornerRadiusCells * TerrainConfigHolder.RoundableCornerRadiusCells ||
            (mask & quadrant) != 0 || MathF.Abs(MathF.Abs(px) - MathF.Abs(py)) >= 0.4f;
    }
}
