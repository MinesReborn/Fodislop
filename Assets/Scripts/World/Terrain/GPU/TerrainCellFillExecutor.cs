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
        private sealed class Scratch
        {
            public readonly TerrainVertex[] Vertices = new TerrainVertex[8];

            public Span<TerrainVertex> Background => Vertices.AsSpan(0, 4);

            public Span<TerrainVertex> Foreground => Vertices.AsSpan(4, 4);
        }

        private sealed class FillState
        {
            public readonly Scratch Scratch = new();
            public long QuadTicks;
            public long PackTicks;
            public bool DoorsTouched;
        }

        private readonly TerrainCellDataTextures _textures;
        private readonly TerrainDoorOverlayIndex _doors;
        private readonly TerrainCellTextureIndex _textureIndex;
        private readonly TerrainMetadataWarmup _warmup;
        private readonly Scratch _mainScratch = new();
        private int _width;
        private int _height;
        private float _cellSize;
        private bool _trackTextureIndex;
        private long _quadTicks;
        private long _packTicks;
        private int _lastFullBuildAnchoredForegroundCellCount;

        public TerrainCellFillExecutor(
            TerrainCellDataTextures textures,
            TerrainDoorOverlayIndex doors,
            TerrainCellTextureIndex textureIndex,
            TerrainMetadataWarmup warmup)
        {
            _textures = textures;
            _doors = doors;
            _textureIndex = textureIndex;
            _warmup = warmup;
        }

        public bool TrackTextureIndex
        {
            get => _trackTextureIndex;
            set => _trackTextureIndex = value;
        }

        public float LastWarmupMs { get; private set; }

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
            LastWarmupMs = 0f;
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
            long warmStart = System.Diagnostics.Stopwatch.GetTimestamp();
            _warmup.WarmRect(sources, 0, _width, 0, _height);
            LastWarmupMs = ElapsedMs(warmStart);

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
                            state.Scratch,
                            true,
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

        public void FillTextureQuads(
            List<int> refreshQuads,
            int minX,
            int minY,
            TerrainCellSources sources,
            ref bool doorsTouched)
        {
            long fillStart = System.Diagnostics.Stopwatch.GetTimestamp();
            _quadTicks = 0;
            _packTicks = 0;
            bool trackTextureIndex = _trackTextureIndex;
            _trackTextureIndex = false;
            try
            {
                for (int index = 0; index < refreshQuads.Count; index++)
                {
                    int quad = refreshQuads[index];
                    int x = quad / _height;
                    int y = quad % _height;
                    bool cellDoorsChanged = FillCell(
                        x,
                        y,
                        minX,
                        minY,
                        sources,
                        _mainScratch,
                        true,
                        out long quadTicks,
                        out long packTicks);
                    doorsTouched |= cellDoorsChanged;
                    _quadTicks += quadTicks;
                    _packTicks += packTicks;
                }

                LastFillMs = ElapsedMs(fillStart);
                LastQuadMs = TicksToMs(_quadTicks);
                LastPackMs = TicksToMs(_packTicks);
                LastFilledCells = refreshQuads.Count;
            }
            finally
            {
                _trackTextureIndex = trackTextureIndex;
            }
        }

        public void WarmAll(TerrainCellSources sources)
        {
            long warmStart = System.Diagnostics.Stopwatch.GetTimestamp();
            _warmup.WarmRect(sources, 0, _width, 0, _height);
            LastWarmupMs = ElapsedMs(warmStart);
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

            long warmStart = System.Diagnostics.Stopwatch.GetTimestamp();
            _warmup.WarmRect(sources, startX, endX, startY, endY);
            LastWarmupMs += ElapsedMs(warmStart);

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
                            _mainScratch,
                            false,
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
                                state.Scratch,
                                false,
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

            if (_trackTextureIndex)
            {
                for (int x = startX; x < endX; x++)
                {
                    for (int y = startY; y < endY; y++)
                    {
                        UpdateTextureIndexCell(x, y, minX, minY, sources);
                    }
                }
            }

            LastFillMs += ElapsedMs(fillStart);
            LastFilledCells += (endX - startX) * (endY - startY);

            _textures.MarkCells(
                TerrainCellDataTextures.Ring(minX + startX, _width),
                TerrainCellDataTextures.Ring(minY + startY, _height),
                endX - startX,
                endY - startY);
            return doorsTouched != 0;
        }

        public void UpdateTextureIndexCell(int x, int y, int minX, int minY, TerrainCellSources sources)
        {
            CachedCellData cell = sources.CellCache.GetCellData(x + 1, y + 1);
            CellType background = TerrainCellLayers.ResolveBackground(
                cell.Type, sources.FloodFill.Buffer[x, y], cell.Properties);
            _textureIndex.UpdateCell(minX + x, minY + y, background, cell.Type);
        }

        public void BuildDoorOverlay(
            TerrainCellSources sources,
            int minX,
            int minY,
            List<TerrainVertex> vertices,
            List<int>[] indicesPerAtlas) =>
            _doors.BuildOverlay(
                sources,
                minX,
                minY,
                _cellSize,
                _mainScratch.Foreground,
                vertices,
                indicesPerAtlas);

        private bool FillCell(
            int x,
            int y,
            int minX,
            int minY,
            TerrainCellSources sources,
            Scratch scratch,
            bool updateTextureIndex,
            out long quadTicks,
            out long packTicks,
            bool countAnchoredForeground = false)
        {
            int gridX = minX + x;
            int unityY = minY + y;
            var site = new TerrainQuadSite(x, y, gridX, unityY, _cellSize);

            long quadStart = System.Diagnostics.Stopwatch.GetTimestamp();
            int background = TerrainQuadBuilder
                .FillQuad(sources, site, TerrainQuadLayer.Background, scratch.Background)
                .AtlasIndex;
            TerrainQuadResult foregroundQuad = TerrainQuadBuilder.FillQuad(
                sources, site, TerrainQuadLayer.Foreground, scratch.Foreground);
            int foreground = foregroundQuad.AtlasIndex;
            quadTicks = System.Diagnostics.Stopwatch.GetTimestamp() - quadStart;

            if (countAnchoredForeground && foregroundQuad.HasAtlas && scratch.Vertices[4].UV5x != 0)
            {
                Interlocked.Increment(ref _lastFullBuildAnchoredForegroundCellCount);
            }

            bool doorsChanged = _doors.RecordCell(
                x, y, foreground, foregroundQuad.IsDoor, scratch.Foreground);
            if (_trackTextureIndex && updateTextureIndex)
            {
                UpdateTextureIndexCell(x, y, minX, minY, sources);
            }

            long packStart = System.Diagnostics.Stopwatch.GetTimestamp();
            int ringX = TerrainCellDataTextures.Ring(gridX, _width);
            int ringY = TerrainCellDataTextures.Ring(unityY, _height);
            TerrainCellTexels backgroundTexels = TerrainCellDataPacker.PackQuad(scratch.Vertices.AsSpan(0, 4), background);
            if (background >= 0 && TerrainForegroundOcclusion.CoversCell(
                x, y, foreground, scratch.Vertices, sources))
            {
                Color32 meta = backgroundTexels.Meta;
                backgroundTexels = backgroundTexels with
                {
                    Meta = new Color32(meta.r, meta.g, byte.MaxValue, meta.a),
                };
            }

            _textures.SetCell(ringX, ringY, TerrainCellDataPacker.BackgroundLayer, backgroundTexels);
            _textures.SetCell(
                ringX, ringY, TerrainCellDataPacker.ForegroundLayer,
                TerrainCellDataPacker.PackQuad(scratch.Vertices.AsSpan(4, 4), foreground));
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
