#nullable enable

using System;
using Kern.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Lighting;

internal sealed class DynamicLightingSolver
{
    // Polar tracing is required for every moved dynamic light, but one texel-wide ray
    // fan at the edge of a large field creates a quadratic-looking burst:
    // angles * emitter points * ray length. This is a frame-wide budget, not a
    // per-light budget: otherwise N visible dynamic lights multiply the supposed cap by
    // N and walking past a busy area creates a burst. The budget is shared
    // only by lights that need a trace this frame; static lights reuse their
    // tiles and must not dilute it.
    private const long MaximumPolarRayWorkUnits = 2_000_000;

    private readonly LightingResourceManager _resources;
    private readonly DynamicLightManager _lightManager;
    private readonly DynamicLightTileCache _tileCache;
    private RectInt[] _lightRects = new RectInt[1];
    private DynamicLightTileCache.TileInfo[] _lightTileInfos = new DynamicLightTileCache.TileInfo[1];
    private Vector2Int[] _lightRaySizes = new Vector2Int[1];
    private int[] _lightRequestedRayFans = new int[1];
    private bool[] _lightNeedsTrace = new bool[1];
    private int _previousLightCount;

    public DynamicLightingSolver(
        LightingResourceManager resources,
        DynamicLightManager lightManager,
        DynamicLightTileCache tileCache)
    {
        _resources = resources;
        _lightManager = lightManager;
        _tileCache = tileCache;
    }

    public void InvalidateTiles()
    {
        _tileCache.InvalidateAll();
    }

    public void Release()
    {
        _tileCache.Release();
    }

