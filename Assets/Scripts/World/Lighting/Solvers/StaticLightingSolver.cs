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
    private readonly CascadeScrollRecorder _scrollRecorder;

    public StaticLightingSolver(
        LightingResourceManager resources,
        IFrameTelemetry telemetry)
    {
        _resources = resources;
        _telemetry = telemetry;
        _scrollRecorder = new CascadeScrollRecorder(resources, telemetry);
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
        Vector2Int[]? scrollDeltas = reuseOverlap ? _scrollRecorder.TryResolveScrollDeltas(regionDelta) : null;
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
            // The solve only dispatches a tight probe rect. A near tier can
            // still read any far-tier changed flag, so flags outside that rect
            // must be zero for this solve rather than left over from an older
            // edit (or uninitialized after a full solve).
            int maskCount = _resources.CascadeChangedMask!.count;
            int maskGroups = Mathf.CeilToInt(maskCount / 64f);
            int maskGroupsX = Mathf.Min(MaximumDispatchGroupsPerDimension, maskGroups);
            ComputeShader maskCompute = _resources.LightingCompute!;
            int clearMaskKernel = _resources.ClearCascadeChangedMaskKernel;
            commandBuffer.SetComputeBufferParam(
                maskCompute,
                clearMaskKernel,
                LightingComputeBinder.CascadeChangedMaskID,
                _resources.CascadeChangedMask);
            commandBuffer.SetComputeIntParam(
                maskCompute,
                LightingComputeBinder.CascadeChangedMaskCountID,
                maskCount);
            commandBuffer.SetComputeIntParam(
                maskCompute,
                LightingComputeBinder.CascadeDispatchRowWidthID,
                maskGroupsX * 64);
            commandBuffer.DispatchCompute(
                maskCompute,
                clearMaskKernel,
                maskGroupsX,
                Mathf.CeilToInt(maskGroups / (float)maskGroupsX),
                1);
        }

        if (scrollDeltas != null)
        {
            _scrollRecorder.RecordScroll(commandBuffer, compute, regionDelta);
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

                _scrollRecorder.RecordCascadeMoveTier(
                    commandBuffer,
                    compute,
                    solveKernel,
                    cascadeIndex,
                    emissionField,
                    scrollDeltas[cascadeIndex],
                    dirtyProbeRect,
                    RecordCascade);
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
