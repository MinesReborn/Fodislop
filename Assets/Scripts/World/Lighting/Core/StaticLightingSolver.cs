#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;
using Kern.Core;
using static Kern.World.Lighting.StaticLightingDirty;

namespace Kern.World.Lighting;

/// <summary>
/// Records static radiance cascade tracing and atlas resolve commands.
/// Resource lifetime remains owned by <see cref="LightingResourceManager"/>.
/// </summary>
internal sealed class StaticLightingSolver
{
    private const int MaximumDispatchGroupsPerDimension = 65535;

    private static readonly ProfilerMarker _CascadeMarker =
        new("Kern.Lighting.Cascades.Record.CPU");
    private static readonly ProfilerMarker _ResolveMarker =
        new("Kern.Lighting.Resolve.Record.CPU");

    private readonly LightingResourceManager _resources;
    private readonly IFrameTelemetry _telemetry;

    public StaticLightingSolver(
        LightingResourceManager resources,
        IFrameTelemetry telemetry)
    {
        _resources = resources;
        _telemetry = telemetry;
    }

    public void RecordTrace(
        CommandBuffer commandBuffer,
        RenderTexture emissionField,
        bool reuseOverlap,
        Vector2Int regionDelta,
        IReadOnlyList<RectInt> dirtyRegions,
        bool allowDependencyMask,
        Vector4 worldRect)
    {
        using var cascadeMarker = _CascadeMarker.Auto();
        long traceStart = System.Diagnostics.Stopwatch.GetTimestamp();
        commandBuffer.BeginSample("Kern.Lighting.RadianceCascades");
        using var radianceCascadesSample = new CommandBufferSampleScope(commandBuffer, "Kern.Lighting.RadianceCascades");
        ComputeShader compute = _resources.LightingCompute!;
        int solveKernel = _resources.SolveCascadeKernel;
        bool useDependencyMask = allowDependencyMask &&
            !reuseOverlap &&
            ShouldUseDependencyMask(dirtyRegions, worldRect);

        // Scroll keeps every entry whose rays cannot touch uncovered strips.
        // A misaligned probe lattice anywhere falls back to a full solve
        // rather than smearing phases across the atlas.
        Vector2Int[]? scrollDeltas = reuseOverlap ? TryResolveScrollDeltas(regionDelta) : null;
        if (useDependencyMask)
        {
            _telemetry.LightingStaticDependencyMaskSolveCount++;
        }
        else if (dirtyRegions.Count > 0)
        {
            _telemetry.LightingStaticDenseFallbackCount++;
        }
        bool needDirtyRegions = useDependencyMask ||
            (scrollDeltas != null && dirtyRegions.Count > 0);
        DirtyRegionGpu[] dirtyFieldRegions = needDirtyRegions
            ? StaticLightingDirty.ConvertDirtyRegions(
                dirtyRegions,
                worldRect,
                _resources.FieldWidth,
                _resources.FieldHeight)
            : Array.Empty<DirtyRegionGpu>();
        _resources.EnsureDirtyRegionCapacity(Mathf.Max(1, dirtyFieldRegions.Length));
        if (useDependencyMask)
        {
            _resources.DirtyRegions!.SetData(dirtyFieldRegions);
        }

        if (scrollDeltas != null)
        {
            RecordScroll(commandBuffer, compute, regionDelta);
            _resources.SwapRadianceAtlases();
        }

        commandBuffer.SetComputeBufferParam(compute, solveKernel,
            LightingComputeBinder.RadianceAtlasID, _resources.RadianceAtlas!);
        commandBuffer.SetComputeTextureParam(compute, solveKernel,
            LightingComputeBinder.CellSolidMaskID, _resources.CellSolidMask!);

        for (int cascadeIndex = _resources.Cascades.Count - 1;
             cascadeIndex >= 0;
             cascadeIndex--)
        {
            CascadeLayout cascade = _resources.Cascades[cascadeIndex];
            if (scrollDeltas != null)
            {
                RectInt dirtyProbeRect = default;
                if (dirtyFieldRegions.Length > 0)
                {
                    ProbeRect tight = StaticLightingDirty.TightProbeRect(
                        cascade,
                        dirtyFieldRegions,
                        worldRect,
                        _resources.FieldWidth,
                        _resources.FieldHeight);
                    if (!tight.IsEmpty)
                    {
                        dirtyProbeRect = new RectInt(tight.X, tight.Y, tight.Width, tight.Height);
                    }
                }

                RecordCascadeMoveTier(
                    commandBuffer,
                    compute,
                    solveKernel,
                    cascadeIndex,
                    emissionField,
                    scrollDeltas[cascadeIndex],
                    dirtyProbeRect);
            }
            else
            {
                RectInt probeRect = new RectInt(0, 0, cascade.ProbeWidth, cascade.ProbeHeight);
                if (useDependencyMask && dirtyFieldRegions.Length > 0)
                {
                    // Маска вже економить DDA через early-out, але треди
                    // слались на всю сітку. Тайт-rect ріже і треди: записи
                    // телеметрії full/partial нижче це покажуть.
                    ProbeRect tight = StaticLightingDirty.TightProbeRect(
                        cascade,
                        dirtyFieldRegions,
                        worldRect,
                        _resources.FieldWidth,
                        _resources.FieldHeight);
                    if (tight.IsEmpty)
                    {
                        continue;
                    }

                    probeRect = new RectInt(tight.X, tight.Y, tight.Width, tight.Height);
                }

                RecordCascade(
                    commandBuffer,
                    compute,
                    solveKernel,
                    cascadeIndex,
                    emissionField,
                    probeRect,
                    useDependencyMask,
                    dirtyFieldRegions.Length);
            }
        }

        _telemetry.LightingCascadeTraceTimeMs =
            (float)((System.Diagnostics.Stopwatch.GetTimestamp() - traceStart) *
                1000.0 / System.Diagnostics.Stopwatch.Frequency);
    }

