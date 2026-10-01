#nullable enable

using System;

namespace Kern.Rendering.PostProcessing;

/// <summary>Absolute world-pixel addressing for a small bloom target; never camera-relative phase.</summary>
internal readonly struct WorldBloomLevel(int minX, int minY, int width, int height, int stride)
{
    public int MinX { get; } = minX;
    public int MinY { get; } = minY;
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int Stride { get; } = stride;

    public static WorldBloomLevel Cover(int worldMinX, int worldMinY, int worldWidth, int worldHeight, int stride)
    {
        if (worldWidth <= 0 || worldHeight <= 0 || stride <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(worldWidth));
        }

        int minX = checked((int)Math.Floor((double)worldMinX / stride));
        int minY = checked((int)Math.Floor((double)worldMinY / stride));
        int endX = checked((int)Math.Ceiling(((double)worldMinX + worldWidth) / stride));
        int endY = checked((int)Math.Ceiling(((double)worldMinY + worldHeight) / stride));
        return new WorldBloomLevel(checked(minX * stride), checked(minY * stride),
            checked(endX - minX), checked(endY - minY), stride);
    }

    public (double X, double Y, double OffsetX, double OffsetY) MapTo(WorldBloomLevel source)
    {
        return ((double)Width * Stride / (source.Width * source.Stride),
            (double)Height * Stride / (source.Height * source.Stride),
            ((double)MinX - source.MinX) / (source.Width * source.Stride),
            ((double)MinY - source.MinY) / (source.Height * source.Stride));
    }
}
