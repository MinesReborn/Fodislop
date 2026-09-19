#nullable enable

using Kern.World.Lighting;

namespace Kern.LightingTests;

// Golden test for partial cascade dispatch (mask path): the tight probe rect
// must cover every probe whose ray segment can touch the dirty bounds, must
// be empty only for out-of-field dirt, and must fall back to full grid when
// the dirty area is large. Pure C#, runs without Unity.
internal static class ProbeRectTests
{
    private static int _failures;

    public static int Run()
    {
        // Fine cascade: 128x128 field, 64x64 probes (spacing 2), short reach.
        var fine = new CascadeLayout(
            Offset: 0,
            EntryCount: 64 * 64 * 4,
            ProbeWidth: 64,
            ProbeHeight: 64,
            ProbeSpacing: 2,
            DirectionCount: 4,
            IntervalStart: 0f,
            IntervalEnd: 12f);

        // Small dirty square in the middle, margin 6 texels.
        ProbeRect tight = CascadeProbeRects.ForCascade(
            60, 60, 68, 68, 128, 128, fine, 6f);
        // Expanded bounds: 60-18 .. 68+18 = 42..86 texels -> probes 21..43.
        Check("tight-not-empty", !tight.IsEmpty);
        Check("tight-x", tight.X == 21);
        Check("tight-y", tight.Y == 21);
        Check("tight-w", tight.Width == 22);
        Check("tight-h", tight.Height == 22);
        Check(
            "tight-smaller-than-grid",
            (long)tight.Width * tight.Height < (long)64 * 64 / 2);

        // Coverage property: every probe whose center (mapped to field texels)
        // lies within reach+margin of the dirty bounds must be inside the rect.
        Check("coverage", CoversReach(60, 60, 68, 68, 12f, 6f, 128, 128, fine, tight));

        // Out-of-field dirt -> empty rect -> dispatch skipped.
        ProbeRect empty = CascadeProbeRects.ForCascade(
            200, 200, 210, 210, 128, 128, fine, 6f);
        Check("empty-out-of-field", empty.IsEmpty);

        // Whole-field dirt -> full grid fallback.
        ProbeRect full = CascadeProbeRects.ForCascade(
            0, 0, 128, 128, 128, 128, fine, 6f);
        Check(
            "full-fallback",
            full is { X: 0, Y: 0, Width: 64, Height: 64 });

        // Coarse cascade with huge interval -> full grid (far levels always
        // re-solve; they are also the cheapest).
        var coarse = fine with { ProbeWidth = 8, ProbeHeight = 8, IntervalEnd = 500f };
        ProbeRect coarseRect = CascadeProbeRects.ForCascade(
            60, 60, 68, 68, 128, 128, coarse, 6f);
        Check(
            "coarse-full",
            coarseRect is { X: 0, Y: 0, Width: 8, Height: 8 });

        // Degenerate grid -> full (never empty, never crash).
        var degenerate = fine with { ProbeWidth = 0 };
        ProbeRect degenerateRect = CascadeProbeRects.ForCascade(
            60, 60, 68, 68, 128, 128, degenerate, 6f);
        Check("degenerate-full", degenerateRect.Width == 0 && degenerateRect.Height == 64);

        if (_failures > 0)
        {
            Console.Error.WriteLine($"ProbeRectTests: {_failures} failure(s).");
            return 1;
        }

        Console.WriteLine("ProbeRectTests: OK.");
        return 0;
    }

    private static bool CoversReach(
        int dirtyMinX, int dirtyMinY, int dirtyMaxX, int dirtyMaxY,
        float reach, float margin,
        int fieldWidth, int fieldHeight,
        CascadeLayout cascade, ProbeRect rect)
    {
        float radius = reach + margin;
        for (int py = 0; py < cascade.ProbeHeight; py++)
        {
            for (int px = 0; px < cascade.ProbeWidth; px++)
            {
                // Probe center in field texels.
                float cx = ((px + 0.5f) * fieldWidth) / cascade.ProbeWidth;
                float cy = ((py + 0.5f) * fieldHeight) / cascade.ProbeHeight;
                float dx = 0f;
                if (cx < dirtyMinX)
                    dx = dirtyMinX - cx;
                else if (cx > dirtyMaxX)
                    dx = cx - dirtyMaxX;
                float dy = 0f;
                if (cy < dirtyMinY)
                    dy = dirtyMinY - cy;
                else if (cy > dirtyMaxY)
                    dy = cy - dirtyMaxY;
                bool withinReach = dx <= radius && dy <= radius;
                bool inside = px >= rect.X && px < rect.X + rect.Width &&
                    py >= rect.Y && py < rect.Y + rect.Height;
                if (withinReach && !inside)
                    return false;
            }
        }

        return true;
    }

    private static void Check(string name, bool condition)
    {
        if (!condition)
        {
            Console.Error.WriteLine($"ProbeRectTests: FAIL {name}.");
            _failures++;
        }
    }
}
