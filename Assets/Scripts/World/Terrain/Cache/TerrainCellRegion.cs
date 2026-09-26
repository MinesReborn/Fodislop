#nullable enable

using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>A half-open rectangle of cells in Unity world-cell coordinates.</summary>
public readonly record struct TerrainWorldCellRegion(RectInt Bounds)
{
    public TerrainWindowCellRegion ToWindowLocal(
        Vector2Int windowOrigin,
        Vector2Int windowSize,
        int neighbourHalo)
    {
        if (neighbourHalo < 0)
        {
            throw new System.ArgumentOutOfRangeException(nameof(neighbourHalo));
        }

        long minX = System.Math.Max(0L, (long)Bounds.xMin - windowOrigin.x - neighbourHalo);
        long minY = System.Math.Max(0L, (long)Bounds.yMin - windowOrigin.y - neighbourHalo);
        long maxX = System.Math.Min(
            windowSize.x,
            (long)Bounds.xMax - windowOrigin.x + neighbourHalo);
        long maxY = System.Math.Min(
            windowSize.y,
            (long)Bounds.yMax - windowOrigin.y + neighbourHalo);

        if (maxX <= minX || maxY <= minY)
        {
            return default;
        }

        return new TerrainWindowCellRegion(
            (int)minX,
            (int)minY,
            (int)(maxX - minX),
            (int)(maxY - minY));
    }
}

/// <summary>A half-open rectangle in the local coordinates of a terrain window.</summary>
public readonly record struct TerrainWindowCellRegion(
    int StartX,
    int StartY,
    int CountX,
    int CountY)
{
    public bool IsEmpty => CountX <= 0 || CountY <= 0;
}
