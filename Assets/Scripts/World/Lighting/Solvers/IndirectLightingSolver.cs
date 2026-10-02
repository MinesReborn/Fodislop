#nullable enable

using Kern.Core;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Lighting;

internal sealed class IndirectLightingSolver
{
    private static readonly ProfilerMarker s_compositeMarker =
        new("Kern.Lighting.Composite.Record.CPU");

    // The dirty union includes every surface receiver whose cached air sample
    // can read a changed direct-light texel. Composite runs on the light
    // lattice; every rectangle here is in light texels.
    private const float CompositeNeighborCells = LightingConfigHolder.SurfaceReflectionReachCells;

    private const int PartialSlackTexels = 2;

    private readonly LightingResourceManager _resources;

    public IndirectLightingSolver(LightingResourceManager resources)
    {
        _resources = resources;
    }

    public void RecordComposite(
        CommandBuffer commandBuffer,
        RectInt? fieldDirtyRect,
        Vector4 worldRect,
        float cellSize,
        IFrameTelemetry telemetry)
    {
        using var compositeMarker = s_compositeMarker.Auto();
        long compositeStart = System.Diagnostics.Stopwatch.GetTimestamp();
        commandBuffer.BeginSample("Kern.Lighting.Composite");
        ComputeShader compute = _resources.LightingCompute!;
        int kernel = _resources.CompositeLightingKernel;
        commandBuffer.SetComputeTextureParam(
            compute,
            kernel,
            LightingComputeBinder.DirectInputId,
            _resources.DirectTexture!);
        commandBuffer.SetComputeTextureParam(
            compute,
            kernel,
            LightingComputeBinder.StaticDirectInputId,
            _resources.StaticDirectTexture!);
        commandBuffer.SetComputeTextureParam(
            compute,
            kernel,
            LightingComputeBinder.ResultId,
            _resources.LightmapTexture!);
        commandBuffer.SetComputeTextureParam(
            compute,
            kernel,
            LightingComputeBinder.SurfaceAirCacheId,
            _resources.SurfaceAirCache!);
        int lightWidth = _resources.LightWidth;
        int lightHeight = _resources.LightHeight;
        if (TryGetFieldRect(fieldDirtyRect, worldRect, cellSize, out RectInt compositeRect))
        {
            commandBuffer.SetComputeIntParams(
                compute,
                LightingComputeBinder.CompositeDispatchOriginId,
                compositeRect.x,
                compositeRect.y);
            commandBuffer.SetComputeIntParams(
                compute,
                LightingComputeBinder.CompositeDispatchSizeId,
                compositeRect.width,
                compositeRect.height);
            telemetry.LightingCompositeDispatchPixels += (long)compositeRect.width * compositeRect.height;
            commandBuffer.DispatchCompute(
                compute,
                kernel,
                LightingComputeBinder.DispatchGroups(compositeRect.width),
                LightingComputeBinder.DispatchGroups(compositeRect.height),
                1);
            telemetry.LightingCompositeTimeMs = ElapsedMs(compositeStart);
            commandBuffer.EndSample("Kern.Lighting.Composite");
            return;
        }

        commandBuffer.SetComputeIntParams(
            compute,
            LightingComputeBinder.CompositeDispatchOriginId,
            0,
            0);
        commandBuffer.SetComputeIntParams(
            compute,
            LightingComputeBinder.CompositeDispatchSizeId,
            lightWidth,
            lightHeight);
        telemetry.LightingCompositeDispatchPixels += (long)lightWidth * lightHeight;
        commandBuffer.DispatchCompute(
            compute,
            kernel,
            LightingComputeBinder.DispatchGroups(lightWidth),
            LightingComputeBinder.DispatchGroups(lightHeight),
            1);
        telemetry.LightingCompositeTimeMs = ElapsedMs(compositeStart);
        commandBuffer.EndSample("Kern.Lighting.Composite");
    }

    private static float ElapsedMs(long startTimestamp)
    {
        return (float)((System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) *
            1000.0 / System.Diagnostics.Stopwatch.Frequency);
    }

    private bool TryGetFieldRect(
        RectInt? fieldDirtyRect,
        Vector4 worldRect,
        float cellSize,
        out RectInt expanded)
    {
        expanded = default;
        if (!fieldDirtyRect.HasValue)
        {
            return false;
        }

        RectInt dirty = fieldDirtyRect.Value;
        if (dirty.width <= 0 || dirty.height <= 0)
        {
            return false;
        }

        int lightWidth = _resources.LightWidth;
        int lightHeight = _resources.LightHeight;
        int margin = ResolveFieldMargin(worldRect, cellSize);
        int minX = Mathf.Max(0, dirty.xMin - margin);
        int minY = Mathf.Max(0, dirty.yMin - margin);
        int maxX = Mathf.Min(lightWidth, dirty.xMax + margin);
        int maxY = Mathf.Min(lightHeight, dirty.yMax + margin);
        if (maxX <= minX || maxY <= minY)
        {
            return false;
        }

        expanded = new RectInt(minX, minY, maxX - minX, maxY - minY);
        return true;
    }

    private int ResolveFieldMargin(Vector4 worldRect, float cellSize)
    {
        float pixelsPerCell = 1f;
        if (worldRect.z > 0f && cellSize > 0f && _resources.LightWidth > 0)
        {
            pixelsPerCell = _resources.LightWidth * cellSize / worldRect.z;
        }

        return Mathf.CeilToInt(CompositeNeighborCells * Mathf.Max(1f, pixelsPerCell)) +
            PartialSlackTexels;
    }
}
