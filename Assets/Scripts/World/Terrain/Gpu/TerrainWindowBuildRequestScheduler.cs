#nullable enable

using System;
using System.Diagnostics;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.Diagnostics;
using Kern.World.Streaming;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

internal enum TerrainBuildSchedulingResult
{
    Scheduled,
    WaitingForData,
    Failed,
}

/// <summary>Immutable view of lifecycle state needed to plan one CPU build.</summary>
internal readonly struct TerrainBuildSchedulingIntent
{
    public TerrainBuildSchedulingIntent(
        Vector2Int origin,
        bool dimensionsChanged,
        MeshRenderer? meshRenderer,
        ulong contentRevision,
        long worldGeneration,
        bool holdPublication,
        bool hasHeldCompletion,
        bool needsRefresh,
        bool hasOrigin,
        Vector2Int publishedOrigin,
        Vector2Int publishedSize)
    {
        Origin = origin;
        DimensionsChanged = dimensionsChanged;
        MeshRenderer = meshRenderer;
        ContentRevision = contentRevision;
        WorldGeneration = worldGeneration;
        HoldPublication = holdPublication;
        HasHeldCompletion = hasHeldCompletion;
        NeedsRefresh = needsRefresh;
        HasOrigin = hasOrigin;
        PublishedOrigin = publishedOrigin;
        PublishedSize = publishedSize;
    }

    public Vector2Int Origin { get; }

    public bool DimensionsChanged { get; }

    public MeshRenderer? MeshRenderer { get; }

    public ulong ContentRevision { get; }

    public long WorldGeneration { get; }

    public bool HoldPublication { get; }

    public bool HasHeldCompletion { get; }

    public bool NeedsRefresh { get; }

    public bool HasOrigin { get; }

    public Vector2Int PublishedOrigin { get; }

    public Vector2Int PublishedSize { get; }
}

/// <summary>
/// Owns terrain build request preparation and worker scheduling. Completion
/// acceptance and publication remain in TerrainWindowBuildLifecycle.
/// </summary>
internal sealed class TerrainWindowBuildRequestScheduler : IDisposable
{
    private static readonly RebuildLedger.Entry _RebuildResize = RebuildLedger.Register("Террейн · полная: смена размера сетки");
    private static readonly RebuildLedger.Entry _RebuildGridMove = RebuildLedger.Register("Террейн · полная: сдвиг сетки");
    private static readonly RebuildLedger.Entry _RebuildRefresh = RebuildLedger.Register("Террейн · полная: флаг обновления");
    private static readonly RebuildLedger.Entry _RebuildPatch = RebuildLedger.Register("Террейн · частичная: изменённые клетки");

    private readonly TerrainBuildDriver _driver;
    private readonly TerrainWindowChangeJournal _changes;
    private readonly TerrainWindowPublishedView _publishedView;
    private readonly TerrainBuildScheduler<TerrainCpuBuildRequest, TerrainCpuBuildResult> _builds;
    private long _buildStartTimestamp;
    private bool _buildRefreshesTextures;

    public TerrainWindowBuildRequestScheduler(
        TerrainBuildDriver driver,
        TerrainWindowChangeJournal changes,
        TerrainWindowPublishedView publishedView)
    {
        _driver = driver;
        _changes = changes;
        _publishedView = publishedView;
        _builds = new(driver.Execute);
    }

    public bool IsBusy => _builds.IsBusy;

    public TerrainCpuBuildRequest? ActiveRequest => _builds.ActiveRequest;

    public bool HasUnpublishedTextureRefresh => _buildRefreshesTextures;

    public float BuildStartElapsedSeconds => (float)((Stopwatch.GetTimestamp() - _buildStartTimestamp) /
        (double)Stopwatch.Frequency);

    public void MarkCompletionHasNoTextureRefresh() => _buildRefreshesTextures = false;

    public void Cancel() => _builds.Cancel();

