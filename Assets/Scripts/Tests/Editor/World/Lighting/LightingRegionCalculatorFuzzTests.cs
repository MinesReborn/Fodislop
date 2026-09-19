#nullable enable

using Kern.World.Lighting;
using Kern.World.Streaming;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World.Lighting;

[TestFixture]
public class LightingRegionCalculatorFuzzTests
{
    private static int Padding => LightingRegionCalculator.LightingRegionPaddingCells;

    private static int Quantum => StreamingPolicy.Default.AllocationQuantumCells;

    private const int MinCell = StreamingPolicy.DefaultMinimumWindowDimension;
    private const int MaximumCellWindow = StreamingPolicy.DefaultMaximumWindowDimension;

    private static readonly int[] _Seeds = [1, 7, 42, 1337, 90210, 2147483, 8675309];

    private static readonly int[] _HostileCoords = [-10_000_000, -1000, -17, -8, -1, 0, 1, 7, 8, 9, 1000, 10_000_000];

    private static readonly int[] _HostileExtents = [-1_000_000, -32, -1, 0, 1, 7, 32, 1000, 10_000_000];

    [Test]
    public void FreshRegionsAreAnchoredQuantizedAndCoverThePaddedViewport([ValueSource(nameof(_Seeds))] int seed)
    {
        var random = new System.Random(seed);

        for (int iteration = 0; iteration < 3000; iteration++)
        {
            (int minX, int minY, int width, int height) = RandomViewport(random);
            Vector4 region = LightingRegionCalculator.GetStableLightingRegion(
                minX, minY, width, height,
                new Vector4(float.NaN, 0, 0, 0));

            AssertFreshRegion(region, minX, minY, width, height, seed, iteration);
        }
    }

    [Test]
    public void TheNaNSentinelForcesAFreshRegionRegardlessOfOtherComponents([ValueSource(nameof(_Seeds))] int seed)
    {
        var random = new System.Random(seed);

        for (int iteration = 0; iteration < 1000; iteration++)
        {
            (int minX, int minY, int width, int height) = RandomViewport(random);
            Vector4 canonical = LightingRegionCalculator.GetStableLightingRegion(
                minX, minY, width, height,
                new Vector4(float.NaN, 0, 0, 0));

            // Garbage in y/z/w must be ignored: x alone is the validity
            // sentinel, so both calls have to compute the same fresh region.
            Vector4 withGarbage = LightingRegionCalculator.GetStableLightingRegion(
                minX, minY, width, height,
                new Vector4(float.NaN, -777f, 123_456f, 9_000_000f));

            Assert.That(
                withGarbage,
                Is.EqualTo(canonical),
                $"seed {seed}, iteration {iteration}: sentinel must ignore y/z/w garbage.");
            Assert.That(float.IsNaN(withGarbage.x), Is.False, $"seed {seed}, iteration {iteration}");
        }
    }

    [Test]
    public void TheContainmentDecisionMatchesTheReferenceForEveryRandomInput([ValueSource(nameof(_Seeds))] int seed)
    {
        var random = new System.Random(seed);

        for (int iteration = 0; iteration < 3000; iteration++)
        {
            Vector4 previous = RandomValidRegion(random);
            (int minX, int minY, int width, int height) = RandomViewport(random);

            Vector4 result = LightingRegionCalculator.GetStableLightingRegion(
                minX, minY, width, height, previous);

            // The reference re-derives the reanchor rule from the shared
            // streaming policy. Lighting and terrain must make the same
            // decision for the same window geometry.
            int currentMinX = Mathf.RoundToInt(previous.x);
            int currentMinY = Mathf.RoundToInt(previous.y);
            int regionWidth = Mathf.RoundToInt(previous.z);
            int regionHeight = Mathf.RoundToInt(previous.w);
            bool inside = StreamingPolicy.Default.ContainsViewport(
                new Vector2Int(regionWidth, regionHeight),
                new Vector2Int(minX - currentMinX, minY - currentMinY),
                new Vector2Int(width, height));

            Vector4 expected = inside
                ? previous
                : ReanchoredReference(minX, minY, width, height, previous);

            Assert.That(
                result,
                Is.EqualTo(expected),
                $"seed {seed}, iteration {iteration}: viewport {minX},{minY} {width}x{height} " +
                $"against region {previous}, inside={inside}.");
        }
    }

