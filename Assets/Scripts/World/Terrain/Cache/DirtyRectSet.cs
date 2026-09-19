#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace Kern.World.Terrain;
public sealed class DirtyRectSet
{
    private readonly List<RectInt> _rects = new(8);

    public int Count => _rects.Count;

    public bool IsEmpty => _rects.Count == 0;

    public RectInt this[int index] => _rects[index];

    public void Clear()
    {
        _rects.Clear();
    }

    public long TotalArea
    {
        get
        {
            long total = 0;
            for (int i = 0; i < _rects.Count; i++)
            {
                total += Area(_rects[i]);
            }

            return total;
        }
    }

    /// <returns>
    /// False when the rectangle lies wholly outside the bounds or is empty,
    /// in which case nothing was recorded.
    /// </returns>
    public bool Add(RectInt candidate, RectInt bounds)
    {
        RectInt clipped = Intersect(candidate, bounds);
        if (clipped.width <= 0 || clipped.height <= 0)
        {
            return false;
        }

        RectInt merged = clipped;
        for (int i = 0; i < _rects.Count;)
        {
            RectInt existing = _rects[i];
            if (Contains(existing, merged))
            {
                return true;
            }

            // Merge only where the union costs no more than keeping the two
            // rectangles apart - touching or overlapping ones. Merging
            // distant rectangles is what produced the screen-sized union.
            RectInt union = Union(existing, merged);
            if (IntersectsOrTouches(existing, merged) ||
                Area(union) <= Area(existing) + Area(merged))
            {
                merged = union;
                _rects.RemoveAt(i);
                i = 0;
                continue;
            }

            i++;
        }

        _rects.Add(merged);
        return true;
    }

    public static long Area(RectInt rect)
    {
        return (long)rect.width * rect.height;
    }

    private static bool Contains(RectInt outer, RectInt inner)
    {
        return inner.xMin >= outer.xMin && inner.xMax <= outer.xMax &&
            inner.yMin >= outer.yMin && inner.yMax <= outer.yMax;
    }

    private static RectInt Union(RectInt a, RectInt b)
    {
        int minX = Mathf.Min(a.xMin, b.xMin);
        int minY = Mathf.Min(a.yMin, b.yMin);
        int maxX = Mathf.Max(a.xMax, b.xMax);
        int maxY = Mathf.Max(a.yMax, b.yMax);
        return new RectInt(minX, minY, maxX - minX, maxY - minY);
    }

    private static RectInt Intersect(RectInt a, RectInt b)
    {
        // Built from long arithmetic because the server supplies the
        // candidate: a hostile or simply buggy chunk rectangle can carry
        // int.MaxValue extents, and computing xMax as x + width in int
        // overflows to a negative number, which would turn a rejected
        // rectangle into an accepted one.
        long aMinX = a.xMin;
        long aMinY = a.yMin;
        long aMaxX = aMinX + a.width;
        long aMaxY = aMinY + a.height;
        long rawMaxX = (long)a.x + a.width;
        long rawMaxY = (long)a.y + a.height;

        // Reject malformed protocol rectangles before clipping. Otherwise
        // an overflowing endpoint can wrap around and appear to overlap
        // the cached region.
        if (rawMaxX > int.MaxValue || rawMaxX < int.MinValue ||
            rawMaxY > int.MaxValue || rawMaxY < int.MinValue)
        {
            return new RectInt(0, 0, 0, 0);
        }

        long minX = System.Math.Max(aMinX, b.xMin);
        long minY = System.Math.Max(aMinY, b.yMin);
        long maxX = System.Math.Min(aMaxX, b.xMax);
        long maxY = System.Math.Min(aMaxY, b.yMax);

        if (maxX <= minX || maxY <= minY)
        {
            return new RectInt(0, 0, 0, 0);
        }

        return new RectInt((int)minX, (int)minY, (int)(maxX - minX), (int)(maxY - minY));
    }

    private static bool IntersectsOrTouches(RectInt left, RectInt right)
    {
        return left.xMin <= right.xMax &&
            left.xMax >= right.xMin &&
            left.yMin <= right.yMax &&
            left.yMax >= right.yMin;
    }
}