    public bool TryTakeCompleted(out TerrainBuildCompletion<TerrainCpuBuildRequest, TerrainCpuBuildResult> completion) =>
        _builds.TryTakeCompleted(out completion);

    public void Dispose() => _builds.Dispose();

    public TerrainBuildSchedulingResult Schedule(
        in TerrainBuildServices services,
        IClientConfigManager clientConfigManager,
        in TerrainBuildSchedulingIntent intent,
        out Exception? failure,
        out long oldestChangeTimestamp)
    {
        failure = null;
        oldestChangeTimestamp = 0;
        if (!_driver.TryBeginBuild(
            services,
            clientConfigManager,
            out TerrainBuildContext context,
            out bool materialsChanged))
        {
            return TerrainBuildSchedulingResult.WaitingForData;
        }

        if (materialsChanged)
        {
            // Прежние атласы и материалы уже уничтожены: прежние тексели
            // ссылаются на несуществующие индексы. Окно не рисуется, пока новый
            // набор не собран целиком.
            if (intent.MeshRenderer != null)
            {
                intent.MeshRenderer.sharedMaterials = _driver.Materials.CellMaterials;
            }

            if (_publishedView.CellsCommitted)
            {
                _publishedView.WithdrawPublication(_driver);
            }
        }

        if (_changes.TryTakeRequestedDistortion(out bool distortion))
        {
            _driver.Pipeline.EnableDistortion = distortion;
        }

        if (_changes.TryTakeRequestedDistortionStyle(out TerrainDistortionStyle distortionStyle))
        {
            _driver.Pipeline.DistortionStyle = distortionStyle;
        }

        // Во время перехода конвейер мог уйти вперёд неопубликованным шагом:
        // приращение к нему не с чем сверить, поэтому шаг перехода — целиком.
        bool forceFull = intent.HoldPublication || intent.HasHeldCompletion ||
            intent.NeedsRefresh || intent.DimensionsChanged || !intent.HasOrigin || materialsChanged ||
            Math.Abs((long)intent.Origin.x - intent.PublishedOrigin.x) >= intent.PublishedSize.x ||
            Math.Abs((long)intent.Origin.y - intent.PublishedOrigin.y) >= intent.PublishedSize.y;
        RebuildLedger.Count(
            intent.DimensionsChanged ? _RebuildResize
            : forceFull ? _RebuildRefresh
            : intent.Origin != intent.PublishedOrigin ? _RebuildGridMove
            : _RebuildPatch);

        // Набор приехавших типов отдаётся шагу целиком и сразу заменяется
        // пустым: текстура, приехавшая во время подготовки, ложится в новый
        // набор и станет следующим шагом, а не меняет перебираемый.
        _changes.SwapTextureTypes();
        TerrainCpuBuildRequest request;
        try
        {
            request = _driver.Prepare(
                context,
                intent.Origin,
                forceFull,
                materialsChanged || _changes.RebuildAllCells,
                _changes.Dirty.Rects,
                _changes.BuildTextureCellTypes,
                intent.ContentRevision,
                intent.WorldGeneration);
        }
        catch (Exception exception)
        {
            failure = exception;
            return TerrainBuildSchedulingResult.Failed;
        }

        // Всё, что шаг взял, из очереди снимается сразу: изменения, пришедшие
        // во время шага, копятся заново и станут следующим шагом.
        _buildRefreshesTextures = _changes.BuildTextureCellTypes.Count > 0;
        _changes.ClearBuildTextureCellTypes();
        _changes.ClearDirty();
        _changes.RebuildAllCells = false;

        // Списки меняются местами, а не копируются: шаг за шагом без аллокаций.
        _changes.SwapBuildChanges();
        oldestChangeTimestamp = _changes.TakeOldestChangeTimestamp();
        _changes.NeedsRefresh = false;
        _buildStartTimestamp = Stopwatch.GetTimestamp();
        _builds.Start(request);
        return TerrainBuildSchedulingResult.Scheduled;
    }
}