    [Test]
    public void HostilePreviousRegionsNeverThrowAndNeverProduceNaN([ValueSource(nameof(_Seeds))] int seed)
    {
        var random = new System.Random(seed);
        Vector4[] hostileRegions =
        [
            new(float.NaN, 0, 0, 0),                       // the sentinel
            new(float.NaN, 1000, 2000, 3000),              // sentinel + plausible garbage
            new(0, 0, -100, 200),                          // negative width
            new(-16, -16, 100, -100),                      // negative height
            new(0, 0, 2, 2),                               // degenerate minimum
            new(-10_000_000, -10_000_000, 20_000_000, 20_000_000), // giant
        ];

        for (int iteration = 0; iteration < 1000; iteration++)
        {
            Vector4 previous = hostileRegions[random.Next(hostileRegions.Length)];
            (int minX, int minY, int width, int height) = RandomViewport(random);

            Vector4 result = LightingRegionCalculator.GetStableLightingRegion(
                minX, minY, width, height, previous);

            Assert.That(
                float.IsNaN(result.x) || float.IsNaN(result.y) || float.IsNaN(result.z) || float.IsNaN(result.w),
                Is.False,
                $"seed {seed}, iteration {iteration}: hostile previous {previous} produced NaN.");

            if (float.IsNaN(previous.x))
            {
                // The sentinel always recomputes: the result is a fresh
                // region and must satisfy the fresh-region contract.
                AssertFreshRegion(result, minX, minY, width, height, seed, iteration);
            }
            else if (result != previous)
            {
                // Either the region was returned verbatim (fine even when
                // the previous region is degenerate - the caller asked for
                // stability) or a fresh compute happened, which must be
                // valid geometry.
                AssertFreshRegion(result, minX, minY, width, height, seed, iteration);
            }
        }
    }

    private static (int MinX, int MinY, int Width, int Height) RandomViewport(System.Random random)
    {
        // A quarter of the draws come from the hostile pool, so negative
        // extents, zero extents and magnitudes near the stress range are
        // hit constantly rather than by luck. All values stay bounded so
        // min + extent never overflows int.
        if (random.Next(4) == 0)
        {
            return (
                Pick(random, _HostileCoords),
                Pick(random, _HostileCoords),
                Pick(random, _HostileExtents),
                Pick(random, _HostileExtents));
        }

        return (
            random.Next(-1_000_000, 1_000_001),
            random.Next(-1_000_000, 1_000_001),
            random.Next(0, 2001),
            random.Next(0, 2001));
    }

    private static Vector4 RandomValidRegion(System.Random random)
    {
        int width = random.Next(1, 257) * Quantum;
        int height = random.Next(1, 257) * Quantum;
        return new Vector4(
            random.Next(-1_000_000, 1_000_001),
            random.Next(-1_000_000, 1_000_001),
            width,
            height);
    }

    private static void AssertFreshRegion(
        Vector4 region,
        int minX,
        int minY,
        int width,
        int height,
        int seed,
        int iteration)
    {
        string context = $"seed {seed}, iteration {iteration}: viewport {minX},{minY} {width}x{height}, region {region}.";

        Assert.That(region.z, Is.GreaterThanOrEqualTo(MinCell), context);
        Assert.That(region.w, Is.GreaterThanOrEqualTo(MinCell), context);

        // Sizes sit on the 32-cell quantum, except when the padded extent
        // is <= 0 (degenerate viewport) and the size clamps to the 2-cell
        // minimum. A negative-zero modulo still compares equal to zero.
        Assert.That(
            region.z % Quantum == 0f || region.z == MinCell,
            Is.True,
            context + " width must be a 32-cell quantum or the 2-cell minimum.");
        Assert.That(
            region.w % Quantum == 0f || region.w == MinCell,
            Is.True,
            context + " height must be a 32-cell quantum or the 2-cell minimum.");

        // The policy intentionally caps resident windows. Oversized or
        // inverted fuzz inputs test the cap below, while only representable
        // camera viewports are required to be fully covered.
        if (FitsPolicyWindow(width) && FitsPolicyWindow(height))
        {
            Assert.That((long)region.x, Is.LessThanOrEqualTo((long)minX - Padding), context + " west padding missing.");
            Assert.That((long)region.y, Is.LessThanOrEqualTo((long)minY - Padding), context + " south padding missing.");
            Assert.That(
                (long)region.x + (long)region.z,
                Is.GreaterThanOrEqualTo((long)minX + width + Padding),
                context + " east padding missing.");
            Assert.That(
                (long)region.y + (long)region.w,
                Is.GreaterThanOrEqualTo((long)minY + height + Padding),
                context + " north padding missing.");
        }
    }

    private static bool FitsPolicyWindow(int extent)
    {
        return extent >= 0 &&
            (long)extent + (Padding * 2L) + Quantum - 1 <= MaximumCellWindow;
    }

    private static Vector4 ReanchoredReference(
        int minX,
        int minY,
        int width,
        int height,
        Vector4 previous)
    {
        Vector4 fresh = LightingRegionCalculator.GetStableLightingRegion(
            minX,
            minY,
            width,
            height,
            new Vector4(float.NaN, 0, 0, 0));
        int widthHighWater = StreamingPolicy.Default.QuantizeDimension(
            Mathf.Max(Mathf.RoundToInt(fresh.z), Mathf.RoundToInt(previous.z)));
        int heightHighWater = StreamingPolicy.Default.QuantizeDimension(
            Mathf.Max(Mathf.RoundToInt(fresh.w), Mathf.RoundToInt(previous.w)));
        return new Vector4(fresh.x, fresh.y, widthHighWater, heightHighWater);
    }

    private static T Pick<T>(System.Random random, params T[] options)
    {
        return options[random.Next(options.Length)];
    }
}
