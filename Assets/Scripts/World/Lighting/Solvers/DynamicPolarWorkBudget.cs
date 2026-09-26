#nullable enable

using UnityEngine;

namespace Kern.World.Lighting;

/// <summary>
/// Allocates the frame-wide angular ray budget among dynamic lights that need tracing.
/// </summary>
internal static class DynamicPolarWorkBudget
{
    // Polar tracing cost is angles * emitter points * ray length. One shared
    // frame budget prevents each visible light from multiplying the cap.
    private const long MaximumPolarRayWorkUnits = 2_000_000;

    public static int AllocateRayFans(
        int count,
        int[] requestedRayFans,
        bool[] needsTrace,
        Vector2Int[] raySizes,
        out int longestRay)
    {
        int maxTextureSize = SystemInfo.maxTextureSize;
        int maxRayLength = Mathf.Max(1, maxTextureSize / LightingComputeBinder.DynamicEmitterPointCount);

        long requestedWork = 0;
        for (int lightIndex = 0; lightIndex < count; lightIndex++)
        {
            if (!needsTrace[lightIndex])
            {
                continue;
            }

            Vector2Int raySize = raySizes[lightIndex];
            requestedWork += (long)requestedRayFans[lightIndex] *
                LightingComputeBinder.DynamicEmitterPointCount *
                Mathf.Max(1, raySize.y);
        }

        int widestRayFan = 1;
        longestRay = 1;
        for (int lightIndex = 0; lightIndex < count; lightIndex++)
        {
            if (!needsTrace[lightIndex])
            {
                raySizes[lightIndex] = new Vector2Int(1, 1);
                continue;
            }

            int requested = Mathf.Max(1, requestedRayFans[lightIndex]);
            // The fan sets angular density only; the stored length is capped
            // to the polar texture limit. Receivers past the cap reuse the
            // edge depth through the Clamp sampler: bounded degradation that
            // cannot crash the frame, unlike an oversized allocation.
            int rayLength = Mathf.Min(Mathf.Max(1, raySizes[lightIndex].y), maxRayLength);
            int rayFan = requested;
            if (requestedWork > MaximumPolarRayWorkUnits)
            {
                long weightedBudget = MaximumPolarRayWorkUnits * requested;
                rayFan = Mathf.Clamp(
                    (int)(weightedBudget / Mathf.Max(1L, requestedWork)),
                    1,
                    requested);
            }

            // The filtered polar texture stores one wrap column at each edge.
            rayFan = Mathf.Min(rayFan, Mathf.Max(1, maxTextureSize - 2));
            raySizes[lightIndex] = new Vector2Int(rayFan, rayLength);
            widestRayFan = Mathf.Max(widestRayFan, rayFan);
            longestRay = Mathf.Max(longestRay, rayLength);
        }

        return widestRayFan;
    }
}
