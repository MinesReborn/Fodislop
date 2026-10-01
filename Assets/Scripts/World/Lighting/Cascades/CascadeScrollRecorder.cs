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

    public bool CanReuseWorldOverlap(Vector2Int cellDelta)
    {
        return _resources.CellGridWidth > 0 && _resources.CellGridHeight > 0 &&
            _resources.FieldWidth % _resources.CellGridWidth == 0 &&
            _resources.FieldHeight % _resources.CellGridHeight == 0 &&
            Math.Abs((long)cellDelta.x) < _resources.CellGridWidth &&
            Math.Abs((long)cellDelta.y) < _resources.CellGridHeight;
    }

    /// <summary>
    /// Move matching world probes, then solve far-to-near with per-entry
    /// dependencies. Incompatible lattice phases are rebuilt per cascade.
    /// Reads 12 and writes 16 bytes/entry before masked solves; stable frames
    /// do not record this stage or allocate its scratch atlas.
    /// </summary>
    public void RecordWorldReanchor(
        CommandBuffer cmd,
        ComputeShader compute,
        RenderTexture emission,
        Vector2Int cellDelta,
        int dirtyRegionCount,
        Action<CommandBuffer, ComputeShader, int, int, RenderTexture, RectInt, bool, int> recordCascade)
    {
        if (!CanReuseWorldOverlap(cellDelta))
        {
            throw new InvalidOperationException("Atlas reanchor requires overlapping fields on an integer texel lattice.");
        }
        Vector2Int texelDelta = new(
            checked(cellDelta.x * (_resources.FieldWidth / _resources.CellGridWidth)),
            checked(cellDelta.y * (_resources.FieldHeight / _resources.CellGridHeight)));
        _resources.EnsureScratchAtlas();
        _telemetry.LightingAtlasScrollCount++;
        cmd.BeginSample("Kern.Lighting.AtlasReanchor");
        cmd.SetComputeIntParam(compute, LightingComputeBinder.CascadeReanchorEnabledID, 1);
        cmd.SetComputeIntParams(compute, LightingComputeBinder.ReanchorDeltaTexelsID, texelDelta.x, texelDelta.y);
        cmd.BeginSample("Kern.Lighting.ReanchorFieldDependencies");
        int rowsKernel = _resources.BuildReanchorChangeRowsKernel;
        int columnsKernel = _resources.BuildReanchorChangeColumnsKernel;
        cmd.SetComputeTextureParam(compute, rowsKernel, "_ReanchorMaterial", _resources.ReanchorMaterial!);
        cmd.SetComputeTextureParam(compute, rowsKernel, "_ReanchorEmission", _resources.ReanchorEmission!);
        cmd.SetComputeTextureParam(compute, rowsKernel, LightingComputeBinder.MaterialFieldID, _resources.MaterialField!);
        cmd.SetComputeTextureParam(compute, rowsKernel, LightingComputeBinder.EmissionFieldID, emission);
        cmd.SetComputeBufferParam(compute, rowsKernel, "_ReanchorChangeRowsOutput", _resources.ReanchorRows!);
        cmd.DispatchCompute(compute, rowsKernel, (_resources.FieldHeight + 63) / 64, 1, 1);
        cmd.SetComputeBufferParam(compute, columnsKernel, "_ReanchorChangeRows", _resources.ReanchorRows!);
        cmd.SetComputeBufferParam(compute, columnsKernel, "_ReanchorChangesOutput", _resources.ReanchorChanges!);
        cmd.DispatchCompute(compute, columnsKernel, (_resources.FieldWidth + 63) / 64, 1, 1);
        cmd.SetComputeBufferParam(compute, _resources.SolveCascadeKernel, "_ReanchorChanges", _resources.ReanchorChanges!);
        cmd.EndSample("Kern.Lighting.ReanchorFieldDependencies");
        int copyKernel = _resources.ScrollRadianceAtlasKernel;
        cmd.SetComputeBufferParam(compute, copyKernel, LightingComputeBinder.RadianceAtlasInputID, _resources.RadianceAtlas!);
        cmd.SetComputeBufferParam(compute, copyKernel, LightingComputeBinder.RadianceAtlasOutputID, _resources.RadianceScratchAtlas!);
        cmd.SetComputeBufferParam(compute, copyKernel, LightingComputeBinder.CascadeChangedMaskID, _resources.CascadeChangedMask!);
        foreach (CascadeLayout cascade in _resources.Cascades)
        {
            bool samePhase = texelDelta.x % cascade.ProbeSpacing == 0 && texelDelta.y % cascade.ProbeSpacing == 0;
            int deltaX = texelDelta.x / cascade.ProbeSpacing;
            int deltaY = texelDelta.y / cascade.ProbeSpacing;
            long copied = samePhase
                ? (long)Mathf.Max(0, cascade.ProbeWidth - Mathf.Abs(deltaX)) *
                    Mathf.Max(0, cascade.ProbeHeight - Mathf.Abs(deltaY)) * cascade.DirectionCount
                : 0;
            _telemetry.LightingAtlasReusedEntries += copied;
            _telemetry.LightingAtlasClearedEntries += cascade.EntryCount - copied;
            cmd.SetComputeIntParam(compute, LightingComputeBinder.CascadePhaseMatchesID, samePhase ? 1 : 0);
            cmd.SetComputeIntParam(compute, LightingComputeBinder.ScrollCascadeOffsetID, cascade.Offset);
            cmd.SetComputeIntParam(compute, LightingComputeBinder.ScrollCascadeEntryCountID, cascade.EntryCount);
            cmd.SetComputeIntParams(compute, LightingComputeBinder.ScrollProbeSizeID, cascade.ProbeWidth, cascade.ProbeHeight);
            cmd.SetComputeIntParam(compute, LightingComputeBinder.ScrollDirectionCountID, cascade.DirectionCount);
            cmd.SetComputeIntParams(compute, LightingComputeBinder.ScrollDeltaProbesID, deltaX, deltaY);
            int groups = (cascade.EntryCount + 63) / 64;
            int x = Mathf.Min(MaximumDispatchGroupsPerDimension, groups);
            cmd.SetComputeIntParam(compute, LightingComputeBinder.CascadeDispatchRowWidthID, x * 64);
            cmd.DispatchCompute(compute, copyKernel, x, (groups + x - 1) / x, 1);
        }
        _resources.SwapRadianceAtlases();
        cmd.SetComputeBufferParam(compute, _resources.SolveCascadeKernel,
            LightingComputeBinder.RadianceAtlasID, _resources.RadianceAtlas!);
        cmd.SetComputeTextureParam(compute, _resources.SolveCascadeKernel,
            LightingComputeBinder.CellSolidMaskID, _resources.CellSolidMask!);
        for (int i = _resources.Cascades.Count - 1; i >= 0; i--)
        {
            CascadeLayout cascade = _resources.Cascades[i];
            bool samePhase = texelDelta.x % cascade.ProbeSpacing == 0 && texelDelta.y % cascade.ProbeSpacing == 0;
            cmd.SetComputeIntParam(compute, LightingComputeBinder.CascadePhaseMatchesID, samePhase ? 1 : 0);
            CascadeLayout far = i + 1 < _resources.Cascades.Count ? _resources.Cascades[i + 1] : cascade;
            bool farPhase = texelDelta.x % far.ProbeSpacing == 0 && texelDelta.y % far.ProbeSpacing == 0;
            cmd.SetComputeIntParam(compute, LightingComputeBinder.ReanchorFarPhaseMatchesID, farPhase ? 1 : 0);
            cmd.SetComputeIntParams(compute, LightingComputeBinder.ReanchorFarDeltaProbesID,
                texelDelta.x / far.ProbeSpacing, texelDelta.y / far.ProbeSpacing);
            recordCascade(cmd, compute, _resources.SolveCascadeKernel, i, emission,
                new RectInt(0, 0, cascade.ProbeWidth, cascade.ProbeHeight), true, dirtyRegionCount);
        }
        cmd.SetComputeIntParam(compute, LightingComputeBinder.CascadeReanchorEnabledID, 0);
        cmd.EndSample("Kern.Lighting.AtlasReanchor");
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