    private void RecordScroll(
        CommandBuffer commandBuffer,
        ComputeShader compute,
        Vector2Int regionDelta)
    {
        _resources.EnsureScratchAtlas();
        _telemetry.LightingAtlasScrollCount++;
        ComputeBuffer input = _resources.RadianceAtlas!;
        ComputeBuffer output = _resources.RadianceScratchAtlas!;
        commandBuffer.SetComputeBufferParam(
            compute,
            _resources.ScrollRadianceAtlasKernel,
            LightingComputeBinder.RadianceAtlasInputID,
            input);
        commandBuffer.SetComputeBufferParam(
            compute,
            _resources.ScrollRadianceAtlasKernel,
            LightingComputeBinder.RadianceAtlasOutputID,
            output);

        foreach (CascadeLayout cascade in _resources.Cascades)
        {
            int deltaX = LightingComputeBinder.ResolveCascadeScrollDelta(
                regionDelta.x, _resources.FieldWidth, _resources.CellGridWidth, cascade.ProbeSpacing);
            int deltaY = LightingComputeBinder.ResolveCascadeScrollDelta(
                regionDelta.y, _resources.FieldHeight, _resources.CellGridHeight, cascade.ProbeSpacing);
            int overlapWidth = Mathf.Max(0, cascade.ProbeWidth - Mathf.Abs(deltaX));
            int overlapHeight = Mathf.Max(0, cascade.ProbeHeight - Mathf.Abs(deltaY));
            long reusedEntries = (long)overlapWidth * overlapHeight * cascade.DirectionCount;
            _telemetry.LightingAtlasReusedEntries += reusedEntries;
            _telemetry.LightingAtlasClearedEntries += cascade.EntryCount - reusedEntries;
            commandBuffer.SetComputeIntParam(
                compute,
                LightingComputeBinder.ScrollCascadeOffsetID,
                cascade.Offset);
            commandBuffer.SetComputeIntParam(
                compute,
                LightingComputeBinder.ScrollCascadeEntryCountID,
                cascade.EntryCount);
            commandBuffer.SetComputeIntParams(
                compute,
                LightingComputeBinder.ScrollProbeSizeID,
                cascade.ProbeWidth,
                cascade.ProbeHeight);
            commandBuffer.SetComputeIntParam(
                compute,
                LightingComputeBinder.ScrollDirectionCountID,
                cascade.DirectionCount);
            commandBuffer.SetComputeIntParams(
                compute,
                LightingComputeBinder.ScrollDeltaProbesID,
                deltaX,
                deltaY);

            int groups = Mathf.CeilToInt(cascade.EntryCount / 64f);
            int groupCountX = Mathf.Min(MaximumDispatchGroupsPerDimension, groups);
            commandBuffer.SetComputeIntParam(
                compute,
                LightingComputeBinder.CascadeDispatchRowWidthID,
                groupCountX * 64);
            commandBuffer.DispatchCompute(
                compute,
                _resources.ScrollRadianceAtlasKernel,
                groupCountX,
                Mathf.CeilToInt(groups / (float)groupCountX),
                1);
        }
    }

