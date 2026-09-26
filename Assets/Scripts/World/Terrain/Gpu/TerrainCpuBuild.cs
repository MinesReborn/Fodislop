#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Kern.Core.Interfaces;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Шаг фоновой сборки террейна: что главный поток уже положил в кэш клеток и
/// что рабочему потоку осталось досчитать.
/// </summary>
///
/// Кэш клеток заполняется на главном потоке — это единственное место, где
/// читается живое хранилище мира и разрешаются типы. После этого кэш,
/// предрасчёт, заливка фона и тексели принадлежат рабочему потоку до
/// публикации. Снимок кэша не копируется: владение передаётся целиком, а
/// главный поток до публикации к этим структурам не прикасается.
internal sealed class TerrainCpuBuildRequest
{
    private readonly TerrainWorldCellRegion[] _dirtyRegions;
    private readonly ReadOnlyCollection<IAtlasDescriptor> _atlases;

    public TerrainCpuBuildRequest(
        Vector2Int origin,
        Vector2Int size,
        bool cacheScrolled,
        Vector2Int scrollDelta,
        bool buildFull,
        IReadOnlyList<IAtlasDescriptor> atlases,
        int worldWidth,
        int worldHeight,
        TerrainWorldCellRegion[] dirtyRegions,
        in TerrainCellTypeSet textureTypes,
        ulong contentRevision,
        long worldGeneration,
        ulong atlasRevision)
    {
        Origin = origin;
        Size = size;
        CacheScrolled = cacheScrolled;
        ScrollDelta = scrollDelta;
        BuildFull = buildFull;
        IAtlasDescriptor[] atlasSnapshot = new IAtlasDescriptor[atlases.Count];
        for (int index = 0; index < atlasSnapshot.Length; index++)
        {
            atlasSnapshot[index] = atlases[index];
        }

        _atlases = Array.AsReadOnly(atlasSnapshot);
        WorldWidth = worldWidth;
        WorldHeight = worldHeight;
        // The prepare stage transfers this freshly allocated array into the
        // request and does not retain or mutate it afterwards.
        _dirtyRegions = dirtyRegions;
        TextureTypes = textureTypes;
        ContentRevision = contentRevision;
        WorldGeneration = worldGeneration;
        AtlasRevision = atlasRevision;
    }

    public Vector2Int Origin { get; }

    public Vector2Int Size { get; }

    /// <summary>Кэш перенёс перекрытие; предрасчёт и заливка идут приращением.</summary>
    public bool CacheScrolled { get; }

    public Vector2Int ScrollDelta { get; }

    /// <summary>Тексели собираются целиком: перекрытие переносить нельзя.</summary>
    public bool BuildFull { get; }

    /// <summary>Числовые снимки атласов: живые атласы меняются на главном потоке.</summary>
    public IReadOnlyList<IAtlasDescriptor> Atlases => _atlases;

    public int WorldWidth { get; }

    public int WorldHeight { get; }

    /// <summary>Изменённые клетки мира Unity, уже перечитанные в кэш.</summary>
    public ReadOnlySpan<TerrainWorldCellRegion> DirtyRegions => _dirtyRegions;

    /// <summary>Типы, чьи метаданные обновились после приезда текстуры.</summary>
    public TerrainCellTypeSet TextureTypes { get; }

    public ulong ContentRevision { get; }

    public long WorldGeneration { get; }

    public ulong AtlasRevision { get; }
}

/// <summary>Итог шага: что изменилось и сколько это стоило рабочему потоку.</summary>
internal sealed class TerrainCpuBuildResult
{
    public TerrainCpuBuildResult(
        bool doorsTouched,
        float cacheMs,
        float precalculateMs,
        float floodFillMs,
        float meshMs,
        float elapsedMs,
        float scrollMs,
        float warmupMs,
        float fillMs,
        int filledCells,
        float quadMs,
        float packMs)
    {
        DoorsTouched = doorsTouched;
        CacheMs = cacheMs;
        PrecalculateMs = precalculateMs;
        FloodFillMs = floodFillMs;
        MeshMs = meshMs;
        ElapsedMs = elapsedMs;
        ScrollMs = scrollMs;
        WarmupMs = warmupMs;
        FillMs = fillMs;
        FilledCells = filledCells;
        QuadMs = quadMs;
        PackMs = packMs;
    }

    public bool DoorsTouched { get; }

    public float CacheMs { get; }

    public float PrecalculateMs { get; }

    public float FloodFillMs { get; }

    public float MeshMs { get; }

    public float ElapsedMs { get; }

    public float ScrollMs { get; }

    public float WarmupMs { get; }

    public float FillMs { get; }

    public int FilledCells { get; }

    public float QuadMs { get; }

    public float PackMs { get; }
}

internal sealed class TerrainCpuBuildResultBuilder
{
    public bool DoorsTouched { get; set; }

    /// <summary>Раскладка снятых клеток в кольцо кэша.</summary>
    public float CacheMs { get; set; }

    public float PrecalculateMs { get; set; }

    public float FloodFillMs { get; set; }

    public float MeshMs { get; set; }

    // Разбивка текселей по стадиям сборщика, сложенная по всем его вызовам
    // шага: полоса и каждая заплатка сбрасывают счётчики сборщика заново.
    public float ScrollMs { get; set; }

    public float WarmupMs { get; set; }

    public float FillMs { get; set; }

    public int FilledCells { get; set; }

    public float QuadMs { get; set; }

    public float PackMs { get; set; }

    public float ElapsedMs { get; set; }

    public void AddBuilderStages(TerrainCellBuilder builder)
    {
        ScrollMs += builder.LastScrollMs;
        WarmupMs += builder.LastWarmupMs;
        FillMs += builder.LastFillMs;
        FilledCells += builder.LastFilledCells;
        QuadMs += builder.LastQuadMs;
        PackMs += builder.LastPackMs;
    }

    public TerrainCpuBuildResult Build() => new(
        DoorsTouched,
        CacheMs,
        PrecalculateMs,
        FloodFillMs,
        MeshMs,
        ElapsedMs,
        ScrollMs,
        WarmupMs,
        FillMs,
        FilledCells,
        QuadMs,
        PackMs);
}
