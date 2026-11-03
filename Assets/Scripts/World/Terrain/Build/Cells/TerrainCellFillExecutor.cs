#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kern.Core.Interfaces;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain
{
    /// <summary>Owns cell quad generation, worker scratch, texture indexing, and fill timing.</summary>
    internal sealed class TerrainCellFillExecutor
    {
        private sealed class FillState
        {
            public long QuadTicks;
            public long PackTicks;
            public bool DoorsTouched;
        }

        private readonly TerrainCellBuffers _buffers;
        private readonly TerrainDoorOverlayIndex _doors;
        private int _width;
        private int _height;
        private float _cellSize;
        private long _quadTicks;
        private long _packTicks;
        private int _lastFullBuildAnchoredForegroundCellCount;

        public TerrainCellFillExecutor(
            TerrainCellBuffers buffers,
            TerrainDoorOverlayIndex doors)
        {
            _buffers = buffers;
            _doors = doors;
        }

        public float LastFillMs { get; private set; }

        public int LastFilledCells { get; private set; }

        public float LastQuadMs { get; private set; }

        public float LastPackMs { get; private set; }

        public int LastFullBuildAnchoredForegroundCellCount =>
            Volatile.Read(ref _lastFullBuildAnchoredForegroundCellCount);

        public void Configure(int width, int height, float cellSize)
        {
            _width = width;
            _height = height;
            _cellSize = cellSize;
        }

        public void ResetStageTimings()
        {
            LastFillMs = 0f;
            LastFilledCells = 0;
            LastQuadMs = 0f;
            LastPackMs = 0f;
        }

        public void FillFull(
            TerrainCellSources sources,
            int minX,
            int minY,
            CancellationToken cancellationToken)
        {
            Volatile.Write(ref _lastFullBuildAnchoredForegroundCellCount, 0);
            long fillStart = System.Diagnostics.Stopwatch.GetTimestamp();
            _quadTicks = 0;
            _packTicks = 0;
            Parallel.For(
                0,
                _width,
                static () => new FillState(),
                (x, _, state) =>
                {
                    for (int y = 0; y < _height; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        FillCell(
                            x,
                            y,
                            minX,
                            minY,
                            sources,
                            out long quadTicks,
                            out long packTicks,
                            countAnchoredForeground: true);
                        state.QuadTicks += quadTicks;
                        state.PackTicks += packTicks;
                    }

                    return state;
                },
                state =>
                {
                    Interlocked.Add(ref _quadTicks, state.QuadTicks);
                    Interlocked.Add(ref _packTicks, state.PackTicks);
                });
            LastFillMs = ElapsedMs(fillStart);
            LastQuadMs = TicksToMs(_quadTicks);
            LastPackMs = TicksToMs(_packTicks);
            LastFilledCells = _width * _height;
        }

        public bool FillRect(
            int startX,
            int endX,
            int startY,
            int endY,
            int minX,
            int minY,
            TerrainCellSources sources)
        {
            if (endX <= startX || endY <= startY)
            {
                return false;
            }

            long fillStart = System.Diagnostics.Stopwatch.GetTimestamp();
            _quadTicks = 0;
            _packTicks = 0;
            int doorsTouched = 0;

            // Мелкая область (заплатка, узкая полоса) идёт на этом же потоке:
            // Parallel.For ждёт запущенные реплики, и при занятом пуле девять
            // клеток ждали свободного потока десятки миллисекунд.
            const int ParallelFillMinimumCells = 4096;
            if ((long)(endX - startX) * (endY - startY) < ParallelFillMinimumCells)
            {
                bool touched = false;
                for (int x = startX; x < endX; x++)
                {
                    for (int y = startY; y < endY; y++)
                    {
                        touched |= FillCell(
                            x,
                            y,
                            minX,
                            minY,
                            sources,
                            out long quadTicks,
                            out long packTicks);
                        _quadTicks += quadTicks;
                        _packTicks += packTicks;
                    }
                }

                doorsTouched = touched ? 1 : 0;
            }
            else
            {
                Parallel.For(
                    startX,
                    endX,
                    () => new FillState(),
                    (x, _, state) =>
                    {
                        for (int y = startY; y < endY; y++)
                        {
                            state.DoorsTouched |= FillCell(
                                x,
                                y,
                                minX,
                                minY,
                                sources,
                                out long quadTicks,
                                out long packTicks);
                            state.QuadTicks += quadTicks;
                            state.PackTicks += packTicks;
                        }

                        return state;
                    },
                    state =>
                    {
                        if (state.DoorsTouched)
                        {
                            Interlocked.Exchange(ref doorsTouched, 1);
                        }

                        Interlocked.Add(ref _quadTicks, state.QuadTicks);
                        Interlocked.Add(ref _packTicks, state.PackTicks);
                    });
            }

            LastQuadMs += TicksToMs(_quadTicks);
            LastPackMs += TicksToMs(_packTicks);

            LastFillMs += ElapsedMs(fillStart);
            LastFilledCells += (endX - startX) * (endY - startY);

            _buffers.MarkCells(
                minX + startX,
                minY + startY,
                endX - startX,
                endY - startY);
            return doorsTouched != 0;
        }

        private bool FillCell(
            int x,
            int y,
            int minX,
            int minY,
            TerrainCellSources sources,
            out long quadTicks,
            out long packTicks,
            bool countAnchoredForeground = false)
        {
            int gridX = minX + x;
            int unityY = minY + y;

            // Классификация клетки: дверь ли она (накладка) и смещена ли.
            long quadStart = System.Diagnostics.Stopwatch.GetTimestamp();
            CellType type = sources.CellCache.GetCell(x + 1, y + 1).Type;
            BlockDefinition block = BlockRegistry.Get(type);
            bool door = block.DrawLayer == CellDrawLayer.Foreground && block.Outline == CellOutline.Door;
            quadTicks = System.Diagnostics.Stopwatch.GetTimestamp() - quadStart;

            // Смещённая искажающая клетка: признак того, что реальная карта
            // дала геометрию, а не плоскую сетку.
            if (countAnchoredForeground &&
                type != CellType.Unloaded &&
                TerrainVertexDistortionCalculator.IsWavy(sources.CellCache.GetCellData(x + 1, y + 1)) &&
                TerrainVertexDistortionCalculator.ComputeNode(
                    sources.CellCache, sources.Distortion, x, y, sources.WorldWidth, sources.WorldHeight) !=
                    TerrainVertexOffset.Zero)
            {
                Interlocked.Increment(ref _lastFullBuildAnchoredForegroundCellCount);
            }

            bool doorsChanged = _doors.RecordCell(x, y, door);
            long packStart = System.Diagnostics.Stopwatch.GetTimestamp();
            _buffers.SetCell(gridX, unityY, TerrainCellPacker.PackCell(sources, x, y));
            packTicks = System.Diagnostics.Stopwatch.GetTimestamp() - packStart;
            return doorsChanged;
        }

        private static float TicksToMs(long ticks) =>
            (float)(ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

        private static float ElapsedMs(long startTimestamp) =>
            (float)((System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 /
                System.Diagnostics.Stopwatch.Frequency);
    }
}
