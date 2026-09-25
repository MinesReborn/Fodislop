#nullable enable

using Kern.Core.Interfaces.WorldLighting;
using UnityEngine;

namespace Kern.World.Lighting;

/// <summary>Transfers a terrain journal item into the lighting-owned pending invalidation state.</summary>
internal static class TerrainLightingChangeApplier
{
    public static bool Apply(TerrainLightingChange change, LightingRuntimeState state)
    {
        if (change.Kind == TerrainLightingChangeKind.FullReset)
        {
            if (change.FullResetReason == TerrainLightingFullResetReason.WorldReplaced)
            {
                state.ClearPendingRegionInvalidation();
            }

            state.FieldDirty = true;
            return false;
        }

        if (change.Kind != TerrainLightingChangeKind.Region)
        {
            throw new System.InvalidOperationException(
                $"Unsupported terrain lighting change kind: {change.Kind}.");
        }

        Vector4 stableRegion = state.LastVisibleRegion;
        if (!LightingRegionCalculator.TouchesStableRegion(
            change.Region.x,
            change.Region.y,
            change.Region.width,
            change.Region.height,
            stableRegion))
        {
            return false;
        }

        state.QueueRegionInvalidation(change.Region);
        return true;
    }
}
