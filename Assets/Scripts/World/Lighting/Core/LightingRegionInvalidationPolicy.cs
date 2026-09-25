#nullable enable

using UnityEngine;

namespace Kern.World.Lighting;

internal static class LightingRegionInvalidationPolicy
{
    public static void OnRegionChanged(
        LightingRuntimeState state,
        bool regionChanged,
        bool canReuseStaticAtlas,
        Vector4 lightingRegion)
    {
        if (!regionChanged)
        {
            return;
        }

        if (!canReuseStaticAtlas)
        {
            state.ClearPendingRegionInvalidation();
            return;
        }

        state.RetainPendingRegionsForReuse(
            new RectInt(
                Mathf.RoundToInt(lightingRegion.x),
                Mathf.RoundToInt(lightingRegion.y),
                Mathf.RoundToInt(lightingRegion.z),
                Mathf.RoundToInt(lightingRegion.w)));
    }
}
