#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.Diagnostics;
using Kern.World;
using UnityEngine;

using Kern.Diagnostics.Frames;

using Kern.Diagnostics.World;

namespace Kern.World.Terrain;

/// <summary>Что кадр успел измерить к моменту отчёта.</summary>
public readonly record struct TerrainFrameTimings(
    float PlanMs,
    float DimensionsMs,
    float ProcessMs,
    float UploadMs,
    int DirtyRectCount,
    long DirtyArea,
    int ResidencyProbeCalls,
    int ResidencyChunkReads,
    int ResidencyCacheHits,
    int ResidencyLRUTouches);

/// <summary>
/// Наблюдение за кадром террейна и разбор отказа сборки.
/// </summary>
///
/// Renderer отвечает за порядок кадра, а не за то, чем этот кадр меряется.
/// Здесь собирается <see cref="TerrainStallFrame"/> из состояния окна и
/// конвейера; дорогие кадры уходят в отчёт о провисе через
/// <see cref="FrameEventLog"/>, пока диагностика жива. Сюда же вынесено
/// сообщение об отказе сборки: оно снимает снимок мира в момент падения, а
/// после перезапуска сцены его уже не снять.
public sealed class TerrainFrameDiagnostics : IDisposable
{
    private const float NotReadyLogIntervalSeconds = 1f;

    private readonly TerrainWindow _window;
    private readonly TerrainDiagnosticLog _log = new();
    private readonly TerrainStallReport _stall = new();
    private float _nextNotReadyLogTime;

    public TerrainFrameDiagnostics(TerrainWindow window)
    {
        _window = window;
        FrameEventLog.AddSource(_stall);
    }

    public void Dispose() => FrameEventLog.RemoveSource(_stall);

    /// <summary>Одноразовая отметка о том, как террейн проходит старт.</summary>
    public void Mark(int bit, string message) => _log.Once(bit, message);

    public void Record(long stallStart, IFrameTelemetry telemetry, in TerrainFrameTimings timings)
    {
        TerrainBuildPipeline pipeline = _window.Driver.Pipeline;

        // Счётчики сборщика клеток принадлежат рабочему потоку и здесь не
        // читаются: цена шага берётся из его опубликованного итога.
        TerrainCellBuffers textures = pipeline.CellBuilder.Buffers;
        _stall.Record(
            stallStart,
            telemetry,
            new TerrainStallFrame(
                pipeline.LastBuildScrolled,
                pipeline.LastScrollDelta,
                _window.Origin,
                new Vector2Int(_window.Width, _window.Height),
                timings.DirtyRectCount,
                timings.DirtyArea,
                timings.ProcessMs,
                timings.UploadMs,
                textures.LastUploadRectCount,
                textures.LastUploadTexels,
                textures.LastStageMs,
                textures.LastUploadStrips,
                timings.PlanMs,
                timings.DimensionsMs,
                timings.ResidencyProbeCalls,
                timings.ResidencyChunkReads,
                timings.ResidencyCacheHits,
                timings.ResidencyLRUTouches,
                new TerrainStallBuildState(_window.BuildState, _window.HasCPUBuildInFlight),
                pipeline.LastWorkerCost));
    }

    /// <summary>
    /// Сборка упала: террейн замолкает до перезапуска сцены. Продолжать
    /// кадрами по частично собранному окну значит показывать дыры и
    /// приписывать их чему угодно, кроме настоящей причины.
    /// </summary>
    ///
    /// true — отказ настоящий. false — «ещё нечем»: атласы не приехали, кадр
    /// пропущен, террейн жив и попробует снова.
    public bool ReportBuildFailure(
        Exception? failure,
        Vector2Int origin,
        MapManager? mapManager,
        ITextureService? textureService,
        IWorldDataStorage? storage)
    {
        if (failure == null)
        {
            // "Ещё нечем" не должно значить "навсегда молча". Состояние
            // Error без исключения - это упавшая сборка, а не ожидание:
            // раньше оба случая схлопывались в одну отметку Once и после
            // первого раза чёрный экран ничего не писал.
            if (_window.BuildState == TerrainBuildState.Error)
            {
                Debug.LogError(
                    "[TerrainRenderer] Build failed without an exception. " +
                    DescribeState(origin, mapManager, textureService, storage));
                return true;
            }

            float now = Time.realtimeSinceStartup;
            if (now >= _nextNotReadyLogTime)
            {
                _nextNotReadyLogTime = now + NotReadyLogIntervalSeconds;
                Debug.LogWarning(
                    "[TerrainRenderer] Terrain has no build sources yet. " +
                    DescribeState(origin, mapManager, textureService, storage));
            }

            return false;
        }

        Debug.LogException(new InvalidOperationException(
            "[TerrainRenderer] Build failed: " +
            DescribeState(origin, mapManager, textureService, storage),
            failure));
        return true;
    }

    private string DescribeState(
        Vector2Int origin,
        MapManager? mapManager,
        ITextureService? textureService,
        IWorldDataStorage? storage) =>
        $"state={_window.BuildState}, committed={_window.CellsCommitted}, " +
        $"inFlight={_window.HasCPUBuildInFlight}, needsRefresh={_window.NeedsRefresh}, " +
        $"pendingTextures={_window.PendingTextureCellTypes.Count}, " +
        $"atlases={textureService?.GetAllAtlases().Count ?? 0}, " +
        $"withdrawn={_window.LastWithdrawalReason ?? "-"}, " +
        $"grid={origin}, size={_window.Width}x{_window.Height}, " +
        $"world={mapManager?.WorldWidth ?? 0}x{mapManager?.WorldHeight ?? 0}, " +
        $"storageReady={storage?.IsReady ?? false}";
}
