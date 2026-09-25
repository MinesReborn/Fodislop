#nullable enable

using UnityEngine;

namespace Kern.World.Terrain;

internal static class TerrainLightingViewportPolicy
{
    public static RectInt ResolveLightingViewport(
        RectInt cameraViewport,
        RectInt retainedLightingViewport,
        bool holdingPublishedView) =>
        holdingPublishedView ? retainedLightingViewport : cameraViewport;

    public static bool TrySelectCommittedView(
        bool holdingPublishedView,
        bool hasPublishedView,
        RectInt lastCameraViewport,
        RectInt lastLightingViewport,
        RectInt requestedCameraViewport,
        RectInt requestedLightingViewport,
        RectInt committedTerrainWindow,
        out RectInt cameraViewport,
        out RectInt lightingViewport)
    {
        if (holdingPublishedView)
        {
            if (!hasPublishedView)
            {
                cameraViewport = default;
                lightingViewport = default;
                return false;
            }

            cameraViewport = lastCameraViewport;
            lightingViewport = lastLightingViewport;
        }
        else
        {
            cameraViewport = requestedCameraViewport;
            lightingViewport = requestedLightingViewport;
        }

        return Contains(committedTerrainWindow, cameraViewport) &&
            Contains(committedTerrainWindow, lightingViewport);
    }

    private static bool Contains(RectInt outer, RectInt inner)
    {
        long outerMaxX = (long)outer.x + outer.width;
        long outerMaxY = (long)outer.y + outer.height;
        long innerMaxX = (long)inner.x + inner.width;
        long innerMaxY = (long)inner.y + inner.height;
        return outer.width > 0 && outer.height > 0 &&
            inner.width > 0 && inner.height > 0 &&
            inner.x >= outer.x && inner.y >= outer.y &&
            innerMaxX <= outerMaxX && innerMaxY <= outerMaxY;
    }
}
