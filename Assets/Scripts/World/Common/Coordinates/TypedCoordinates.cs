#nullable enable

using System;
using UnityEngine;

namespace Kern.World.Coordinates;

/// <summary>
/// Continuous world coordinates in Unity world space (meters, Y-up).
/// </summary>
public readonly record struct WorldCoord(float X, float Y)
{
    public CellCoord ToCell(float cellSize)
    {
        float s = MathF.Max(cellSize, 0.0001f);
        return new CellCoord((int)MathF.Floor(X / s), (int)MathF.Floor(Y / s));
    }

    public static implicit operator Vector2(WorldCoord c) => new(c.X, c.Y);
    public static implicit operator WorldCoord(Vector2 v) => new(v.x, v.y);
    public static implicit operator Vector3(WorldCoord c) => new(c.X, c.Y, 0f);
    public static implicit operator WorldCoord(Vector3 v) => new(v.x, v.y);
}

/// <summary>
/// Discrete grid cell coordinate in Unity world space (cells, Y-up).
/// </summary>
public readonly record struct CellCoord(int X, int Y)
{
    public WorldCoord ToWorldCenter(float cellSize) => new((X + 0.5f) * cellSize, (Y + 0.5f) * cellSize);

    public ServerCellCoord ToServer(int worldHeight) =>
        new(X, CoordinateUtils.UnityToServerY(Y, worldHeight));

    public static implicit operator Vector2Int(CellCoord c) => new(c.X, c.Y);
    public static implicit operator CellCoord(Vector2Int v) => new(v.x, v.y);
}

/// <summary>
/// Server authoritative tile coordinate (cells, Y-down, origin top-left).
/// Guaranteed to prevent accidental passing into Unity world functions.
/// </summary>
public readonly record struct ServerCellCoord(int X, int Y)
{
    public CellCoord ToUnityCell(int worldHeight) =>
        new(X, (int)MathF.Floor(CoordinateUtils.ServerToUnityY(Y, worldHeight)));

    public static implicit operator Vector2Int(ServerCellCoord c) => new(c.X, c.Y);
    public static implicit operator ServerCellCoord(Vector2Int v) => new(v.x, v.y);
}

/// <summary>
/// Discrete pixel coordinate within the lighting field texture [0..FieldWidth-1, 0..FieldHeight-1].
/// </summary>
public readonly record struct FieldPixelCoord(int X, int Y)
{
    public static implicit operator Vector2Int(FieldPixelCoord c) => new(c.X, c.Y);
    public static implicit operator FieldPixelCoord(Vector2Int v) => new(v.x, v.y);
}
