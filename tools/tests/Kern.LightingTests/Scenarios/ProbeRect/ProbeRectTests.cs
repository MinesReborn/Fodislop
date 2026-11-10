#nullable enable

using Kern.World.Lighting;

namespace Kern.LightingTests;

// Local-ray bounds and full-chain dependency coverage. The latter independently
// enumerates each near probe's four clamped far samples, including edge rows.
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

        // 48x48 probes exceed half of 64x64, but their geometric bounds still
        // fit inside the grid. Area alone must not expand the dispatch.
        ProbeRect majority = CascadeProbeRects.ForCascade(
            20, 20, 80, 80, 128, 128, fine, 6f);
        Check("majority-keeps-geometric-bounds", majority is { X: 1, Y: 1, Width: 48, Height: 48 });
        Check("majority-coverage", CoversReach(20, 20, 80, 80, 12f, 6f, 128, 128, fine, majority));

        // Out-of-field dirt -> empty rect -> dispatch skipped.
        ProbeRect empty = CascadeProbeRects.ForCascade(
            200, 200, 210, 210, 128, 128, fine, 6f);
        Check("empty-out-of-field", empty.IsEmpty);

        // Whole-field dirt requires the full grid geometrically.
        ProbeRect full = CascadeProbeRects.ForCascade(
            0, 0, 128, 128, 128, 128, fine, 6f);
        Check(
            "full-coverage",
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

        CheckCascadeChainCoverage();
        CheckDependencyMaskEligibility();

        if (_failures > 0)
        {
            Console.Error.WriteLine($"ProbeRectTests: {_failures} failure(s).");
            return 1;
        }

        Console.WriteLine("ProbeRectTests: OK.");
        return 0;
    }

    private static void CheckDependencyMaskEligibility()
    {
        Check("no-dirty-regions-no-mask", !StaticLightingDirty.ShouldUseDependencyMask(
            Array.Empty<StaticLightingDirty.DirtyRegionGPU>(), 128, 128));
        Check("full-field-no-mask", !StaticLightingDirty.ShouldUseDependencyMask(
            new[] { new StaticLightingDirty.DirtyRegionGPU(0, 0, 128, 128) }, 128, 128));
        Check("regional-change-keeps-mask", StaticLightingDirty.ShouldUseDependencyMask(
            new[] { new StaticLightingDirty.DirtyRegionGPU(4, 4, 8, 8) }, 128, 128));
        Check("large-region-keeps-mask", StaticLightingDirty.ShouldUseDependencyMask(
            new[] { new StaticLightingDirty.DirtyRegionGPU(0, 0, 127, 128) }, 128, 128));

        // Repeating overlapping edits changes neither coverage nor eligibility.
        var repeated = new StaticLightingDirty.DirtyRegionGPU[1024];
        Array.Fill(repeated, new StaticLightingDirty.DirtyRegionGPU(4, 4, 8, 8));
        Check("overlap-count-does-not-disable-mask", StaticLightingDirty.ShouldUseDependencyMask(repeated, 128, 128));
        repeated[^1] = new StaticLightingDirty.DirtyRegionGPU(0, 0, 128, 128);
        Check("full-field-after-regional-change-no-mask", !StaticLightingDirty.ShouldUseDependencyMask(repeated, 128, 128));
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

    private static void CheckCascadeChainCoverage()
    {
        CascadeLayout[] chain =
        [
            new(0, 64 * 60 * 4, 64, 60, 2, 4, 0, 1),
            new(0, 32 * 30 * 16, 32, 30, 4, 16, 1, 4),
            new(0, 16 * 15 * 64, 16, 15, 8, 64, 4, 12),
        ];
        foreach ((int x, int y) in new[] { (60, 56), (0, 0), (124, 116), (0, 116), (124, 0) })
        {
            Span<ProbeRect> rectangles = stackalloc ProbeRect[chain.Length];
            CascadeProbeRects.FillCascadeChain(x, y, x + 4, y + 4, 127, 119, chain, 0, rectangles);
            for (int level = 0; level + 1 < chain.Length; level++)
            {
                CascadeLayout near = chain[level];
                CascadeLayout far = chain[level + 1];
                ProbeRect nearRect = rectangles[level];
                ProbeRect farRect = rectangles[level + 1];
                for (int py = 0; py < near.ProbeHeight; py++)
                {
                    for (int px = 0; px < near.ProbeWidth; px++)
                    {
                        int baseX = (int)Math.Floor((px + 0.5) * near.ProbeSpacing / far.ProbeSpacing - 0.5);
                        int baseY = (int)Math.Floor((py + 0.5) * near.ProbeSpacing / far.ProbeSpacing - 0.5);
                        bool readsChangedFarProbe = false;
                        for (int oy = 0; oy < 2; oy++)
                        {
                            for (int ox = 0; ox < 2; ox++)
                            {
                                int farX = Math.Clamp(baseX + ox, 0, far.ProbeWidth - 1);
                                int farY = Math.Clamp(baseY + oy, 0, far.ProbeHeight - 1);
                                readsChangedFarProbe |= farX >= farRect.X && farX < farRect.X + farRect.Width &&
                                    farY >= farRect.Y && farY < farRect.Y + farRect.Height;
                            }
                        }

                        Check("near-dispatch-covers-far-input", !readsChangedFarProbe ||
                            (px >= nearRect.X && px < nearRect.X + nearRect.Width &&
                            py >= nearRect.Y && py < nearRect.Y + nearRect.Height));
                    }
                }
            }
        }
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