    // Scroll is valid only when every tier's probe lattice keeps its world
    // phase: the cell delta must translate to whole probes at each tier's
    // spacing. Governor moves come in whole quanta at an integer texel scale,
    // so this holds for ordinary movement; anything else returns null and the
    // caller falls back to a full solve.
    private Vector2Int[]? TryResolveScrollDeltas(Vector2Int regionDelta)
    {
        var scrollDeltas = new Vector2Int[_resources.Cascades.Count];
        try
        {
            for (int cascadeIndex = 0; cascadeIndex < _resources.Cascades.Count; cascadeIndex++)
            {
                CascadeLayout cascade = _resources.Cascades[cascadeIndex];
                scrollDeltas[cascadeIndex] = new Vector2Int(
                    LightingComputeBinder.ResolveCascadeScrollDelta(
                        regionDelta.x, _resources.FieldWidth, _resources.CellGridWidth, cascade.ProbeSpacing),
                    LightingComputeBinder.ResolveCascadeScrollDelta(
                        regionDelta.y, _resources.FieldHeight, _resources.CellGridHeight, cascade.ProbeSpacing));
            }
        }
        catch (ArgumentException)
        {
            return null;
        }

        return scrollDeltas;
    }

    private void RecordCascadeMoveTier(
        CommandBuffer commandBuffer,
        ComputeShader compute,
        int solveKernel,
        int cascadeIndex,
        RenderTexture emissionField,
        Vector2Int scrollDelta,
        RectInt dirtyProbeRect)
    {
        CascadeLayout cascade = _resources.Cascades[cascadeIndex];
        int probeW = cascade.ProbeWidth;
        int probeH = cascade.ProbeHeight;
        if (probeW <= 0 || probeH <= 0)
        {
            return;
        }

        // Kept entries stay valid unless their rays (up to the tier interval)
        // can touch uncovered strips: fresh bands on the leading edge, or the
        // dropped bands' far side on the trailing edge. Solving the strips
        // dilated by the interval refreshes exactly the suspect zone; the old
        // exact-strip solve left a stale fringe of interval width at every
        // border, which was the seam regression that disabled this path. Far
        // tiers dilate to the whole grid and solve full, where they are
        // cheapest anyway.
        int marginProbes = Mathf.CeilToInt(
            cascade.IntervalEnd / Mathf.Max(1, cascade.ProbeSpacing)) + 1;

        if (scrollDelta.x != 0)
        {
            int leadW = Mathf.Min(Mathf.Abs(scrollDelta.x), probeW);
            int leadX = scrollDelta.x > 0 ? probeW - leadW : 0;
            int trailW = Mathf.Min(marginProbes, probeW);
            int trailX = scrollDelta.x > 0 ? 0 : probeW - trailW;
            RecordCascade(
                commandBuffer,
                compute,
                solveKernel,
                cascadeIndex,
                emissionField,
                ClipProbeRect(
                    Mathf.Min(leadX, trailX) - marginProbes,
                    0,
                    Mathf.Max(leadX + leadW, trailX + trailW) - Mathf.Min(leadX, trailX) + (marginProbes * 2),
                    probeH,
                    probeW,
                    probeH),
                false,
                0);
        }

        if (scrollDelta.y != 0)
        {
            int leadH = Mathf.Min(Mathf.Abs(scrollDelta.y), probeH);
            int leadY = scrollDelta.y > 0 ? probeH - leadH : 0;
            int trailH = Mathf.Min(marginProbes, probeH);
            int trailY = scrollDelta.y > 0 ? 0 : probeH - trailH;
            RecordCascade(
                commandBuffer,
                compute,
                solveKernel,
                cascadeIndex,
                emissionField,
                ClipProbeRect(
                    0,
                    Mathf.Min(leadY, trailY) - marginProbes,
                    probeW,
                    Mathf.Max(leadY + leadH, trailY + trailH) - Mathf.Min(leadY, trailY) + (marginProbes * 2),
                    probeW,
                    probeH),
                false,
                0);
        }

        if (dirtyProbeRect.width > 0 && dirtyProbeRect.height > 0)
        {
            RecordCascade(
                commandBuffer,
                compute,
                solveKernel,
                cascadeIndex,
                emissionField,
                ClipProbeRect(
                    dirtyProbeRect.x,
                    dirtyProbeRect.y,
                    dirtyProbeRect.width,
                    dirtyProbeRect.height,
                    probeW,
                    probeH),
                false,
                0);
        }
    }

