#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces.WorldLighting;

namespace Kern.Core.Bootstrap.WorldLighting;

/// <summary>Main-thread data mailbox. It never calls either domain.</summary>
public sealed class TerrainLightingExchange : ITerrainLightingExchange
{
    private readonly List<TerrainLightingChange> _terrainChanges = [];
    private bool _hasRequirements;
    private LightingTerrainRequirements _requirements;
    private ulong _worldGeneration;
    private ulong _latestFrameSequence;
    private ulong _acknowledgedFrameSequence;
    private bool _hasAcknowledgedFrameInGeneration;
    private TerrainLightingFrameSnapshot _latestFrame;
    private bool _hasFrame;
    private ulong _latestChangeSequence;
    private ulong _acknowledgedChangeSequence;
    private LightingOutputSnapshot _lightingOutput;
    private bool _hasLightingOutput;

    public void PublishLightingRequirements(in LightingTerrainRequirements requirements)
    {
        if (requirements.PolicyRevision == 0 ||
            requirements.RequiredTerrainPaddingCells < 0 ||
            requirements.StableLightingPaddingCells < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requirements));
        }

        if (_hasRequirements)
        {
            bool valuesChanged =
                requirements.RequiredTerrainPaddingCells != _requirements.RequiredTerrainPaddingCells ||
                requirements.StableLightingPaddingCells != _requirements.StableLightingPaddingCells;
            ulong expectedRevision = valuesChanged
                ? checked(_requirements.PolicyRevision + 1)
                : _requirements.PolicyRevision;
            if (requirements.PolicyRevision != expectedRevision)
            {
                throw new InvalidOperationException(
                    "Lighting policy revision must advance if and only if requirements change.");
            }
        }

        _requirements = requirements;
        _hasRequirements = true;
    }

    public bool TryReadLightingRequirements(out LightingTerrainRequirements requirements)
    {
        requirements = _requirements;
        return _hasRequirements;
    }

    public void PublishTerrainFrame(in TerrainLightingFrameSnapshot frame)
    {
        ValidateFrame(frame);
        if (_worldGeneration == 0 || frame.WorldGeneration != _worldGeneration)
        {
            throw new InvalidOperationException("Terrain frame does not belong to the active world generation.");
        }

        ulong expectedFrameSequence = checked(_latestFrameSequence + 1);
        if (frame.FrameSequence != expectedFrameSequence)
        {
            throw new InvalidOperationException(
                $"Terrain frame sequence must be {expectedFrameSequence}, got {frame.FrameSequence}.");
        }

        _latestFrame = frame;
        _latestFrameSequence = frame.FrameSequence;
        _hasFrame = true;
    }

    public bool TryReadLatestTerrainFrame(
        ulong afterFrameSequence,
        out TerrainLightingFrameSnapshot frame)
    {
        frame = _latestFrame;
        ulong readWatermark = _hasAcknowledgedFrameInGeneration
            ? Math.Max(afterFrameSequence, _acknowledgedFrameSequence)
            : 0;
        return _hasFrame && _latestFrame.FrameSequence > readWatermark;
    }

    public void AcknowledgeTerrainFrame(ulong throughFrameSequence)
    {
        if (!_hasFrame ||
            throughFrameSequence <= _acknowledgedFrameSequence ||
            throughFrameSequence > _latestFrameSequence)
        {
            throw new InvalidOperationException("Invalid terrain-frame acknowledgement.");
        }

        _acknowledgedFrameSequence = throughFrameSequence;
        _hasAcknowledgedFrameInGeneration = true;
    }

    public void PublishTerrainChange(in TerrainLightingChange change)
    {
        ValidateChange(change);
        if (change.Kind == TerrainLightingChangeKind.FullReset && change.WorldGeneration > _worldGeneration)
        {
            if (change.Sequence != 1)
            {
                throw new InvalidOperationException("A new world generation must start at change sequence 1.");
            }

            _worldGeneration = change.WorldGeneration;
            _latestChangeSequence = 0;
            _acknowledgedChangeSequence = 0;
            _latestFrameSequence = 0;
            _acknowledgedFrameSequence = 0;
            _hasAcknowledgedFrameInGeneration = false;
            _hasFrame = false;
            _terrainChanges.Clear();
        }
        else if (change.WorldGeneration != _worldGeneration)
        {
            throw new InvalidOperationException("Terrain change belongs to an inactive world generation.");
        }

        ulong expectedChangeSequence = checked(_latestChangeSequence + 1);
        if (change.Sequence != expectedChangeSequence)
        {
            throw new InvalidOperationException(
                $"Terrain change sequence must be {expectedChangeSequence}, got {change.Sequence}.");
        }

        if (change.Kind == TerrainLightingChangeKind.FullReset)
        {
            // A full reset supersedes every earlier unacknowledged region in this generation.
            _terrainChanges.Clear();
        }

        _terrainChanges.Add(change);
        _latestChangeSequence = change.Sequence;
    }

    public bool TryReadNextTerrainChange(
        ulong worldGeneration,
        ulong afterSequence,
        out TerrainLightingChange change)
    {
        if (_worldGeneration != 0 && worldGeneration != _worldGeneration)
        {
            throw new InvalidOperationException("Requested terrain changes belong to an inactive world generation.");
        }

        for (int index = 0; index < _terrainChanges.Count; index++)
        {
            TerrainLightingChange candidate = _terrainChanges[index];
            if (candidate.WorldGeneration == worldGeneration && candidate.Sequence > afterSequence)
            {
                change = candidate;
                return true;
            }
        }

        change = default;
        return false;
    }

    public void AcknowledgeTerrainChanges(ulong worldGeneration, ulong throughSequence)
    {
        if (worldGeneration != _worldGeneration ||
            throughSequence <= _acknowledgedChangeSequence ||
            throughSequence > _latestChangeSequence)
        {
            throw new InvalidOperationException("Invalid terrain-change acknowledgement.");
        }

        int targetIndex = _terrainChanges.FindIndex(change =>
            change.WorldGeneration == worldGeneration && change.Sequence == throughSequence);
        if (targetIndex < 0)
        {
            throw new InvalidOperationException("Acknowledged terrain change is not pending.");
        }

        TerrainLightingChange target = _terrainChanges[targetIndex];
        if (throughSequence != _acknowledgedChangeSequence + 1 &&
            target.Kind != TerrainLightingChangeKind.FullReset)
        {
            throw new InvalidOperationException("Only a full reset may acknowledge superseded terrain changes.");
        }

        _acknowledgedChangeSequence = throughSequence;
        _terrainChanges.RemoveAll(change =>
            change.WorldGeneration == worldGeneration && change.Sequence <= throughSequence);
    }

    public void PublishLightingOutput(in LightingOutputSnapshot output)
    {
        if (output.OutputGeneration == 0 || output.WorldGeneration == 0 ||
            !Enum.IsDefined(typeof(LightingOutputState), output.State) ||
            !IsValidRect(output.WorldRectCells, allowEmpty: output.State == LightingOutputState.Disabled))
        {
            throw new ArgumentOutOfRangeException(nameof(output));
        }

        if (_worldGeneration != 0 && output.WorldGeneration != _worldGeneration)
        {
            throw new InvalidOperationException("Lighting output belongs to an inactive world generation.");
        }

        if (_hasLightingOutput && output.OutputGeneration <= _lightingOutput.OutputGeneration)
        {
            throw new InvalidOperationException("Lighting output generation must increase.");
        }

        if (output.State == LightingOutputState.Published &&
            (output.WorldRectCells.width == 0 || output.WorldRectCells.height == 0))
        {
            throw new ArgumentException(
                "Published lighting output requires a non-empty world rectangle.", nameof(output));
        }

        _lightingOutput = output;
        _hasLightingOutput = true;
    }

    public bool TryReadLightingOutput(out LightingOutputSnapshot output)
    {
        output = _lightingOutput;
        return _hasLightingOutput;
    }

    private static void ValidateFrame(TerrainLightingFrameSnapshot frame)
    {
        if (frame.WorldGeneration == 0 || frame.FrameSequence == 0 ||
            frame.Camera == null || frame.GeometryContributor == null ||
            !Enum.IsDefined(typeof(TerrainLightingFrameState), frame.State) ||
            !IsValidRect(frame.CameraViewportCells, allowEmpty: false) ||
            !IsValidRect(frame.LightingViewportCells, allowEmpty: false))
        {
            throw new ArgumentException(
                "Terrain frame has invalid generation, contributor, camera, or viewport.", nameof(frame));
        }
    }

    private static void ValidateChange(TerrainLightingChange change)
    {
        if (change.WorldGeneration == 0 || change.Sequence == 0 ||
            !Enum.IsDefined(typeof(TerrainLightingChangeKind), change.Kind) ||
            change.Channels == TerrainLightingChannels.None ||
            (change.Channels & ~TerrainLightingChannels.All) != 0)
        {
            throw new ArgumentException("Terrain change requires generation, sequence, and channels.", nameof(change));
        }

        if (change.Kind == TerrainLightingChangeKind.Region && !IsValidRect(change.Region, allowEmpty: false))
        {
            throw new ArgumentException("Terrain region change requires a non-empty rectangle.", nameof(change));
        }

        if (change.Kind == TerrainLightingChangeKind.FullReset &&
            !Enum.IsDefined(typeof(TerrainLightingFullResetReason), change.FullResetReason))
        {
            throw new ArgumentOutOfRangeException(nameof(change), "Full reset requires a named reason.");
        }
    }

    private static bool IsValidRect(UnityEngine.RectInt rect, bool allowEmpty)
    {
        if (rect.width < 0 || rect.height < 0 ||
            (!allowEmpty && (rect.width == 0 || rect.height == 0)))
        {
            return false;
        }

        long maxX = (long)rect.x + rect.width;
        long maxY = (long)rect.y + rect.height;
        return maxX <= int.MaxValue && maxY <= int.MaxValue;
    }
}
