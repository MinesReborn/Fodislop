#nullable enable

using System;
using System.Collections.Generic;
using Kern.World.Lighting;
using NUnit.Framework;

namespace Kern.Tests.World.Lighting;

[TestFixture]
public class CascadeLayoutBuilderFuzzTests
{
    private const string OverflowMessage = "Radiance cascade atlas exceeds the supported buffer size.";

    private static readonly int[] _Seeds = [1, 7, 42, 1337, 90210, 2147483, 8675309];

    private static readonly int[] _HostileDimensions = [0, 1, 2, 255, 256, 257, 1000, 1_000_000];

    private static readonly long[] _HostileAtlases = [0L, 1L, 64L, 256L, 257L, 1024L, 4096L];

    // Не record struct: primary constructor record-типа требует
    // System.Runtime.CompilerServices.IsExternalInit (для init-сеттеров),
    // который в этой тестовой asmdef не резолвится для вложенных типов.
    // Обычный readonly struct с явным конструктором от зависимости свободен.
    private readonly struct WorldSpec
    {
        public readonly int Width;
        public readonly int Height;
        public readonly long Atlas;

        public WorldSpec(int width, int height, long atlas)
        {
            Width = width;
            Height = height;
            Atlas = atlas;
        }
    }

    [Test]
    public void RandomWorldsKeepOffsetsCumulativeAndNonOverlapping([ValueSource(nameof(_Seeds))] int seed)
    {
        var random = new System.Random(seed);

        for (int iteration = 0; iteration < 3000; iteration++)
        {
            WorldSpec world = RandomWorld(random);
            var cascades = new List<CascadeLayout>();
            if (!TryBuild(world, cascades))
            {
                continue;
            }

            Assert.That(cascades, Is.Not.Empty, Describe(world, 0, seed));
            int runningOffset = 0;
            for (int i = 0; i < cascades.Count; i++)
            {
                CascadeLayout cascade = cascades[i];
                Assert.That(
                    cascade.Offset,
                    Is.EqualTo(runningOffset),
                    $"{Describe(world, i, seed)}: offset must be cumulative, not overlapping.");
                Assert.That(cascade.EntryCount, Is.GreaterThanOrEqualTo(0), Describe(world, i, seed));
                Assert.That(cascade.ProbeWidth, Is.GreaterThanOrEqualTo(0), Describe(world, i, seed));
                Assert.That(cascade.ProbeHeight, Is.GreaterThanOrEqualTo(0), Describe(world, i, seed));
                runningOffset += cascade.EntryCount;
            }
        }
    }

    [Test]
    public void TheIndependentEntryCounterAgreesForEveryRandomWorld([ValueSource(nameof(_Seeds))] int seed)
    {
        var random = new System.Random(seed);

        for (int iteration = 0; iteration < 3000; iteration++)
        {
            WorldSpec world = RandomWorld(random);
            var cascades = new List<CascadeLayout>();
            if (!TryBuild(world, cascades))
            {
                continue;
            }

            long lastOffset = cascades[^1].Offset;
            long lastEntry = cascades[^1].EntryCount;
            int budget = CascadeLayoutBuilder.GetMaximumCascadeCount(world.Atlas);

            // The counter walks the same loop with the same budget; a world
            // where the two disagree means the builder's accumulation and
            // its advertised total are not the same layout.
            Assert.That(
                lastOffset + lastEntry,
                Is.EqualTo(CascadeLayoutBuilder.CalculateCascadeEntryCount(world.Width, world.Height, budget)),
                $"{Describe(world, cascades.Count - 1, seed)}: builder total must match independent counter.");
        }
    }