    private static RectInt ClipProbeRect(int x, int y, int width, int height, int probeW, int probeH)
    {
        int minX = Mathf.Max(0, x);
        int minY = Mathf.Max(0, y);
        int maxX = Mathf.Min(probeW, x + width);
        int maxY = Mathf.Min(probeH, y + height);
        return new RectInt(minX, minY, Mathf.Max(0, maxX - minX), Mathf.Max(0, maxY - minY));
    }

    public void RecordResolve(
        CommandBuffer commandBuffer,
        LightingEngine.DebugView debugView,
        RenderTexture emissionField,
        RenderTexture directTarget)
    {
        using var resolveMarker = _ResolveMarker.Auto();
        long resolveStart = System.Diagnostics.Stopwatch.GetTimestamp();
        ComputeShader compute = _resources.LightingCompute!;
        bool transmissionDebug = debugView == LightingEngine.DebugView.Transmission;
        int resolveKernel = transmissionDebug
            ? _resources.ResolveTransmissionDebugKernel
            : _resources.ResolveDirectKernel;

        commandBuffer.SetComputeIntParam(
            compute,
            LightingComputeBinder.CascadeOffsetID,
            _resources.Cascades[0].Offset);
        commandBuffer.SetComputeBufferParam(
            compute,
            resolveKernel,
            LightingComputeBinder.RadianceAtlasID,
            _resources.RadianceAtlas!);
        commandBuffer.SetComputeTextureParam(
            compute,
            resolveKernel,
            LightingComputeBinder.DirectTextureID,
            directTarget);
        if (transmissionDebug)
        {
            BindFieldTextures(commandBuffer, compute, resolveKernel, emissionField);
            commandBuffer.SetComputeTextureParam(
                compute,
                resolveKernel,
                LightingComputeBinder.CellSolidMaskID,
                _resources.CellSolidMask!);
        }

        commandBuffer.DispatchCompute(
            compute,
            resolveKernel,
            LightingComputeBinder.DispatchGroups(_resources.FieldWidth),
            LightingComputeBinder.DispatchGroups(_resources.FieldHeight),
            1);
        _telemetry.LightingCascadeMergeTimeMs =
            (float)((System.Diagnostics.Stopwatch.GetTimestamp() - resolveStart) *
                1000.0 / System.Diagnostics.Stopwatch.Frequency);
    }