    public void Record(
        CommandBuffer commandBuffer,
        int lightCount,
        Vector4 worldRect,
        float cellSize,
        bool invalidateDynamicTiles,
        LightingEngine.DebugView debugView,
        IFrameTelemetry telemetry,
        out RectInt dynamicDirtyUnion)
    {
        commandBuffer.BeginSample("Kern.Lighting.DynamicRadiance");
        using var dynamicSample = new CommandBufferSampleScope(commandBuffer, "Kern.Lighting.DynamicRadiance");
        long dynamicStart = System.Diagnostics.Stopwatch.GetTimestamp();

        bool hasPreviousRectUnion = TryGetRectUnion(
            _lightRects,
            _previousLightCount,
            out RectInt previousRectUnion);

        // The transmission debug view reads the static component only.
        if (debugView == LightingEngine.DebugView.Transmission)
        {
            ClearDynamicDirect(commandBuffer);
            _tileCache.InvalidateAll();
            telemetry.LightingDynamicLightingTimeMs = ElapsedMs(dynamicStart);
            dynamicDirtyUnion = default;
            // The full clear wiped every previous rect: the next frame must
            // not re-clear a stale previous union.
            _previousLightCount = 0;
            return;
        }

        System.ReadOnlySpan<DynamicLightGpuData> lights = _lightManager.UploadedLights;
        System.ReadOnlySpan<int> lightIDs = _lightManager.UploadedLightIDs;
        int count = Mathf.Min(lightCount, lights.Length);
        if (_lightRects.Length < count)
        {
            Array.Resize(ref _lightRects, count);
            Array.Resize(ref _lightTileInfos, count);
            Array.Resize(ref _lightRaySizes, count);
            Array.Resize(ref _lightRequestedRayFans, count);
            Array.Resize(ref _lightNeedsTrace, count);
        }

        long dynamicDispatchPixels = 0;
        long polarRayWorkUnits = 0;

        float minimumExtinction = LightingComputeBinder.ResolveMinimumExtinction();
        float texelsPerWorldX = _resources.FieldWidth / worldRect.z;
        float texelsPerWorldY = _resources.FieldHeight / worldRect.w;
        int widestRect = 1;
        int tallestRect = 1;
        int composeMinX = int.MaxValue;
        int composeMinY = int.MaxValue;
        int composeMaxX = int.MinValue;
        int composeMaxY = int.MinValue;
        for (int lightIndex = 0; lightIndex < count; lightIndex++)
        {
            DynamicLightGpuData light = lights[lightIndex];
            float brightest = Mathf.Max(
                0f,
                Mathf.Max(light.ColorIntensity.x, Mathf.Max(light.ColorIntensity.y, light.ColorIntensity.z)) *
                    light.ColorIntensity.w) * LightingConfigHolder.EmissionScale;
            RectInt rect = default;
            if (brightest > 0f)
            {
                int minX = 0;
                int minY = 0;
                int maxX = _resources.FieldWidth;
                int maxY = _resources.FieldHeight;
                if (minimumExtinction > 0f)
                {
                    float reachCells = Mathf.Max(
                        0f,
                        Mathf.Log(brightest * 1.5f / LightingComputeBinder.InvisibleDynamicRadiance) /
                            minimumExtinction);
                    float halfExtent = (0.5f + reachCells) * cellSize;
                    // One texel of margin against rounding of the rectangle edge.
                    minX = Mathf.Max(0, Mathf.FloorToInt((light.PositionRadius.x - halfExtent - worldRect.x) * texelsPerWorldX) - 1);
                    minY = Mathf.Max(0, Mathf.FloorToInt((light.PositionRadius.y - halfExtent - worldRect.y) * texelsPerWorldY) - 1);
                    maxX = Mathf.Min(_resources.FieldWidth, Mathf.CeilToInt((light.PositionRadius.x + halfExtent - worldRect.x) * texelsPerWorldX) + 1);
                    maxY = Mathf.Min(_resources.FieldHeight, Mathf.CeilToInt((light.PositionRadius.y + halfExtent - worldRect.y) * texelsPerWorldY) + 1);
                }

                if (maxX > minX && maxY > minY)
                {
                    rect = new RectInt(minX, minY, maxX - minX, maxY - minY);
                    widestRect = Mathf.Max(widestRect, rect.width);
                    tallestRect = Mathf.Max(tallestRect, rect.height);
                    composeMinX = Mathf.Min(composeMinX, rect.xMin);
                    composeMinY = Mathf.Min(composeMinY, rect.yMin);
                    composeMaxX = Mathf.Max(composeMaxX, rect.xMax);
                    composeMaxY = Mathf.Max(composeMaxY, rect.yMax);
                }
            }

            _lightRects[lightIndex] = rect;

            // Dynamic-centred rays long enough to reach every corner of the
            // rectangle. Angular density is bounded by the complete polar
            // work budget below; the receiver interpolates between rays.
            Vector2 rayCenter = new(
                (light.PositionRadius.x - worldRect.x) * texelsPerWorldX,
                (light.PositionRadius.y - worldRect.y) * texelsPerWorldY);
            float farthest = 0f;
            if (rect.width > 0)
            {
                farthest = Mathf.Max(
                    Vector2.Distance(rayCenter, new Vector2(rect.xMin, rect.yMin)),
                    Mathf.Max(
                        Vector2.Distance(rayCenter, new Vector2(rect.xMax, rect.yMin)),
                        Mathf.Max(
                            Vector2.Distance(rayCenter, new Vector2(rect.xMin, rect.yMax)),
                            Vector2.Distance(rayCenter, new Vector2(rect.xMax, rect.yMax)))));
            }

            // Fans start at emitter points anywhere in the dynamic light cell.
            int rayLength = Mathf.CeilToInt(farthest + (texelsPerWorldX + texelsPerWorldY) * cellSize) + 2;
            int requestedRayFan = Mathf.Max(
                1,
                Mathf.CeilToInt(2f * Mathf.PI * rayLength));
            _lightRequestedRayFans[lightIndex] = requestedRayFan;
            _lightRaySizes[lightIndex] = new Vector2Int(1, rayLength);
        }

        _tileCache.EnsureLayout(widestRect, tallestRect, count);
        if (invalidateDynamicTiles)
        {
            _tileCache.InvalidateAll();
        }

        _tileCache.AssignSlots(lightIDs.Slice(0, count));
        MarkLightsNeedingTrace(count, lights, lightIDs);

        int widestRayFan = AllocatePolarRayFans(count, out int longestRay);

        if (invalidateDynamicTiles)
        {
            ClearDynamicDirect(commandBuffer);
            // Full clear with a static re-solve following: the frame takes
            // the full bounce/composite path, so no partial rect applies.
            dynamicDirtyUnion = default;
        }
        else
        {
            bool hasCurrentRectUnion = composeMaxX > composeMinX && composeMaxY > composeMinY;
            RectInt clearRect = hasPreviousRectUnion
                ? previousRectUnion
                : default;
            if (hasCurrentRectUnion)
            {
                RectInt currentRectUnion = new(
                    composeMinX,
                    composeMinY,
                    composeMaxX - composeMinX,
                    composeMaxY - composeMinY);
                clearRect = hasPreviousRectUnion
                    ? Union(clearRect, currentRectUnion)
                    : currentRectUnion;
            }

            if (clearRect.width > 0 && clearRect.height > 0)
            {
                ClearDynamicDirect(commandBuffer, clearRect);
            }

            // Bounce and composite must refresh both where the dynamic light was and
            // where it is: the cleared old area changed just as much as the
            // newly lit one. clearRect already is that union.
            dynamicDirtyUnion = clearRect;
        }

        _tileCache.EnsurePolar(widestRayFan, longestRay * LightingComputeBinder.DynamicEmitterPointCount);

        ComputeShader compute = _resources.LightingCompute!;
        RenderTexture tiles = _tileCache.Tiles!;
        RenderTexture polarRays = _tileCache.Polar!;
        int traceKernel = _resources.SolveDynamicLightingKernel;
        BindFieldTextures(commandBuffer, traceKernel, _resources.StaticEmissionField!);
        commandBuffer.SetComputeBufferParam(compute, traceKernel, LightingComputeBinder.DynamicLightsID, _resources.DynamicLightBuffer!);
        commandBuffer.SetComputeTextureParam(compute, traceKernel, LightingComputeBinder.DynamicTilesID, tiles);
        commandBuffer.SetComputeTextureParam(compute, traceKernel, LightingComputeBinder.DynamicPolarInputID, polarRays);
        commandBuffer.SetComputeTextureParam(
            compute,
            traceKernel,
            LightingComputeBinder.DirectTextureID,
            _resources.DirectTexture!);
        commandBuffer.SetComputeTextureParam(
            compute,
            traceKernel,
            LightingComputeBinder.CellSolidMaskID,
            _resources.CellSolidMask!);
        int rayKernel = _resources.TraceDynamicPolarKernel;
        BindFieldTextures(commandBuffer, rayKernel, _resources.StaticEmissionField!);
        commandBuffer.SetComputeTextureParam(compute, rayKernel, LightingComputeBinder.DynamicPolarID, polarRays);
        commandBuffer.SetComputeBufferParam(compute, rayKernel, LightingComputeBinder.DynamicLightsID, _resources.DynamicLightBuffer!);
        commandBuffer.SetComputeTextureParam(
            compute,
            rayKernel,
            LightingComputeBinder.CellSolidMaskID,
            _resources.CellSolidMask!);

        bool singleLightDirectWritten = false;
        for (int lightIndex = 0; lightIndex < count; lightIndex++)
        {
            DynamicLightGpuData light = lights[lightIndex];
            RectInt rect = _lightRects[lightIndex];
            int slot = _tileCache.SlotOf(lightIDs[lightIndex]);
            Vector2Int tileOffset = _tileCache.TileOffset(slot);
            _lightTileInfos[lightIndex] = new DynamicLightTileCache.TileInfo(rect, tileOffset);
            if (rect.width <= 0 ||
                !_tileCache.NeedsTrace(slot, light.PositionRadius, light.ColorIntensity, rect))
            {
                continue;
            }

            Vector2Int raySize = _lightRaySizes[lightIndex];
            dynamicDispatchPixels += (long)rect.width * rect.height;
            polarRayWorkUnits += (long)raySize.x *
                LightingComputeBinder.DynamicEmitterPointCount * raySize.y;
            bool writeDynamicDirect = count == 1;
            commandBuffer.SetComputeIntParam(
                compute,
                LightingComputeBinder.WriteDynamicDirectID,
                writeDynamicDirect ? 1 : 0);
            singleLightDirectWritten |= writeDynamicDirect;
            commandBuffer.SetComputeIntParams(compute, LightingComputeBinder.DynamicPolarSizeID, raySize.x, raySize.y);
            commandBuffer.SetComputeIntParam(compute, LightingComputeBinder.DynamicLightIndexID, lightIndex);
            for (int point = 0; point < LightingComputeBinder.DynamicEmitterPointCount; point++)
            {
                commandBuffer.SetComputeIntParam(compute, LightingComputeBinder.DynamicPolarPointID, point);
                commandBuffer.DispatchCompute(compute, rayKernel, Mathf.CeilToInt(raySize.x / 64f), 1, 1);
            }

            commandBuffer.SetComputeIntParams(compute, LightingComputeBinder.DynamicDispatchOriginID, rect.x, rect.y);
            commandBuffer.SetComputeIntParams(compute, LightingComputeBinder.DynamicDispatchSizeID, rect.width, rect.height);
            commandBuffer.SetComputeIntParams(compute, LightingComputeBinder.DynamicTileOffsetID, tileOffset.x, tileOffset.y);
            commandBuffer.SetComputeIntParam(compute, LightingComputeBinder.DynamicLightIndexID, lightIndex);
            commandBuffer.DispatchCompute(
                compute,
                traceKernel,
                LightingComputeBinder.DispatchGroups(rect.width),
                LightingComputeBinder.DispatchGroups(rect.height),
                1);
            _tileCache.MarkTraced(slot, light.PositionRadius, light.ColorIntensity, rect);
            telemetry.LightingDynamicTraceCount++;
        }

        if (!singleLightDirectWritten && composeMaxX > composeMinX && composeMaxY > composeMinY)
        {
            int composeKernel = _resources.ComposeDynamicLightingKernel;
            ComputeBuffer tileInfos = _tileCache.TileInfos!;
            commandBuffer.SetBufferData(tileInfos, _lightTileInfos, 0, 0, count);
            commandBuffer.SetComputeBufferParam(compute, composeKernel, LightingComputeBinder.DynamicTileInfosID, tileInfos);
            commandBuffer.SetComputeTextureParam(compute, composeKernel, LightingComputeBinder.DynamicTilesInputID, tiles);
            commandBuffer.SetComputeTextureParam(compute, composeKernel, LightingComputeBinder.DirectTextureID, _resources.DirectTexture!);
            commandBuffer.SetComputeIntParam(compute, LightingComputeBinder.DynamicTileCountID, count);
            int composeWidth = composeMaxX - composeMinX;
            int composeHeight = composeMaxY - composeMinY;
            telemetry.LightingDynamicComposePixels += (long)composeWidth * composeHeight;
            commandBuffer.SetComputeIntParams(compute, LightingComputeBinder.ComposeOriginID, composeMinX, composeMinY);
            commandBuffer.SetComputeIntParams(compute, LightingComputeBinder.ComposeSizeID, composeWidth, composeHeight);
            commandBuffer.DispatchCompute(
                compute,
                composeKernel,
                LightingComputeBinder.DispatchGroups(composeWidth),
                LightingComputeBinder.DispatchGroups(composeHeight),
                1);
        }

        telemetry.LightingDynamicDispatchPixels += dynamicDispatchPixels;
        telemetry.LightingPolarRayWorkUnits += polarRayWorkUnits;
        telemetry.LightingDynamicLightingTimeMs = ElapsedMs(dynamicStart);

        _previousLightCount = count;
    }

