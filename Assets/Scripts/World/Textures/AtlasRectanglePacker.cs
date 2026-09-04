#nullable enable

using System;
using System.Collections.Generic;

namespace Fodinae.World;

/// <summary>
/// 2D guillotine/shelf rectangle bin-packer for texture atlas allocations.
/// </summary>
internal sealed class AtlasRectanglePacker
{
    private readonly int _size;
    private readonly int _padding;
    private readonly List<Rectangle> _freeRectangles = new();
    private readonly List<Rectangle> _usedRectangles = new();

    public AtlasRectanglePacker(int size, int padding)
    {
        _size = size;
        _padding = padding;
        _freeRectangles.Add(new Rectangle(0, 0, size, size));
    }

    public void Clear()
    {
        _usedRectangles.Clear();
        _freeRectangles.Clear();
        _freeRectangles.Add(new Rectangle(0, 0, _size, _size));
    }

    public bool TryAllocate(int width, int height, out Rectangle allocatedRect)
    {
        var bestFit = FindBestFit(width, height);
        if (bestFit == null)
        {
            allocatedRect = default;
            return false;
        }

        allocatedRect = bestFit.Value;
        var rectWithPadding = new Rectangle(
            allocatedRect.X,
            allocatedRect.Y,
            allocatedRect.Width + _padding,
            allocatedRect.Height + _padding);

        _usedRectangles.Add(rectWithPadding);
        SplitFreeRectangles(rectWithPadding);
        return true;
    }

    private Rectangle? FindBestFit(int width, int height)
    {
        Rectangle? bestFit = null;
        int bestScore = int.MaxValue;
        foreach (var freeRect in _freeRectangles)
        {
            if (freeRect.Width >= width + _padding && freeRect.Height >= height + _padding)
            {
                int score = (freeRect.Width - width) * (freeRect.Height - height);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestFit = new Rectangle(freeRect.X, freeRect.Y, width, height);
                }
            }
        }

        return bestFit;
    }

    private void SplitFreeRectangles(Rectangle usedRect)
    {
        var newFree = new List<Rectangle>();
        foreach (var free in _freeRectangles)
        {
            if (Intersects(free, usedRect))
            {
                SplitRectangle(free, usedRect, newFree);
            }
            else
            {
                newFree.Add(free);
            }
        }

        _freeRectangles.Clear();
        _freeRectangles.AddRange(newFree);
    }

    private static void SplitRectangle(Rectangle free, Rectangle used, List<Rectangle> newFree)
    {
        if (used.Y > free.Y)
        {
            newFree.Add(new Rectangle(free.X, free.Y, free.Width, used.Y - free.Y));
        }

        if (used.Y + used.Height < free.Y + free.Height)
        {
            newFree.Add(new Rectangle(free.X, used.Y + used.Height, free.Width, (free.Y + free.Height) - (used.Y + used.Height)));
        }

        if (used.X > free.X)
        {
            newFree.Add(new Rectangle(free.X, free.Y, used.X - free.X, free.Height));
        }

        if (used.X + used.Width < free.X + free.Width)
        {
            newFree.Add(new Rectangle(used.X + used.Width, free.Y, (free.X + free.Width) - (used.X + used.Width), free.Height));
        }
    }

    private static bool Intersects(Rectangle a, Rectangle b)
    {
        return a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;
    }
}
