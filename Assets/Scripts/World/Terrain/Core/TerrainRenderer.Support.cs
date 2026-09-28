#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.Diagnostics;

namespace Kern.World.Terrain;

/// <summary>
/// Reserved support namespace for terrain frame helpers.
/// </summary>
internal static class TerrainRendererSupport
{
    public static void HandleTextureLoaded(
        TerrainFrameDiagnostics diagnostics,
        TerrainWorldChangeHandler worldChanges,
        string filename,
        UnityEngine.Texture2D texture)
    {
        diagnostics.Mark(1 << 9, $"[TerrainDiag] first texture arrived: {filename}");
        worldChanges.HandleTextureLoaded(filename, texture);
    }

    public static TerrainBuildContext CreateBuildContext(
        IWorldDataStorage storage,
        MapManager mapManager,
        ITextureService textureService,
        IFrameTelemetry telemetry,
        int width,
        int height) =>
        new(
            storage,
            mapManager,
            textureService ?? throw new InvalidOperationException(
                "TerrainRenderer requires ITextureService injection."),
            telemetry,
            width,
            height);

    public static void RecordFrameDiagnostics(
        TerrainFrameDiagnostics diagnostics,
        IFrameTelemetry telemetry,
        TerrainFramePlanner planner,
        long stallStart,
        float planMs,
        float dimensionsMs,
        float processMs,
        float uploadMs,
        int dirtyRectCount,
        long dirtyArea) =>
        diagnostics.Record(
            stallStart,
            telemetry,
            new TerrainFrameTimings(
                planMs,
                dimensionsMs,
                processMs,
                uploadMs,
                dirtyRectCount,
                dirtyArea,
                planner.LastResidencyProbeCalls,
                planner.LastResidencyChunkReads,
                planner.LastResidencyCacheHits,
                planner.LastResidencyLruTouches));
}