    private static float ElapsedMs(long startTimestamp)
    {
        return (float)((System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) *
            1000.0 / System.Diagnostics.Stopwatch.Frequency);
    }

    private void ClearDynamicDirect(CommandBuffer commandBuffer)
    {
        commandBuffer.SetRenderTarget(_resources.DirectTexture!);
        commandBuffer.ClearRenderTarget(
            clearDepth: false,
            clearColor: true,
            backgroundColor: Color.clear);
    }

    private void ClearDynamicDirect(CommandBuffer commandBuffer, RectInt rect)
    {
        ComputeShader compute = _resources.LightingCompute!;
        int kernel = _resources.ClearDynamicDirectKernel;
        commandBuffer.SetComputeTextureParam(
            compute,
            kernel,
            LightingComputeBinder.DirectTextureID,
            _resources.DirectTexture!);
        commandBuffer.SetComputeIntParams(
            compute,
            LightingComputeBinder.DynamicDispatchOriginID,
            rect.x,
            rect.y);
        commandBuffer.SetComputeIntParams(
            compute,
            LightingComputeBinder.DynamicDispatchSizeID,
            rect.width,
            rect.height);
        commandBuffer.DispatchCompute(
            compute,
            kernel,
            LightingComputeBinder.DispatchGroups(rect.width),
            LightingComputeBinder.DispatchGroups(rect.height),
            1);
    }

