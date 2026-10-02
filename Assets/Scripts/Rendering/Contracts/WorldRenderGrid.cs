#nullable enable

using System;

namespace Kern.Rendering;

/// <summary>
/// Immutable renderer-owned layout in Unity Y-up world cells. Bounds are half-open.
/// No resources, camera mutation, display transform, or lighting transport policy live here.
/// A renderer replaces this value when coverage changes; resource reuse compares Width/Height.
/// </summary>
public readonly struct WorldRenderGrid
{
    public const int PixelsPerCell = 32;
    public const int GuardPixels = 1;

    public int MinPixelX { get; }
    public int MinPixelY { get; }
    public int Width { get; }
    public int Height { get; }
    public double ViewportMinX { get; }
    public double ViewportMinY { get; }
    public double ViewportWidth { get; }
    public double ViewportHeight { get; }
    public double WorldMinX => (double)MinPixelX / PixelsPerCell;
    public double WorldMinY => (double)MinPixelY / PixelsPerCell;
    public double WorldWidth => (double)Width / PixelsPerCell;
    public double WorldHeight => (double)Height / PixelsPerCell;
    public long PixelCount => (long)Width * Height;

    private WorldRenderGrid(int minPixelX, int minPixelY, int width, int height,
        double viewportMinX, double viewportMinY, double viewportWidth, double viewportHeight)
    {
        MinPixelX = minPixelX;
        MinPixelY = minPixelY;
        Width = width;
        Height = height;
        ViewportMinX = viewportMinX;
        ViewportMinY = viewportMinY;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
    }

    /// <summary>Outward rounding followed by one guard pixel on each edge; never reduces density.</summary>
    public static WorldRenderGrid Create(double viewportMinX, double viewportMinY,
        double viewportWidth, double viewportHeight)
    {
        RequireFinite(viewportMinX, nameof(viewportMinX));
        RequireFinite(viewportMinY, nameof(viewportMinY));
        RequirePositive(viewportWidth, nameof(viewportWidth));
        RequirePositive(viewportHeight, nameof(viewportHeight));
        double maxX = viewportMinX + viewportWidth;
        double maxY = viewportMinY + viewportHeight;
        if (maxX <= viewportMinX || maxY <= viewportMinY)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportWidth), "Viewport extent is not representable.");
        }

        int minX = checked((int)Math.Floor(viewportMinX * PixelsPerCell) - GuardPixels);
        int minY = checked((int)Math.Floor(viewportMinY * PixelsPerCell) - GuardPixels);
        int endX = checked((int)Math.Ceiling(maxX * PixelsPerCell) + GuardPixels);
        int endY = checked((int)Math.Ceiling(maxY * PixelsPerCell) + GuardPixels);
        int width = checked(endX - minX);
        int height = checked(endY - minY);
        return new WorldRenderGrid(minX, minY, width, height,
            viewportMinX, viewportMinY, viewportWidth, viewportHeight);
    }

    /// <summary>Pixel centers, with integer x/y relative to the world target's bottom-left.</summary>
    public (double X, double Y) PixelCenter(int x, int y)
    {
        RequireValid();
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Pixel lies outside the world target.");
        }

        return (((double)MinPixelX + x + 0.5) / PixelsPerCell,
            ((double)MinPixelY + y + 0.5) / PixelsPerCell);
    }

    /// <summary>
    /// Original viewport UV to guarded world-target UV. UVs are bottom-left/Y-up;
    /// the graphics API's texture flip belongs to the renderer binding boundary.
    /// </summary>
    public (double U, double V) ViewportToWorldUv(double u, double v)
    {
        RequireValid();
        RequireFinite(u, nameof(u));
        RequireFinite(v, nameof(v));
        return ((ViewportMinX - WorldMinX + u * ViewportWidth) / WorldWidth,
            (ViewportMinY - WorldMinY + v * ViewportHeight) / WorldHeight);
    }

    /// <summary>World point to original viewport UV, retaining the camera used by UI and input.</summary>
    public (double U, double V) WorldToViewportUv(double x, double y)
    {
        RequireValid();
        RequireFinite(x, nameof(x));
        RequireFinite(y, nameof(y));
        return ((x - ViewportMinX) / ViewportWidth, (y - ViewportMinY) / ViewportHeight);
    }

    public bool HasSameTargetSize(WorldRenderGrid other)
    {
        RequireValid();
        other.RequireValid();
        return Width == other.Width && Height == other.Height;
    }

    private void RequireValid()
    {
        if (Width <= 0 || Height <= 0)
        {
            throw new InvalidOperationException("WorldRenderGrid must be constructed from valid viewport coverage.");
        }
    }

    private static void RequireFinite(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(name, "A finite value is required.");
        }
    }

    private static void RequirePositive(double value, string name)
    {
        RequireFinite(value, name);
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, "A positive extent is required.");
        }
    }
}
