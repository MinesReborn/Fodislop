#nullable enable

using System;
using System.Collections.Generic;

namespace Kern.Core;

/// <summary>Builds strict-schema capture fields from observed terrain upload snapshots.</summary>
public static class TerrainTextureUploadCaptureFields
{
    public static Dictionary<string, object?> Cumulative(TerrainTextureUploadSnapshot? snapshot)
    {
        TerrainTextureUploadSnapshot? available = snapshot is { IsAvailable: true } ? snapshot : null;
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["terrainCellDataApplyCalls"] = available?.ApplyCalls,
            ["terrainCellDataApplyPayloadBytes"] = available?.ApplyPayloadBytes,
            ["terrainCellDataCopyTextureCalls"] = available?.CopyTextureCalls,
            ["terrainCellDataCopyTexturePayloadBytes"] = available?.CopyTexturePayloadBytes,
            ["terrainCellDataUploadGeneration"] = snapshot?.Generation,
            ["terrainCellDataUploadAvailable"] = snapshot?.IsAvailable,
            ["terrainCellDataUploadSourceFrameId"] = snapshot is { HasSourceFrame: true } ? snapshot.Value.SourceFrameId : null,
            ["terrainCellDataUploadHasSourceFrame"] = snapshot?.HasSourceFrame,
            ["terrainCellDataUploadObservationFrameId"] = snapshot?.ObservationFrameId,
        };
    }

    public static Dictionary<string, object?> FrameCounters(
        TerrainTextureUploadSnapshot? current,
        TerrainTextureUploadSnapshot? previous)
    {
        TerrainTextureUploadDelta? delta = null;
        if (previous is { } previousSnapshot && current is { } currentSnapshot &&
            TerrainTextureUploadCounters.TryGetDelta(previousSnapshot, currentSnapshot, out TerrainTextureUploadDelta observedDelta))
        {
            delta = observedDelta;
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["terrainCellDataApplyCalls"] = delta?.ApplyCalls,
            ["terrainCellDataApplyPayloadBytes"] = delta?.ApplyPayloadBytes,
            ["terrainCellDataCopyTextureCalls"] = delta?.CopyTextureCalls,
            ["terrainCellDataCopyTexturePayloadBytes"] = delta?.CopyTexturePayloadBytes,
            ["terrainCellDataUploadFrameDeltaValid"] = delta.HasValue,
            ["terrainCellDataUploadDeltaStartObservationFrameId"] = delta?.StartObservationFrameId,
            ["terrainCellDataUploadDeltaEndObservationFrameId"] = delta?.EndObservationFrameId,
        };
    }

    public static Dictionary<string, object?> Observation(TerrainTextureUploadSnapshot? snapshot) =>
        new(StringComparer.Ordinal)
        {
            ["available"] = snapshot?.IsAvailable,
            ["generation"] = snapshot?.Generation,
            ["hasSourceFrame"] = snapshot?.HasSourceFrame,
            ["sourceFrameId"] = snapshot is { HasSourceFrame: true } ? snapshot.Value.SourceFrameId : null,
            ["observationFrameId"] = snapshot?.ObservationFrameId,
        };
}
