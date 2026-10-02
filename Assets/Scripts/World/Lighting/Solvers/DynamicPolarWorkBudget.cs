#nullable enable

using System;
using UnityEngine;

namespace Kern.World.Lighting;

/// <summary>
/// Allocates the frame-wide angular ray budget among dynamic lights that need tracing.
/// </summary>
internal static class DynamicPolarWorkBudget
{
    // Polar tracing cost is angles * emitter points * ray length. One shared
    // frame budget prevents each visible light from multiplying the cap.
    private const long MaximumPolarRayWorkUnits = LightingConfigHolder.MaximumDynamicPolarRayWorkUnits;

    public static int RequiredRayLength(int count, Vector2Int[] raySizes)
    {
        int longestRay = 1;
        for (int lightIndex = 0; lightIndex < count; lightIndex++)
        {
            longestRay = Mathf.Max(longestRay, raySizes[lightIndex].y);
        }
        return longestRay;
    }

    public static int AllocateRayFans(
        int count,
        int[] requestedRayFans,
        bool[] needsTrace,
        Vector2Int[] raySizes,
        out int longestRay)
    {
        int maxTextureSize = SystemInfo.maxTextureSize;
        int maxRayLength = maxTextureSize;

        long requestedWork = 0;
        for (int lightIndex = 0; lightIndex < count; lightIndex++)
        {
            if (!needsTrace[lightIndex])
            {
                continue;
            }

            Vector2Int raySize = raySizes[lightIndex];
            requestedWork += (long)LightingQualityTuningController.DynamicPolarDirectionCount *
                LightingComputeBinder.DynamicEmitterPointCount *
                Mathf.Max(1, raySize.y);
        }

        if (requestedWork > MaximumPolarRayWorkUnits)
        {
            throw new InvalidOperationException($"Dynamic transport requires {requestedWork} ray work units at the authored " +
                $"angular quality; configured limit is {MaximumPolarRayWorkUnits}.");
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

            int requested = LightingQualityTuningController.DynamicPolarDirectionCount;
            // Each emitter has its own array layer. Never shorten transport
            // to fit stacked emitter rows or reuse edge depth beyond the ray.
            int rayLength = Mathf.Max(1, raySizes[lightIndex].y);
            if (rayLength > maxRayLength)
            {
                throw new InvalidOperationException(
                    $"Dynamic transport requires {rayLength} distance samples; texture limit is {maxRayLength}.");
            }
            int rayFan = requested;
            if (requested < 1 || requested + 2 > maxTextureSize)
            {
                throw new InvalidOperationException($"Dynamic angular quality {requested} cannot fit the texture limit {maxTextureSize}.");
            }

            // The filtered polar texture stores one wrap column at each edge.
            raySizes[lightIndex] = new Vector2Int(rayFan, rayLength);
            widestRayFan = Mathf.Max(widestRayFan, rayFan);
            longestRay = Mathf.Max(longestRay, rayLength);
        }

        return widestRayFan;
    }
}