    private static bool TryGetRectUnion(
        RectInt[] rects,
        int count,
        out RectInt union)
    {
        union = default;
        bool found = false;
        int boundedCount = Mathf.Min(count, rects.Length);
        for (int index = 0; index < boundedCount; index++)
        {
            RectInt rect = rects[index];
            if (rect.width <= 0 || rect.height <= 0)
            {
                continue;
            }

            union = found ? Union(union, rect) : rect;
            found = true;
        }

        return found;
    }

    private static RectInt Union(RectInt left, RectInt right)
    {
        int minX = Mathf.Min(left.xMin, right.xMin);
        int minY = Mathf.Min(left.yMin, right.yMin);
        int maxX = Mathf.Max(left.xMax, right.xMax);
        int maxY = Mathf.Max(left.yMax, right.yMax);
        return new RectInt(minX, minY, maxX - minX, maxY - minY);
    }

    // The polar work budget is shared only by lights that actually need a
    // trace this frame. Static lights reuse their tiles: letting them dilute
    // the budget would starve the one moving light of angular density.
    private void MarkLightsNeedingTrace(
        int count,
        System.ReadOnlySpan<DynamicLightGpuData> lights,
        System.ReadOnlySpan<int> lightIDs)
    {
        for (int lightIndex = 0; lightIndex < count; lightIndex++)
        {
            int slot = _tileCache.SlotOf(lightIDs[lightIndex]);
            _lightNeedsTrace[lightIndex] = _tileCache.NeedsTrace(
                slot,
                lights[lightIndex].PositionRadius,
                lights[lightIndex].ColorIntensity,
                _lightRects[lightIndex]);
        }
    }

