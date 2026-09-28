#nullable enable

using System;
using Kern.World;
using UnityEngine;

namespace Kern.UI;

internal sealed class WorldMapBounds
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public string CodeName { get; private set; } = string.Empty;

    public void Bind(MapManager manager, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException(
                $"[WorldMapRenderer] Invalid world dimensions: {width}x{height}.");
        }

        if (string.IsNullOrWhiteSpace(manager.WorldCodeName))
        {
            throw new InvalidOperationException(
                "[WorldMapRenderer] World code name is required before binding map state.");
        }

        Width = width;
        Height = height;
        CodeName = manager.WorldCodeName;
    }

    public bool Matches(MapManager manager) =>
        manager.WorldWidth == Width &&
        manager.WorldHeight == Height &&
        string.Equals(manager.WorldCodeName, CodeName, StringComparison.Ordinal);

    public float ComputeMaxZoomOut(int textureWidth, int textureHeight) =>
        MapViewportBounds.ComputeMaxZoomOut(textureWidth, textureHeight, Width, Height);

    public void Clamp(
        ref float centerX,
        ref float centerY,
        float cellsPerPixel,
        int textureWidth,
        int textureHeight) =>
        MapViewportBounds.ClampViewCenter(
            ref centerX,
            ref centerY,
            cellsPerPixel,
            textureWidth,
            textureHeight,
            Width,
            Height);
}
