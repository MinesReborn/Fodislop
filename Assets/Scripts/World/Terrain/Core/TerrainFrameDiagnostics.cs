#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.Diagnostics;
using Kern.World;
using UnityEngine;

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
    int ResidencyLruTouches);

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
    private readonly TerrainWindow _window;
    private readonly TerrainDiagnosticLog _log = new();
    private readonly TerrainStallReport _stall = new();

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
        TerrainCellDataTextures textures = pipeline.CellBuilder.Textures;
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
                textures.LastStageCopyMs,
                textures.LastStageApplyMs,
                textures.LastUploadStrips,
                timings.PlanMs,
                timings.DimensionsMs,
                timings.ResidencyProbeCalls,
                timings.ResidencyChunkReads,
                timings.ResidencyCacheHits,
                timings.ResidencyLruTouches,
                new TerrainStallBuildState(_window.BuildState, _window.HasCpuBuildInFlight),
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
            _log.Once(1 << 6, "[TerrainDiag] BAIL: build sources not ready");
            return false;
        }

        Debug.LogException(new InvalidOperationException(
            $"[TerrainRenderer] Build failed: grid={origin} " +
            $"size={_window.Width}x{_window.Height}, world=" +
            $"{mapManager?.WorldWidth ?? 0}x{mapManager?.WorldHeight ?? 0}, " +
            $"atlases={textureService?.GetAllAtlases().Count ?? 0}, " +
            $"storageReady={storage?.IsReady ?? false}.",
            failure));
        return true;
    }
}