    private int AllocatePolarRayFans(int count, out int longestRay)
    {
        int maxTextureSize = SystemInfo.maxTextureSize;
        int maxRayLength = Mathf.Max(1, maxTextureSize / LightingComputeBinder.DynamicEmitterPointCount);

        long requestedWork = 0;
        for (int lightIndex = 0; lightIndex < count; lightIndex++)
        {
            if (!_lightNeedsTrace[lightIndex])
            {
                continue;
            }

            Vector2Int raySize = _lightRaySizes[lightIndex];
            requestedWork += (long)_lightRequestedRayFans[lightIndex] *
                LightingComputeBinder.DynamicEmitterPointCount *
                Mathf.Max(1, raySize.y);
        }

        long budget = MaximumPolarRayWorkUnits;
        int widestRayFan = 1;
        longestRay = 1;
        for (int lightIndex = 0; lightIndex < count; lightIndex++)
        {
            if (!_lightNeedsTrace[lightIndex])
            {
                _lightRaySizes[lightIndex] = new Vector2Int(1, 1);
                continue;
            }

            int requested = Mathf.Max(1, _lightRequestedRayFans[lightIndex]);
            // The fan sets angular density only; the stored length is capped
            // to the polar texture limit. Receivers past the cap reuse the
            // edge depth through the Clamp sampler: bounded degradation that
            // cannot crash the frame, unlike an oversized allocation.
            int rayLength = Mathf.Min(Mathf.Max(1, _lightRaySizes[lightIndex].y), maxRayLength);
            int rayFan = requested;
            if (requestedWork > budget)
            {
                long weightedBudget = budget * requested;
                long weightedWork = requestedWork;
                rayFan = Mathf.Clamp(
                    (int)(weightedBudget / Mathf.Max(1L, weightedWork)),
                    1,
                    requested);
            }

            rayFan = Mathf.Min(rayFan, maxTextureSize);
            _lightRaySizes[lightIndex] = new Vector2Int(rayFan, rayLength);
            widestRayFan = Mathf.Max(widestRayFan, rayFan);
            longestRay = Mathf.Max(longestRay, rayLength);
        }

        return widestRayFan;
    }

    private void BindFieldTextures(
        CommandBuffer commandBuffer,
        int kernel,
        RenderTexture emissionField)
    {
        LightingComputeBinder.BindFieldTextures(
            commandBuffer,
            _resources.LightingCompute!,
            kernel,
            _resources.MaterialField!,
            emissionField,
            _resources.LightingCounters);
    }
}
