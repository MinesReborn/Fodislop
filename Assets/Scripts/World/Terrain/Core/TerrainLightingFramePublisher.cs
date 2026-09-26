#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces.WorldLighting;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Owns Terrain's generation-scoped sequence numbers and publishes committed facts
/// through the neutral Terrain/Lighting exchange.
/// </summary>
internal sealed class TerrainLightingFramePublisher(ITerrainLightingExchange? exchange)
{
#if UNITY_EDITOR
    private static readonly LightingTerrainRequirements _EditorPreviewRequirements = new(1, 3, 16);
#endif

    private ulong _worldGeneration;
    private ulong _changeSequence;
    private ulong _frameSequence;
    private TerrainLightingFullResetReason? _pendingFullReset;
    private bool _hasPublishedView;
    private RectInt _lastCameraViewport;
    private RectInt _lastLightingViewport;

    public ulong WorldGeneration => _worldGeneration;

    public void BeginWorldGeneration()
    {
        _worldGeneration = checked(_worldGeneration + 1);
        _changeSequence = 0;
        _frameSequence = 0;
        _hasPublishedView = false;
        _lastCameraViewport = default;
        _lastLightingViewport = default;
        RequestFullReset(TerrainLightingFullResetReason.WorldReplaced);
    }

    public void RequestFullReset(TerrainLightingFullResetReason reason) =>
        _pendingFullReset ??= reason;

    public LightingTerrainRequirements ReadRequirements()
    {
        if (exchange != null && exchange.TryReadLightingRequirements(out LightingTerrainRequirements requirements))
        {
            return requirements;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            return _EditorPreviewRequirements;
        }
#endif

        throw new InvalidOperationException(
            "Terrain cannot plan a runtime window before Lighting publishes its padding requirements.");
    }

    public bool TryReadLightingOutput(out LightingOutputSnapshot output)
    {
        output = default;
        return exchange != null && exchange.TryReadLightingOutput(out output);
    }

    public void PublishCommittedChanges(ulong terrainContentRevision, IReadOnlyList<RectInt> changedRegions)
    {
        if (exchange == null)
        {
            return;
        }

        if (_pendingFullReset is TerrainLightingFullResetReason fullResetReason)
        {
            PublishTerrainChange(
                terrainContentRevision,
                TerrainLightingChangeKind.FullReset,
                TerrainLightingChannels.All,
                default,
                fullResetReason);
            _pendingFullReset = null;
        }

        for (int index = 0; index < changedRegions.Count; index++)
        {
            PublishTerrainChange(
                terrainContentRevision,
                TerrainLightingChangeKind.Region,
                TerrainLightingChannels.All,
                changedRegions[index],
                default);
        }
    }

    public void ValidateLightingOutput(
        bool hasOutput,
        LightingOutputSnapshot output,
        TerrainWindow window)
    {
        if (!hasOutput || output.WorldGeneration != _worldGeneration)
        {
            return;
        }

        window.Driver.Presentation.ValidateLightingBinding(output);
    }

    public void PublishFrameDemand(
        TerrainFramePlan framePlan,
        bool holdingView,
        RectInt currentLightingViewport,
        TerrainWindow window,
        Camera? camera,
        MeshRenderer? meshRenderer,
        ulong terrainContentRevision,
        ILightingGeometryContributor contributor)
    {
        if (exchange == null ||
            !window.CellsCommitted ||
            !window.HasOrigin ||
            camera == null ||
            !camera.orthographic ||
            meshRenderer == null ||
            !meshRenderer.enabled)
        {
            return;
        }

        RectInt terrainWindow = new(window.Origin.x, window.Origin.y, window.Width, window.Height);
        if (!TerrainLightingViewportPolicy.TrySelectCommittedView(
            holdingView,
            _hasPublishedView,
            _lastCameraViewport,
            _lastLightingViewport,
            framePlan.CameraViewport,
            holdingView ? currentLightingViewport : framePlan.LightingViewport,
            terrainWindow,
            out RectInt cameraViewport,
            out RectInt lightingViewport))
        {
            return;
        }

        ulong frameSequence = checked(_frameSequence + 1);
        var frame = new TerrainLightingFrameSnapshot(
            _worldGeneration,
            frameSequence,
            holdingView
                ? TerrainLightingFrameState.HoldingPublishedView
                : TerrainLightingFrameState.Ready,
            terrainContentRevision,
            cameraViewport,
            lightingViewport,
            camera,
            contributor);
        exchange.PublishTerrainFrame(frame);
        _frameSequence = frameSequence;
        if (!holdingView)
        {
            _lastCameraViewport = cameraViewport;
            _lastLightingViewport = lightingViewport;
            _hasPublishedView = true;
        }
    }

    private void PublishTerrainChange(
        ulong terrainContentRevision,
        TerrainLightingChangeKind kind,
        TerrainLightingChannels channels,
        RectInt region,
        TerrainLightingFullResetReason fullResetReason)
    {
        if (_worldGeneration == 0)
        {
            throw new InvalidOperationException(
                "Terrain cannot publish lighting changes before its world generation is established.");
        }

        ulong sequence = checked(_changeSequence + 1);
        var change = new TerrainLightingChange(
            _worldGeneration,
            sequence,
            terrainContentRevision,
            kind,
            channels,
            region,
            fullResetReason);
        exchange!.PublishTerrainChange(change);
        _changeSequence = sequence;
    }

}
