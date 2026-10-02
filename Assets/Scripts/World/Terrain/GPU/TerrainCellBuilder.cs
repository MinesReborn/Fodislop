#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Kern.Core.Interfaces;
using Kern.World.Terrain.Background;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

public sealed class TerrainCellBuilder : IDisposable
{
    private readonly TerrainCellDataTextures _textures = new();
    private readonly TerrainDoorOverlayIndex _doors = new();
    private readonly TerrainCellTextureIndex _textureIndex = new();
    private readonly TerrainMetadataWarmup _warmup = new();
    private readonly TerrainCellFillExecutor _fillExecutor;
    private int _width;
    private int _height;
    private float _cellSize;
    private bool _doorsTouched;

    internal int LastFullBuildAnchoredForegroundCellCount =>
        _fillExecutor.LastFullBuildAnchoredForegroundCellCount;

    public TerrainCellBuilder()
    {
        _fillExecutor = new TerrainCellFillExecutor(_textures, _doors, _textureIndex, _warmup);
    }

    public TerrainCellDataTextures Textures => _textures;

    public float CellSize => _cellSize;

    public bool DoorsTouched => _doorsTouched;

    public bool HasDoors => _doors.HasDoors;

    /// <summary>Сколько стоила последняя сборка, по стадиям.</summary>
    ///
    /// Графа «тексели» в отчёте о провисе оказалась на порядок дороже той же
    /// работы в бенчмарке, а внутри неё четыре разных дела: перенос колец,
    /// снятие уехавших клеток с индекса типов, прогрев метаданных и сама
    /// заливка. Без разбивки следующий шаг опять был бы догадкой.
    public float LastScrollMs { get; private set; }

    public float LastIndexRemoveMs { get; private set; }

    public float LastWarmupMs => _fillExecutor.LastWarmupMs;

    public float LastFillMs => _fillExecutor.LastFillMs;

    public int LastFilledCells => _fillExecutor.LastFilledCells;

    /// <summary>Сумма времени сборки квадов по рабочим потокам, не длительность кадра.</summary>
    ///
    /// Заливка полосы оказалась в тридцать раз дороже той же работы в
    /// бенчмарке, а в ней два разных дела: TerrainQuadBuilder.FillQuad (его
    /// бенчмарк не меряет вообще) и упаковка с записью (её меряет, 43 нс на
    /// клетку). Разделение показывает, какое из двух врёт.
    public float LastQuadMs => _fillExecutor.LastQuadMs;

    /// <summary>Сумма времени упаковки по рабочим потокам; может превышать LastFillMs.</summary>
    public float LastPackMs => _fillExecutor.LastPackMs;

    public void EnsureCapacity(int meshWidth, int meshHeight, float cellSize)
    {
        _cellSize = cellSize;
        _fillExecutor.Configure(meshWidth, meshHeight, cellSize);
        if (_width == meshWidth && _height == meshHeight && _textures.IsAllocated)
        {
            return;
        }

        _width = meshWidth;
        _height = meshHeight;
        _doors.EnsureSize(meshWidth, meshHeight);
        _textureIndex.EnsureWindow(meshWidth, meshHeight);
        _textureIndex.Clear();
        _textures.EnsureCapacity(meshWidth, meshHeight);
    }

    public void BuildFull(TerrainCellSources sources, int minX, int minY)
    {
        BuildFull(sources, minX, minY, CancellationToken.None);
    }

