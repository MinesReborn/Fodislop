#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Kern.Core;
using UnityEngine;

namespace Kern.World.Lighting;

// Dirty-region mapping for dependency-mask solves: world rects to expanded
// field-texel regions, then to tight per-cascade probe rects. Pure mapping
// over passed-in dimensions; resource lifetime stays in the solver.
internal static class StaticLightingDirty
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct DirtyRegionGpu
    {
        public readonly int MinX;
        public readonly int MinY;
        public readonly int MaxX;
        public readonly int MaxY;

        public DirtyRegionGpu(int minX, int minY, int maxX, int maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }
    }

    internal static DirtyRegionGpu[] ConvertDirtyRegions(
        IReadOnlyList<RectInt> dirtyRegions,
        Vector4 worldRect,
        int fieldWidth,
        int fieldHeight)
    {
        float cellSize = ProjectRuntimeContracts.World.CellSize;
        float worldOriginX = worldRect.x / cellSize;
        float worldOriginY = worldRect.y / cellSize;
        float cellsWidth = worldRect.z / cellSize;
        float cellsHeight = worldRect.w / cellSize;
        float pixelsPerCellX = fieldWidth / Mathf.Max(1f, cellsWidth);
        float pixelsPerCellY = fieldHeight / Mathf.Max(1f, cellsHeight);
        var result = new DirtyRegionGpu[dirtyRegions.Count];
        for (int index = 0; index < dirtyRegions.Count; index++)
        {
            RectInt region = dirtyRegions[index];
            int minX = Mathf.FloorToInt((region.xMin - worldOriginX) * pixelsPerCellX) - 2;
            int minY = Mathf.FloorToInt((region.yMin - worldOriginY) * pixelsPerCellY) - 2;
            int maxX = Mathf.CeilToInt((region.xMax - worldOriginX) * pixelsPerCellX) + 2;
            int maxY = Mathf.CeilToInt((region.yMax - worldOriginY) * pixelsPerCellY) + 2;
            result[index] = new DirtyRegionGpu(
                Mathf.Clamp(minX, 0, fieldWidth),
                Mathf.Clamp(minY, 0, fieldHeight),
                Mathf.Clamp(maxX, 0, fieldWidth),
                Mathf.Clamp(maxY, 0, fieldHeight));
        }

        return result;
    }

    internal static ProbeRect TightProbeRect(
        CascadeLayout cascade,
        DirtyRegionGpu[] dirtyFieldRegions,
        Vector4 worldRect,
        int fieldWidth,
        int fieldHeight)
    {
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;
        foreach (DirtyRegionGpu region in dirtyFieldRegions)
        {
            if (region.MinX < minX)
                minX = region.MinX;
            if (region.MinY < minY)
                minY = region.MinY;
            if (region.MaxX > maxX)
                maxX = region.MaxX;
            if (region.MaxY > maxY)
                maxY = region.MaxY;
        }

        float cellSize = ProjectRuntimeContracts.World.CellSize;
        float cellsX = Mathf.Max(1f, worldRect.z / cellSize);
        float cellsY = Mathf.Max(1f, worldRect.w / cellSize);
        float texelsPerCell = Mathf.Max(
            fieldWidth / cellsX,
            fieldHeight / cellsY);
        float marginTexels = texelsPerCell + cascade.ProbeSpacing + 2f;
        return CascadeProbeRects.ForCascade(
            minX,
            minY,
            maxX,
            maxY,
            fieldWidth,
            fieldHeight,
            cascade,
            marginTexels);
    }
}