    [Test]
    public void RealisticRandomWorldsNeverThrow([ValueSource(nameof(_Seeds))] int seed)
    {
        var random = new System.Random(seed);

        for (int iteration = 0; iteration < 3000; iteration++)
        {
            // Bounded below the overflow threshold on purpose: this test is
            // about worlds the system is expected to handle, not the
            // documented escape hatch (covered by HugeWorldsFail...).
            int width = random.Next(1, 4097);
            int height = random.Next(1, 4097);
            long atlas = random.Next(64, 8193);

            var cascades = new List<CascadeLayout>();
            Assert.DoesNotThrow(
                () => CascadeLayoutBuilder.BuildCascadeLayouts(width, height, atlas, cascades),
                $"seed {seed}, iteration {iteration}: {width}x{height} @ {atlas}");

            Assert.That(cascades, Is.Not.Empty, $"seed {seed}, iteration {iteration}");
            Assert.That(
                cascades.Count,
                Is.LessThanOrEqualTo(CascadeLayoutBuilder.GetMaximumCascadeCount(atlas)),
                $"seed {seed}, iteration {iteration}");
            foreach (CascadeLayout cascade in cascades)
            {
                Assert.That(cascade.EntryCount, Is.GreaterThan(0), $"seed {seed}, iteration {iteration}");
            }
        }
    }

    [Test]
    public void HugeWorldsFailWithOnlyTheDocumentedMessage([ValueSource(nameof(_Seeds))] int seed)
    {
        var random = new System.Random(seed);

        for (int iteration = 0; iteration < 500; iteration++)
        {
            int width = Pick(random, 1_000_000, 1_000_000, 2_000_000, 100_000, 10_000);
            int height = random.Next(1, 2_000_000);
            long atlas = Pick(random, _HostileAtlases);

            var cascades = new List<CascadeLayout>();
            try
            {
                CascadeLayoutBuilder.BuildCascadeLayouts(width, height, atlas, cascades);
            }
            catch (InvalidOperationException exception)
            {
                // The only failure this API is allowed to raise for an
                // oversized atlas. Any other exception type escapes and
                // fails the test, and a *different* message would mean the
                // caller's guard no longer recognizes the failure.
                Assert.That(
                    exception.Message,
                    Is.EqualTo(OverflowMessage),
                    $"seed {seed}, iteration {iteration}: {width}x{height} @ {atlas}");
                continue;
            }

            // Some worlds in the pool (e.g. 10_000 wide) still fit; that
            // is fine - what must never happen is a wrong offset or a
            // non-documented exception.
        }
    }

    [Test]
    public void IntervalsStayContiguousAndDirectionsStayCapped([ValueSource(nameof(_Seeds))] int seed)
    {
        var random = new System.Random(seed);

        for (int iteration = 0; iteration < 3000; iteration++)
        {
            WorldSpec world = RandomWorld(random);
            var cascades = new List<CascadeLayout>();
            if (!TryBuild(world, cascades))
            {
                continue;
            }

            for (int i = 0; i < cascades.Count; i++)
            {
                Assert.That(
                    cascades[i].DirectionCount,
                    Is.GreaterThan(0),
                    Describe(world, i, seed));
                Assert.That(
                    cascades[i].DirectionCount,
                    Is.LessThanOrEqualTo(64),
                    Describe(world, i, seed));

                if (i == 0)
                {
                    continue;
                }

                Assert.That(
                    cascades[i].IntervalStart,
                    Is.EqualTo(cascades[i - 1].IntervalEnd),
                    $"{Describe(world, i, seed)}: interval must start where the previous one ended.");
                Assert.That(
                    cascades[i].ProbeSpacing,
                    Is.EqualTo(cascades[i - 1].ProbeSpacing * 2),
                    Describe(world, i, seed));
            }
        }
    }

    private static WorldSpec RandomWorld(System.Random random)
    {
        // A quarter of the draws come from the hostile pool, so degenerate
        // and near-overflow cases are hit constantly rather than by luck.
        if (random.Next(4) == 0)
        {
            return new WorldSpec(
                Pick(random, _HostileDimensions),
                Pick(random, _HostileDimensions),
                Pick(random, _HostileAtlases));
        }

        return new WorldSpec(
            random.Next(1, 4097),
            random.Next(1, 4097),
            random.Next(64, 8193));
    }

    private static bool TryBuild(WorldSpec world, List<CascadeLayout> cascades)
    {
        try
        {
            CascadeLayoutBuilder.BuildCascadeLayouts(world.Width, world.Height, world.Atlas, cascades);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static T Pick<T>(System.Random random, params T[] options)
    {
        return options[random.Next(options.Length)];
    }

    private static string Describe(WorldSpec world, int index, int seed)
    {
        return $"seed {seed}: world {world.Width}x{world.Height} @ atlas {world.Atlas}, cascade {index}.";
    }
}
