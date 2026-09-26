#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.Diagnostics;
using Kern.Core.Lifecycle;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>Живые источники главного потока для планирования и публикации сборки.</summary>
public readonly record struct TerrainBuildContext(
    IWorldDataStorage Storage,
    IMapDataProvider MapData,
    ITextureService TextureService,
    IFrameTelemetry Telemetry,
    int MeshWidth,
    int MeshHeight);

/// <summary>Coordinates CPU build stages and delegates scene presentation to its owner.</summary>
public sealed class TerrainBuildDriver : IDisposable
{
    private readonly TerrainBuildPipeline _pipeline = new();
    private readonly TerrainBuildPresentation _presentation = new();

    public TerrainBuildPipeline Pipeline => _pipeline;

    internal TerrainBuildPresentation Presentation => _presentation;

    /// <summary>Привязка к сцене: у накладки дверей собственный объект под террейном.</summary>
    public void Attach(
        Transform parent,
        ISceneObjectFactory sceneObjects,
        string sortingLayerName,
        int doorOverlaySortingOrder,
        float cellSize)
    {
        _presentation.Attach(
            parent,
            sceneObjects,
            sortingLayerName,
            doorOverlaySortingOrder,
            cellSize);
    }

    public void EnsureCapacity(int meshWidth, int meshHeight)
    {
        _pipeline.EnsureCapacity(meshWidth, meshHeight, _presentation.CellSize);
    }

    /// <summary>
    /// Подготовить материалы кадра и собрать контекст сборки. Возвращает false,
    /// если строить ещё не из чего: атласы не приехали.
    /// </summary>
    public bool TryBeginBuild(
        in TerrainBuildContext context,
        IClientConfigManager clientConfigManager,
        out IReadOnlyList<IAtlasDescriptor> atlases,
        out bool materialsChanged)
    {
        return _presentation.TryBeginBuild(
            context,
            clientConfigManager,
            _pipeline.CellCache,
            out atlases,
            out materialsChanged);
    }

    /// <summary>Контекст без пересоздания материалов: публикация их не меняет.</summary>
    public bool TryContinueBuild(
        in TerrainBuildContext context,
        out IReadOnlyList<IAtlasDescriptor> atlases)
    {
        return _presentation.TryContinueBuild(context, out atlases);
    }

    internal TerrainCpuBuildRequest Prepare(
        in TerrainBuildContext context,
        IReadOnlyList<IAtlasDescriptor> atlases,
        Vector2Int origin,
        bool forceFull,
        bool rebuildAllCells,
        DirtyRectSet dirtyRects,
        HashSet<CellType> textureTypes,
        ulong contentRevision,
        long worldGeneration) =>
        _pipeline.Prepare(
            context,
            atlases,
            origin,
            forceFull,
            rebuildAllCells,
            dirtyRects,
            textureTypes,
            contentRevision,
            worldGeneration);

    internal TerrainCpuBuildResult Execute(
        TerrainCpuBuildRequest request,
        CancellationToken cancellationToken) =>
        _pipeline.Execute(request, cancellationToken);

    internal bool IsAtlasSnapshotCurrent(
        TerrainCpuBuildRequest request,
        IReadOnlyList<IAtlasDescriptor> atlases) =>
        _pipeline.IsAtlasSnapshotCurrent(request, atlases);

    /// <summary>
    /// Главный поток, шаг завершён: привязать атласы и довести накладку дверей
    /// до той же версии, что тексели. Вызывается до переноса родителя.
    /// </summary>
    internal void Publish(
        in TerrainBuildContext context,
        IReadOnlyList<IAtlasDescriptor> atlases,
        TerrainCpuBuildRequest request,
        TerrainCpuBuildResult result,
        float latencyMs)
    {
        _pipeline.RecordPublished(request, result, latencyMs);
        _presentation.Publish(
            atlases,
            context,
            request,
            result,
            _pipeline.CellBuilder,
            _pipeline.CreateSources(request),
            _pipeline.LastBuildScrolled,
            _pipeline.LastScrollDelta);
    }

    /// <summary>Опубликованной версии больше нет: её двери не показываются.</summary>
    public void HideDoorOverlay() => _presentation.HideDoorOverlay();

    public float Commit(int originX, int originY) => _pipeline.Commit(originX, originY);

    public void Dispose()
    {
        _pipeline.Dispose();
        _presentation.Dispose();
    }
}
