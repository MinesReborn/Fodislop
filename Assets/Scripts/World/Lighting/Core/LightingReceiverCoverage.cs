#nullable enable

using System;
using Kern.Rendering;
using UnityEngine;

namespace Kern.World.Lighting;

/// <summary>World-frame coverage in the light (receiver) lattice's bottom-left texels.</summary>
internal static class LightingReceiverCoverage
{
    public static RectInt GetRect(Camera camera, Vector4 fieldRect, int width, int height, float cellSize)
    {
        Vector3 position = camera.transform.position;
        double viewportHeight = (double)camera.orthographicSize * 2.0;
        double viewportWidth = viewportHeight * camera.aspect;
        WorldRenderGrid grid = WorldRenderGrid.Create(position.x - viewportWidth * 0.5,
            position.y - viewportHeight * 0.5, viewportWidth, viewportHeight);
        double scaleX = width / (double)fieldRect.z;
        double scaleY = height / (double)fieldRect.w;
        // Composite gathers an air receiver beyond the surface, then bilinear
        // presentation can sample one more texel. Include both dependencies.
        int marginX = (int)Math.Ceiling(LightingConfigHolder.SurfaceReflectionReachCells * cellSize * scaleX) + 2;
        int marginY = (int)Math.Ceiling(LightingConfigHolder.SurfaceReflectionReachCells * cellSize * scaleY) + 2;
        int minX = Math.Clamp((int)Math.Floor((grid.WorldMinX - fieldRect.x) * scaleX) - marginX, 0, width);
        int minY = Math.Clamp((int)Math.Floor((grid.WorldMinY - fieldRect.y) * scaleY) - marginY, 0, height);
        int maxX = Math.Clamp((int)Math.Ceiling((grid.WorldMinX + grid.WorldWidth - fieldRect.x) * scaleX) + marginX, 0, width);
        int maxY = Math.Clamp((int)Math.Ceiling((grid.WorldMinY + grid.WorldHeight - fieldRect.y) * scaleY) + marginY, 0, height);
        return new RectInt(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }
}
