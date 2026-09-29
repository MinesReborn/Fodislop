#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;
using Kern.Core;

namespace Kern.World.Lighting;

/// <summary>
/// Encapsulates cascade atlas scrolling, delta resolution, and move-tier invalidation dispatch.
/// </summary>
internal sealed class CascadeScrollRecorder
{
    private const int MaximumDispatchGroupsPerDimension = 65535;

    private readonly LightingResourceManager _resources;
    private readonly IFrameTelemetry _telemetry;

    public CascadeScrollRecorder(
        LightingResourceManager resources,
        IFrameTelemetry telemetry)
    {
        _resources = resources;
        _telemetry = telemetry;
    }

    public void RecordScroll(
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
    public Vector2Int[]? TryResolveScrollDeltas(Vector2Int regionDelta)
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

    public bool CanResolveScrollDeltas(Vector2Int regionDelta)
    {
        try
        {
            for (int cascadeIndex = 0; cascadeIndex < _resources.Cascades.Count; cascadeIndex++)
            {
                CascadeLayout cascade = _resources.Cascades[cascadeIndex];
                LightingComputeBinder.ResolveCascadeScrollDelta(
                    regionDelta.x,
                    _resources.FieldWidth,
                    _resources.CellGridWidth,
                    cascade.ProbeSpacing);
                LightingComputeBinder.ResolveCascadeScrollDelta(
                    regionDelta.y,
                    _resources.FieldHeight,
                    _resources.CellGridHeight,
                    cascade.ProbeSpacing);
            }
        }
        catch (ArgumentException)
        {
            return false;
        }

        return true;
    }

    public void RecordCascadeMoveTier(
        CommandBuffer commandBuffer,
        ComputeShader compute,
        int solveKernel,
        int cascadeIndex,
        RenderTexture emissionField,
        Vector2Int scrollDelta,
        RectInt dirtyProbeRect,
        Action<CommandBuffer, ComputeShader, int, int, RenderTexture, RectInt, bool, int> recordCascade)
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
            recordCascade(
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
            recordCascade(
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
            recordCascade(
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

    public static RectInt ClipProbeRect(int x, int y, int width, int height, int probeW, int probeH)
    {
        int minX = Mathf.Max(0, x);
        int minY = Mathf.Max(0, y);
        int maxX = Mathf.Min(probeW, x + width);
        int maxY = Mathf.Min(probeH, y + height);
        return new RectInt(minX, minY, Mathf.Max(0, maxX - minX), Mathf.Max(0, maxY - minY));
    }
}