    public void BuildFull(
        TerrainCellSources sources,
        int minX,
        int minY,
        CancellationToken cancellationToken)
    {
        if (!CanBuild(sources))
        {
            return;
        }

        ResetStageTimings();
        _doorsTouched = true;
        _doors.BeginFullBuild();
        _fillExecutor.TrackTextureIndex = false;
        _textures.MarkAllDirty();
        _fillExecutor.FillFull(sources, minX, minY, cancellationToken);
        _doors.CompleteFullBuild();
        _fillExecutor.TrackTextureIndex = true;
        _textureIndex.Clear();
        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                _fillExecutor.UpdateTextureIndexCell(x, y, minX, minY, sources);
            }
        }
    }

    public void ScrollAndBuildBand(TerrainCellSources sources, int minX, int minY, int dx, int dy)
    {
        if (!CanBuild(sources))
        {
            return;
        }

        // Сдвиг во всё окно не оставляет ничего годного, и полосы выродились
        // бы в ту же полную сборку.
        if (Math.Abs(dx) >= _width || Math.Abs(dy) >= _height)
        {
            BuildFull(sources, minX, minY);
            return;
        }

        // Тексели по кольцевому адресу не двигаются: двери переставляет
        // TerrainDoorOverlayIndex вместе с окном.
        ResetStageTimings();
        long scrollStart = System.Diagnostics.Stopwatch.GetTimestamp();
        _doorsTouched = false;
        if (dx != 0 || dy != 0)
        {
            _doorsTouched = _doors.Scroll(dx, dy);
            LastScrollMs = ElapsedMs(scrollStart);

            // Уехавшие клетки с индекса типов не снимаются: слот кольца
            // передаётся приехавшей клетке, и UpdateCell ниже снимает
            // прежнего жильца сам. Полоса заливки накрывает каждый такой
            // слот, поэтому отдельный проход был чистым дублем — и стоил
            // хеш-операции на каждую клетку полосы.
        }

        // Кайма в одну клетку: тексель клетки несёт маски соседства, и у
        // клетки на старой границе сосед снаружи только что появился.
        TerrainScrollBands bands = TerrainScrollBands.Resolve(
            _width, _height, dx, dy, neighbourMargin: 1);

        TerrainScrollBands entered = TerrainScrollBands.Resolve(_width, _height, dx, dy);
        _doors.ClearBand(entered.ColumnBand);
        _doors.ClearBand(entered.RowBand);
        FillBand(bands.ColumnBand, minX, minY, sources);
        FillBand(bands.RowBand, minX, minY, sources);
    }

    public void BuildRegion(
        TerrainCellSources sources,
        int minX,
        int minY,
        int startX,
        int startY,
        int countX,
        int countY)
    {
        ResetStageTimings();
        _doorsTouched = false;
        if (!CanBuild(sources))
        {
            return;
        }

        FillRect(
            Mathf.Clamp(startX, 0, _width),
            Mathf.Clamp(startX + countX, 0, _width),
            Mathf.Clamp(startY, 0, _height),
            Mathf.Clamp(startY + countY, 0, _height),
            minX,
            minY,
            sources);
    }

    internal void BuildTextureCells(
        in TerrainCellTypeSet cellTypes,
        TerrainCellSources sources,
        int minX,
        int minY)
    {
        _doorsTouched = false;
        if (!CanBuild(sources))
        {
            return;
        }

        ResetStageTimings();

        _fillExecutor.WarmAll(sources);

        // Сбор квадов по типам занимает отдельную графу: он идёт по обратному
        // индексу, а не по окну, и его цена растёт с числом приехавших типов.
        long collectStart = System.Diagnostics.Stopwatch.GetTimestamp();
        _textureIndex.CollectRefreshQuads(cellTypes, minX, minY, _width, _height);
        LastIndexRemoveMs = ElapsedMs(collectStart);
        List<int> refreshQuads = _textureIndex.TextureRefreshQuads;
        _fillExecutor.FillTextureQuads(refreshQuads, minX, minY, sources, ref _doorsTouched);

        MarkTextureRefreshRuns(refreshQuads, minX, minY);
    }

    private void MarkTextureRefreshRuns(List<int> refreshQuads, int minX, int minY)
    {
        int index = 0;
        while (index < refreshQuads.Count)
        {
            int firstQuad = refreshQuads[index];
            int x = firstQuad / _height;
            int firstY = firstQuad % _height;
            int lastY = firstY;
            index++;

            while (index < refreshQuads.Count)
            {
                int nextQuad = refreshQuads[index];
                if (nextQuad / _height != x || nextQuad % _height != lastY + 1)
                {
                    break;
                }

                lastY++;
                index++;
            }

            _textures.MarkCells(
                TerrainCellDataTextures.Ring(minX + x, _width),
                TerrainCellDataTextures.Ring(minY + firstY, _height),
                1,
                lastY - firstY + 1);
        }
    }


    // Вершины накладки дверей: клеток с дверью мало, поэтому их квады
    // собираются заново по требованию, а не хранятся для всей сетки.
    public void BuildDoorOverlay(
        TerrainCellSources sources,
        int minX,
        int minY,
        List<TerrainVertex> vertices,
        List<int>[] indicesPerAtlas) =>
        _fillExecutor.BuildDoorOverlay(
            sources,
            minX,
            minY,
            vertices,
            indicesPerAtlas);

    // Выгрузка cell-data на GPU и адрес окна для шейдера. При scroll
    // переписываются только новые клетки; полный upload остаётся для первого
    // build и resize.
    public void Commit(int originX, int originY)
    {
        _textures.Apply();
        _textures.BindGlobals(_cellSize, originX, originY);
    }

    public void Dispose()
    {
        _textures.Dispose();
        _width = 0;
        _height = 0;
    }

    private bool CanBuild(TerrainCellSources sources) =>
        _textures.IsAllocated && sources.Atlases != null && sources.Atlases.Count > 0;

    // Графы отчёта обнуляются на входе в КАЖДЫЙ путь сборки. Пока это делали
    // только сдвиг и перечитывание текстур, отчёт о полной сборке печатал
    // числа прошлого кадра — и они выглядели как измерение, а не как мусор.
    private void ResetStageTimings()
    {
        LastScrollMs = 0f;
        LastIndexRemoveMs = 0f;
        _fillExecutor.ResetStageTimings();
    }

    private static float ElapsedMs(long startTimestamp) =>
        (float)((System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 /
            System.Diagnostics.Stopwatch.Frequency);

    private void FillBand(RectInt band, int minX, int minY, TerrainCellSources sources) =>
        FillRect(band.xMin, band.xMax, band.yMin, band.yMax, minX, minY, sources);

    private void FillRect(int startX, int endX, int startY, int endY, int minX, int minY, TerrainCellSources sources)
    {
        _doorsTouched |= _fillExecutor.FillRect(
            startX,
            endX,
            startY,
            endY,
            minX,
            minY,
            sources);
    }
}
