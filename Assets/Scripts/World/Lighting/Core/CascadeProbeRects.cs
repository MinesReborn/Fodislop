#nullable enable

namespace Kern.World.Lighting;

// Tight per-cascade probe rect for dependency-mask solves.
//
// A probe entry changes only if its transport inputs changed: geometry or
// emission along its ray segment, or the far-field values it reads. The far
// cascade solves its own rect from the same dirty bounds, so a near probe is
// covered when every probe whose segment can touch the dirty bounds is
// dispatched. Segment reach is bounded by the cascade interval length, hence
// the rect is the dirty bounds expanded by interval + margin, mapped into
// probe space. Far cascades have huge intervals, so they naturally fall back
// to the full grid (which is also where the cost is lowest: coarse grids).
//
// Pure C# (no UnityEngine) so tools/lighting-tests links this file directly
// for the full-vs-partial golden test.
public readonly record struct ProbeRect(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public static class CascadeProbeRects
{
    // Fraction of the cascade grid above which the tight rect is not worth
    // it: dispatch the full grid instead (same cost class, simpler).
    public const float FullFallbackFraction = 0.5f;

    public static ProbeRect ForCascade(
        int fieldMinX,
        int fieldMinY,
        int fieldMaxXExclusive,
        int fieldMaxYExclusive,
        int fieldWidth,
        int fieldHeight,
        CascadeLayout cascade,
        float marginTexels)
    {
        if (fieldWidth <= 0 || fieldHeight <= 0 ||
            cascade.ProbeWidth <= 0 || cascade.ProbeHeight <= 0)
        {
            return Full(cascade);
        }

        float reach = cascade.IntervalEnd > 0f ? cascade.IntervalEnd : 0f;
        float expand = reach + (marginTexels > 0f ? marginTexels : 0f);
        float minX = fieldMinX - expand;
        float minY = fieldMinY - expand;
        float maxX = fieldMaxXExclusive + expand;
        float maxY = fieldMaxYExclusive + expand;

        float scaleX = (float)cascade.ProbeWidth / fieldWidth;
        float scaleY = (float)cascade.ProbeHeight / fieldHeight;
        int probeMinX = Clamp((int)System.Math.Floor(minX * scaleX), 0, cascade.ProbeWidth);
        int probeMinY = Clamp((int)System.Math.Floor(minY * scaleY), 0, cascade.ProbeHeight);
        int probeMaxX = Clamp((int)System.Math.Ceiling(maxX * scaleX), 0, cascade.ProbeWidth);
        int probeMaxY = Clamp((int)System.Math.Ceiling(maxY * scaleY), 0, cascade.ProbeHeight);

        if (probeMaxX <= probeMinX || probeMaxY <= probeMinY)
        {
            return new ProbeRect(0, 0, 0, 0);
        }

        long tightArea = (long)(probeMaxX - probeMinX) * (probeMaxY - probeMinY);
        long gridArea = (long)cascade.ProbeWidth * cascade.ProbeHeight;
        if (tightArea >= gridArea * FullFallbackFraction)
        {
            return Full(cascade);
        }

        return new ProbeRect(probeMinX, probeMinY, probeMaxX - probeMinX, probeMaxY - probeMinY);
    }

    public static ProbeRect Full(CascadeLayout cascade)
    {
        return new ProbeRect(0, 0, cascade.ProbeWidth, cascade.ProbeHeight);
    }

    private static int Clamp(int value, int min, int max)
    {
        return value < min ? min : (value > max ? max : value);
    }
}