    private void RecordCascade(
        CommandBuffer commandBuffer,
        ComputeShader compute,
        int solveKernel,
        int cascadeIndex,
        RenderTexture emissionField,
        RectInt probeRect,
        bool useDependencyMask,
        int dirtyRegionCount)
    {
        string sampleName = cascadeIndex switch
        {
            3 => "Kern.Lighting.Cascade_3",
            2 => "Kern.Lighting.Cascade_2",
            1 => "Kern.Lighting.Cascade_1",
            _ => "Kern.Lighting.Cascade_0",
        };
        commandBuffer.BeginSample(sampleName);
        CascadeLayout cascade = _resources.Cascades[cascadeIndex];
        bool hasFarCascade = cascadeIndex + 1 < _resources.Cascades.Count;
        CascadeLayout farCascade = hasFarCascade
            ? _resources.Cascades[cascadeIndex + 1]
            : cascade;
        LightingComputeBinder.BindCascadeParameters(
            commandBuffer,
            compute,
            cascade,
            farCascade,
            hasFarCascade);
        BindFieldTextures(commandBuffer, compute, solveKernel, emissionField);
        commandBuffer.SetComputeBufferParam(
            compute,
            solveKernel,
            LightingComputeBinder.DirtyRegionsID,
            _resources.DirtyRegions!);
        commandBuffer.SetComputeBufferParam(
            compute,
            solveKernel,
            LightingComputeBinder.CascadeChangedMaskID,
            _resources.CascadeChangedMask!);
        commandBuffer.SetComputeIntParam(
            compute,
            LightingComputeBinder.DirtyRegionCountID,
            dirtyRegionCount);
        commandBuffer.SetComputeIntParam(
            compute,
            LightingComputeBinder.CascadeMaskEnabledID,
            useDependencyMask ? 1 : 0);

        LightingComputeBinder.BindCascadeDispatch(
            commandBuffer,
            compute,
            probeRect.x,
            probeRect.y,
            probeRect.width,
            probeRect.height,
            cascade.DirectionCount);
        int dispatchEntryCount = checked(
            probeRect.width * probeRect.height * cascade.DirectionCount);
        int totalGroupCount = Mathf.CeilToInt(dispatchEntryCount / 64f);
        int groupCountX = Mathf.Min(
            MaximumDispatchGroupsPerDimension,
            totalGroupCount);
        int groupCountY = Mathf.CeilToInt(totalGroupCount / (float)groupCountX);
        commandBuffer.SetComputeIntParam(
            compute,
            LightingComputeBinder.CascadeDispatchRowWidthID,
            groupCountX * 64);
        commandBuffer.DispatchCompute(compute, solveKernel, groupCountX, groupCountY, 1);
        long dispatchEntries = (long)probeRect.width * probeRect.height * cascade.DirectionCount;
        if (probeRect.x == 0 && probeRect.y == 0 &&
            probeRect.width == cascade.ProbeWidth &&
            probeRect.height == cascade.ProbeHeight)
        {
            _telemetry.LightingCascadeFullEntries += dispatchEntries;
            _telemetry.LightingCascadeFullEntriesFrame += dispatchEntries;
        }
        else
        {
            _telemetry.LightingCascadePartialEntries += dispatchEntries;
            _telemetry.LightingCascadePartialEntriesFrame += dispatchEntries;
        }
        commandBuffer.EndSample(sampleName);
    }

    private bool ShouldUseDependencyMask(
        IReadOnlyList<RectInt> dirtyRegions,
        Vector4 worldRect)
    {
        if (dirtyRegions.Count == 0 || worldRect.z <= 0f || worldRect.w <= 0f)
        {
            return false;
        }

        long dirtyArea = 0;
        foreach (RectInt region in dirtyRegions)
        {
            dirtyArea += (long)region.width * region.height;
        }

        float fieldArea = (worldRect.z / ProjectRuntimeContracts.World.CellSize) *
            (worldRect.w / ProjectRuntimeContracts.World.CellSize);
        // Convert the changed world area directly to a candidate estimate.
        // ConvertDirtyRegions already expands every region for rasterization
        // and diagonal-cell dependencies; multiplying this estimate again
        // made a narrow streaming strip fall back to a dense solve too early.
        float candidateFraction = Mathf.Min(
            1f,
            dirtyArea / Mathf.Max(1f, fieldArea));
        long maskOverhead = _resources.EstimatedCascadeDispatchThreads;
        long estimatedPartialCost = (long)(_resources.EstimatedCascadeRayWorkUnits * candidateFraction) +
            maskOverhead;
        return estimatedPartialCost < _resources.EstimatedCascadeRayWorkUnits;
    }

    private void BindFieldTextures(
        CommandBuffer commandBuffer,
        ComputeShader compute,
        int kernel,
        RenderTexture emissionField)
    {
        LightingComputeBinder.BindFieldTextures(
            commandBuffer,
            compute,
            kernel,
            _resources.MaterialField!,
            emissionField,
            _resources.LightingCounters);
    }
}
