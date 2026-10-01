#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Lighting;

internal sealed class DynamicLightingSolver
{
    private readonly LightingResourceManager _resources;
    private readonly DynamicLightManager _lightManager;
    private readonly DynamicLightTileCache _tileCache;
    private RectInt[] _lightRects = new RectInt[1];
    private DynamicLightTileCache.TileInfo[] _lightTileInfos = new DynamicLightTileCache.TileInfo[1];
    private Vector2Int[] _lightRaySizes = new Vector2Int[1];
    private int[] _lightRequestedRayFans = new int[1];
    private bool[] _lightNeedsTrace = new bool[1];
    private int _previousLightCount;

    // Light ID -> receiver rectangle its tile currently contributes to
    // DirectTexture. A solve where only some lights changed recomposes just
    // their old and new rectangles: every other pixel's sum is unchanged.
    private readonly Dictionary<int, RectInt> _composedRects = new();
    private readonly HashSet<int> _currentLightIDs = new();
    private readonly List<int> _goneLightIDs = new();

    // True when the last Record changed no pixel of DirectTexture.
    public bool LastSolveUnchanged { get; private set; }

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
        _previousLightCount = 0;
        _composedRects.Clear();
    }

    public void Record(
        CommandBuffer commandBuffer,
        int lightCount,
        Vector4 worldRect,
        float cellSize,
        bool invalidateDynamicTiles,
        LightingEngine.DebugView debugView,
        IFrameTelemetry telemetry,
        out RectInt dynamicDirtyUnion,
        RectInt receiverRect)
    {
        using var dynamicSample = new CommandBufferSampleScope(commandBuffer, "Kern.Lighting.DynamicRadiance");
        long dynamicStart = System.Diagnostics.Stopwatch.GetTimestamp();
        LastSolveUnchanged = false;

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
            _composedRects.Clear();
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
        // Receiver rectangles, tiles, the compose union and polar rows are
        // light-lattice texels.
        float texelsPerWorldX = _resources.LightWidth / worldRect.z;
        float texelsPerWorldY = _resources.LightHeight / worldRect.w;
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
                int maxX = _resources.LightWidth;
                int maxY = _resources.LightHeight;
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
                    maxX = Mathf.Min(_resources.LightWidth, Mathf.CeilToInt((light.PositionRadius.x + halfExtent - worldRect.x) * texelsPerWorldX) + 1);
                    maxY = Mathf.Min(_resources.LightHeight, Mathf.CeilToInt((light.PositionRadius.y + halfExtent - worldRect.y) * texelsPerWorldY) + 1);
                }

                if (maxX > minX && maxY > minY)
                {
                    rect = new RectInt(minX, minY, maxX - minX, maxY - minY);
                }
            }

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
            // Rows are receiver texels: TraceDynamicPolar marches every
            // transport texel but stores one radial sample per light texel.
            int rayLength = Mathf.CeilToInt(farthest + (texelsPerWorldX + texelsPerWorldY) * cellSize) + 2;
            int requestedRayFan = Mathf.Max(
                1,
                Mathf.CeilToInt(2f * Mathf.PI * rayLength));
            _lightRequestedRayFans[lightIndex] = requestedRayFan;
            _lightRaySizes[lightIndex] = new Vector2Int(1, rayLength);
            int receiverMinX = Mathf.Max(rect.xMin, receiverRect.xMin);
            int receiverMinY = Mathf.Max(rect.yMin, receiverRect.yMin);
            int receiverMaxX = Mathf.Min(rect.xMax, receiverRect.xMax);
            int receiverMaxY = Mathf.Min(rect.yMax, receiverRect.yMax);
            RectInt applied = new(receiverMinX, receiverMinY,
                Mathf.Max(0, receiverMaxX - receiverMinX), Mathf.Max(0, receiverMaxY - receiverMinY));
            _lightRects[lightIndex] = applied;
            if (applied.width > 0 && applied.height > 0)
            {
                widestRect = Mathf.Max(widestRect, applied.width);
                tallestRect = Mathf.Max(tallestRect, applied.height);
                composeMinX = Mathf.Min(composeMinX, applied.xMin);
                composeMinY = Mathf.Min(composeMinY, applied.yMin);
                composeMaxX = Mathf.Max(composeMaxX, applied.xMax);
                composeMaxY = Mathf.Max(composeMaxY, applied.yMax);
            }
        }

        int longestRequestedRay = DynamicPolarWorkBudget.RequiredRayLength(count, _lightRaySizes);
        int layoutGeneration = _tileCache.LayoutGeneration;
        _tileCache.EnsureLayout(widestRect, tallestRect, count,
            LightingQualityTuningController.DynamicPolarDirectionCount, longestRequestedRay);
        if (invalidateDynamicTiles)
        {
            _tileCache.InvalidateAll();
        }

        _tileCache.AssignSlots(lightIDs.Slice(0, count));
        // Resize before evaluating cache validity: replacing this array loses
        // every layer's optical depth, including unchanged source slots.
        _tileCache.EnsurePolar(LightingQualityTuningController.DynamicPolarDirectionCount, longestRequestedRay);
        MarkLightsNeedingTrace(count, lights, lightIDs);

        int widestRayFan = DynamicPolarWorkBudget.AllocateRayFans(
            count,
            _lightRequestedRayFans,
            _lightNeedsTrace,
            _lightRaySizes,
            out int longestRay);

        // Several lights on a retained atlas: patch only what changed. One
        // light writes DirectTexture directly; a replaced atlas or a static
        // re-solve rebuilds the whole union.
        bool incremental = count > 1 && !invalidateDynamicTiles &&
            layoutGeneration == _tileCache.LayoutGeneration;
        bool hasDirty = false;
        RectInt dirtyBounds = default;
        if (incremental)
        {
            // Set after the trace loop from the lights that actually changed.
            dynamicDirtyUnion = default;
        }
        else if (invalidateDynamicTiles)
        {
            // The trace/compose pass replaces the whole direct texture when
            // current light bounds cover the field. Clearing it first only
            // adds a full-field write on the most expensive rebuild frames.
            bool currentCoversField = composeMinX <= 0 && composeMinY <= 0 &&
                composeMaxX >= _resources.LightWidth &&
                composeMaxY >= _resources.LightHeight;
            if (!currentCoversField)
            {
                ClearDynamicDirect(commandBuffer);
            }

            // A static re-solve takes the full composite path, so no partial
            // dirty rectangle applies.
            dynamicDirtyUnion = default;
        }
        else
        {
            bool hasCurrentRectUnion = composeMaxX > composeMinX && composeMaxY > composeMinY;
            RectInt clearRect = hasPreviousRectUnion
                ? previousRectUnion
                : default;
            RectInt currentRectUnion = default;
            if (hasCurrentRectUnion)
            {
                currentRectUnion = new RectInt(
                    composeMinX,
                    composeMinY,
                    composeMaxX - composeMinX,
                    composeMaxY - composeMinY);
                clearRect = hasPreviousRectUnion
                    ? Union(clearRect, currentRectUnion)
                    : currentRectUnion;
            }

            // ComposeDynamicLighting (or the single-light trace) overwrites
            // every pixel in the current union. Clear only when the old union
            // extends outside it, where stale light would otherwise remain.
            bool currentCoversPrevious = hasCurrentRectUnion &&
                (!hasPreviousRectUnion ||
                    (currentRectUnion.xMin <= previousRectUnion.xMin &&
                     currentRectUnion.yMin <= previousRectUnion.yMin &&
                     currentRectUnion.xMax >= previousRectUnion.xMax &&
                     currentRectUnion.yMax >= previousRectUnion.yMax));
            if (!currentCoversPrevious && clearRect.width > 0 && clearRect.height > 0)
            {
                ClearDynamicDirect(commandBuffer, clearRect);
            }

            // Composite must refresh both where the dynamic light was and
            // where it is: the cleared old area changed just as much as the
            // newly lit one. clearRect already is that union.
            dynamicDirtyUnion = clearRect;
        }

        _tileCache.EnsurePolar(widestRayFan, longestRay);

        ComputeShader compute = _resources.LightingCompute!;
        RenderTexture tiles = _tileCache.Tiles!;
        RenderTexture polarRays = _tileCache.Polar!;
        commandBuffer.SetComputeFloatParam(compute, LightingComputeBinder.InvisibleDynamicRadianceID,
            LightingComputeBinder.InvisibleDynamicRadiance);
        commandBuffer.SetComputeIntParams(
            compute,
            LightingComputeBinder.DynamicPolarTextureSizeID,
            polarRays.width,
            polarRays.height);
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
        ComputeBuffer horizon = _tileCache.Horizon!;
        int horizonStride = _tileCache.HorizonStride;
        commandBuffer.SetComputeIntParam(compute, LightingComputeBinder.DynamicHorizonStrideID, horizonStride);
        commandBuffer.SetComputeBufferParam(compute, traceKernel, LightingComputeBinder.DynamicHorizonInputID, horizon);
        commandBuffer.SetComputeBufferParam(compute, rayKernel, LightingComputeBinder.DynamicHorizonID, horizon);
        BindFieldTextures(commandBuffer, rayKernel, _resources.StaticEmissionField!);
        commandBuffer.SetComputeTextureParam(compute, rayKernel, LightingComputeBinder.DynamicPolarID, polarRays);
        commandBuffer.SetComputeBufferParam(compute, rayKernel, LightingComputeBinder.DynamicLightsID, _resources.DynamicLightBuffer!);
        commandBuffer.SetComputeTextureParam(
            compute,
            rayKernel,
            LightingComputeBinder.CellSolidMaskID,
            _resources.CellSolidMask!);

        _currentLightIDs.Clear();
        for (int lightIndex = 0; lightIndex < count; lightIndex++)
        {
            DynamicLightGpuData light = lights[lightIndex];
            RectInt rect = _lightRects[lightIndex];
            int lightID = lightIDs[lightIndex];
            _currentLightIDs.Add(lightID);
            int slot = _tileCache.SlotOf(lightID);
            Vector2Int tileOffset = Vector2Int.zero;
            _lightTileInfos[lightIndex] = new DynamicLightTileCache.TileInfo(rect, tileOffset, slot);
            bool wasComposed = _composedRects.TryGetValue(lightID, out RectInt composedRect);
            if (rect.width <= 0 || rect.height <= 0)
            {
                if (wasComposed)
                {
                    AddDirty(ref hasDirty, ref dirtyBounds, composedRect);
                    _composedRects.Remove(lightID);
                }
                continue;
            }

            if (!_tileCache.NeedsTrace(slot, light.PositionRadius, light.ColorIntensity, rect))
            {
                if (!wasComposed || !composedRect.Equals(rect))
                {
                    AddDirty(ref hasDirty, ref dirtyBounds, rect);
                    _composedRects[lightID] = rect;
                }
                continue;
            }

            if (wasComposed)
            {
                AddDirty(ref hasDirty, ref dirtyBounds, composedRect);
            }
            AddDirty(ref hasDirty, ref dirtyBounds, rect);
            _composedRects[lightID] = rect;

            Vector2Int raySize = _lightRaySizes[lightIndex];
            dynamicDispatchPixels += (long)rect.width * rect.height;
            bool tracePolar = _tileCache.NeedsPolarTrace(slot, light.PositionRadius, light.ColorIntensity);
            if (!tracePolar)
            {
                raySize = _tileCache.PolarSize(slot);
            }
            bool writeDynamicDirect = count == 1;
            commandBuffer.SetComputeIntParam(
                compute,
                LightingComputeBinder.WriteDynamicDirectID,
                writeDynamicDirect ? 1 : 0);
            commandBuffer.SetComputeIntParams(compute, LightingComputeBinder.DynamicPolarSizeID, raySize.x, raySize.y);
            commandBuffer.SetComputeIntParam(compute, LightingComputeBinder.DynamicLightIndexID, lightIndex);
            commandBuffer.SetComputeIntParam(compute, LightingComputeBinder.DynamicPolarLayerOffsetID,
                slot * LightingComputeBinder.DynamicEmitterPointCount);
            commandBuffer.SetComputeIntParam(compute, LightingComputeBinder.DynamicHorizonBaseID,
                slot * LightingComputeBinder.DynamicEmitterPointCount * horizonStride);

            if (tracePolar)
            {
                // Slot identity owns cached optical depth and its horizon. An
                // upload may reorder light indices without invalidating either.
                // Every fan thread overwrites its own horizon entry: no clear.
                commandBuffer.BeginSample("Kern.Lighting.DynamicPolar");
                commandBuffer.DispatchCompute(compute, rayKernel,
                    Mathf.CeilToInt(raySize.x / 64f), LightingComputeBinder.DynamicEmitterPointCount, 1);
                commandBuffer.EndSample("Kern.Lighting.DynamicPolar");
                _tileCache.MarkPolarTraced(slot, raySize);
                polarRayWorkUnits += (long)raySize.x * LightingComputeBinder.DynamicEmitterPointCount * raySize.y;
            }
            commandBuffer.SetComputeIntParam(compute, LightingComputeBinder.DynamicReachIndexID, slot);

            commandBuffer.SetComputeIntParams(compute, LightingComputeBinder.DynamicDispatchOriginID, rect.x, rect.y);
            commandBuffer.SetComputeIntParams(compute, LightingComputeBinder.DynamicDispatchSizeID, rect.width, rect.height);
            commandBuffer.SetComputeIntParams(compute, LightingComputeBinder.DynamicTileOffsetID, tileOffset.x, tileOffset.y);
            commandBuffer.SetComputeIntParam(compute, LightingComputeBinder.DynamicLightIndexID, lightIndex);
            commandBuffer.BeginSample("Kern.Lighting.DynamicReceiverTrace");
            commandBuffer.DispatchCompute(
                compute,
                traceKernel,
                LightingComputeBinder.DispatchGroups(rect.width),
                LightingComputeBinder.DispatchGroups(rect.height),
                1);
            commandBuffer.EndSample("Kern.Lighting.DynamicReceiverTrace");
            _tileCache.MarkTraced(slot, light.PositionRadius, light.ColorIntensity, rect);
            telemetry.LightingDynamicTraceCount++;
        }

        // Lights gone since the last solve leave their old area to recompose.
        _goneLightIDs.Clear();
        foreach (KeyValuePair<int, RectInt> composed in _composedRects)
        {
            if (!_currentLightIDs.Contains(composed.Key))
            {
                _goneLightIDs.Add(composed.Key);
                AddDirty(ref hasDirty, ref dirtyBounds, composed.Value);
            }
        }
        foreach (int goneID in _goneLightIDs)
        {
            _composedRects.Remove(goneID);
        }

        if (incremental)
        {
            // Compose writes every pixel of its rectangle, zeros included:
            // the old area of a moved or removed light needs no clear.
            composeMinX = dirtyBounds.xMin;
            composeMinY = dirtyBounds.yMin;
            composeMaxX = hasDirty ? dirtyBounds.xMax : composeMinX;
            composeMaxY = hasDirty ? dirtyBounds.yMax : composeMinY;
            dynamicDirtyUnion = hasDirty ? dirtyBounds : default;
            LastSolveUnchanged = !hasDirty;
        }

        // With one unchanged light, DirectTexture is already its retained
        // result. Only multiple-light layouts have radiance tiles to compose.
        if (count > 1 && composeMaxX > composeMinX && composeMaxY > composeMinY)
        {
            int composeKernel = _resources.ComposeDynamicLightingKernel;
            ComputeBuffer tileInfos = _tileCache.TileInfos!;
            commandBuffer.SetBufferData(tileInfos, _lightTileInfos, 0, 0, count);
            commandBuffer.SetComputeBufferParam(compute, composeKernel, LightingComputeBinder.DynamicTileInfosID, tileInfos);
            commandBuffer.SetComputeBufferParam(compute, composeKernel, LightingComputeBinder.DynamicLightsID, _resources.DynamicLightBuffer!);
            commandBuffer.SetComputeTextureParam(compute, composeKernel, LightingComputeBinder.DynamicTilesInputID, tiles);
            commandBuffer.SetComputeTextureParam(compute, composeKernel, LightingComputeBinder.DirectTextureID, _resources.DirectTexture!);
            commandBuffer.SetComputeIntParam(compute, LightingComputeBinder.DynamicTileCountID, count);
            int composeWidth = composeMaxX - composeMinX;
            int composeHeight = composeMaxY - composeMinY;
            telemetry.LightingDynamicComposePixels += (long)composeWidth * composeHeight;
            commandBuffer.SetComputeIntParams(compute, LightingComputeBinder.ComposeOriginID, composeMinX, composeMinY);
            commandBuffer.SetComputeIntParams(compute, LightingComputeBinder.ComposeSizeID, composeWidth, composeHeight);
            commandBuffer.BeginSample("Kern.Lighting.DynamicCompose");
            commandBuffer.DispatchCompute(
                compute,
                composeKernel,
                LightingComputeBinder.DispatchGroups(composeWidth),
                LightingComputeBinder.DispatchGroups(composeHeight),
                1);
            commandBuffer.EndSample("Kern.Lighting.DynamicCompose");
        }

        telemetry.LightingDynamicDispatchPixels += dynamicDispatchPixels;
        telemetry.LightingPolarRayWorkUnits += polarRayWorkUnits;
        telemetry.LightingDynamicLightingTimeMs = ElapsedMs(dynamicStart);

        _previousLightCount = count;
    }

    private static void AddDirty(ref bool hasDirty, ref RectInt bounds, RectInt rect)
    {
        if (rect.width <= 0 || rect.height <= 0)
        {
            return;
        }

        bounds = hasDirty ? Union(bounds, rect) : rect;
        hasDirty = true;
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
                _lightRects[lightIndex]) && _tileCache.NeedsPolarTrace(
                    slot, lights[lightIndex].PositionRadius, lights[lightIndex].ColorIntensity);
        }
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
