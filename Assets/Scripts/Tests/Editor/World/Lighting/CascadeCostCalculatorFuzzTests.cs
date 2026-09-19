#nullable enable

using System;
using System.Collections.Generic;
using Kern.World.Lighting;
using NUnit.Framework;

namespace Kern.Tests.World.Lighting;

[TestFixture]
public class CascadeCostCalculatorFuzzTests
{
    private static readonly int[] _Seeds = [1, 7, 42, 1337, 90210, 2147483, 8675309];

    private static readonly int[] _HostileEntryCounts = [0, -1, 1, 7, 1000];
    private static readonly int[] _HostileDirectionCounts = [0, 1, 4, 16, 64, 256, 512];
    private static readonly float[] _HostileIntervalStarts = [0f, -1f, 1f, 4f, 100f];
    private static readonly float[] _HostileIntervalEnds = [0f, -1f, 1f, 4f, 16f, 1000f];

    [Test]
    public void SamplesMatchTheReferenceForEveryRandomWorld([ValueSource(nameof(_Seeds))] int seed)
    {
        var random = new System.Random(seed);

        for (int iteration = 0; iteration < 2000; iteration++)
        {
            var cascades = new List<CascadeLayout>();
            CascadeLayoutBuilder.BuildCascadeLayouts(
                random.Next(1, 4097),
                random.Next(1, 4097),
                random.Next(64, 8193),
                cascades);
            int maximumSteps = random.Next(1, 257);

            // Pre-seed garbage: the collector must wipe it, every time.
            var destination = new List<CascadeCostSample> { default, default };
            CascadeCostCalculator.CollectCascadeCosts(cascades, maximumSteps, destination);

            Assert.That(destination.Count, Is.EqualTo(cascades.Count), $"seed {seed}, iteration {iteration}");
            for (int i = 0; i < cascades.Count; i++)
            {
                AssertSampleMatchesReference(destination[i], cascades, i, maximumSteps, seed, iteration);
            }
        }
    }

    [Test]
    public void HostileStepBudgetsNeverThrowAndStillMatchTheReference([ValueSource(nameof(_Seeds))] int seed)
    {
        var random = new System.Random(seed);
        int[] budgets = [0, -1, int.MinValue, 1, 63, 64, 256, int.MaxValue];

        for (int iteration = 0; iteration < 1000; iteration++)
        {
            var cascades = new List<CascadeLayout>();
            CascadeLayoutBuilder.BuildCascadeLayouts(
                random.Next(1, 4097),
                random.Next(1, 4097),
                random.Next(64, 8193),
                cascades);

            int maximumSteps = Pick(random, budgets);
            var destination = new List<CascadeCostSample>();
            Assert.DoesNotThrow(
                () => CascadeCostCalculator.CollectCascadeCosts(cascades, maximumSteps, destination),
                $"seed {seed}, iteration {iteration}, maximumSteps {maximumSteps}");
            Assert.That(destination.Count, Is.EqualTo(cascades.Count), $"seed {seed}, iteration {iteration}");
            for (int i = 0; i < cascades.Count; i++)
            {
                AssertSampleMatchesReference(destination[i], cascades, i, maximumSteps, seed, iteration);
            }
        }
    }

    [Test]
    public void HostileLayoutsMatchTheReferenceWithoutThrowing([ValueSource(nameof(_Seeds))] int seed)
    {
        var random = new System.Random(seed);

        for (int iteration = 0; iteration < 1000; iteration++)
        {
            int cascadeCount = random.Next(1, 8);
            var cascades = new List<CascadeLayout>(cascadeCount);
            int offset = 0;
            for (int i = 0; i < cascadeCount; i++)
            {
                int entryCount = Pick(random, _HostileEntryCounts);
                cascades.Add(new CascadeLayout(
                    offset,
                    entryCount,
                    random.Next(0, 64),
                    random.Next(0, 64),
                    random.Next(1, 9),
                    Pick(random, _HostileDirectionCounts),
                    Pick(random, _HostileIntervalStarts),
                    Pick(random, _HostileIntervalEnds)));
                offset += entryCount;
            }

            // Budgets go negative on purpose: the documented clamp rule
            // must still produce a defined answer, not an exception.
            int maximumSteps = random.Next(-5, 65);
            var destination = new List<CascadeCostSample>();
            Assert.DoesNotThrow(
                () => CascadeCostCalculator.CollectCascadeCosts(cascades, maximumSteps, destination),
                $"seed {seed}, iteration {iteration}");
            Assert.That(destination.Count, Is.EqualTo(cascades.Count), $"seed {seed}, iteration {iteration}");
            for (int i = 0; i < cascades.Count; i++)
            {
                AssertSampleMatchesReference(destination[i], cascades, i, maximumSteps, seed, iteration);
            }
        }
    }

    [Test]
    public void AggregateCostsStayNonNegativeForRealisticWorlds([ValueSource(nameof(_Seeds))] int seed)
    {
        var random = new System.Random(seed);

        for (int iteration = 0; iteration < 2000; iteration++)
        {
            var cascades = new List<CascadeLayout>();
            CascadeLayoutBuilder.BuildCascadeLayouts(
                random.Next(1, 4097),
                random.Next(1, 4097),
                random.Next(64, 8193),
                cascades);

            var destination = new List<CascadeCostSample>();
            CascadeCostCalculator.CollectCascadeCosts(cascades, random.Next(1, 257), destination);

            long rays = 0;
            long raySteps = 0;
            long mergeTaps = 0;
            foreach (CascadeCostSample sample in destination)
            {
                Assert.That(sample.RayCount, Is.GreaterThan(0), $"seed {seed}, iteration {iteration}");
                Assert.That(sample.RayStepCount, Is.GreaterThan(0), $"seed {seed}, iteration {iteration}");
                rays += sample.RayCount;
                raySteps += sample.RayStepCount;
                mergeTaps += sample.MergeTapCount;
            }

            Assert.That(rays, Is.GreaterThan(0), $"seed {seed}, iteration {iteration}");
            Assert.That(
                raySteps,
                Is.GreaterThanOrEqualTo(rays),
                $"seed {seed}, iteration {iteration}: every ray marches at least one step.");
            Assert.That(mergeTaps, Is.GreaterThanOrEqualTo(0), $"seed {seed}, iteration {iteration}");
        }
    }

    private static void AssertSampleMatchesReference(
        CascadeCostSample sample,
        IReadOnlyList<CascadeLayout> cascades,
        int index,
        int maximumSteps,
        int seed,
        int iteration)
    {
        CascadeLayout cascade = cascades[index];
        string context = $"seed {seed}, iteration {iteration}, cascade {index}";

        float traceLength = Math.Max(cascade.IntervalEnd - cascade.IntervalStart, 0f);
        int traceCount = 1;
        long mergeTaps = 0;
        if (index + 1 < cascades.Count)
        {
            CascadeLayout far = cascades[index + 1];
            traceCount = Clamp(far.DirectionCount / Math.Max(1, cascade.DirectionCount), 1, 4) * 4;
            mergeTaps = (long)cascade.EntryCount * traceCount;
            traceLength = Math.Abs(cascade.IntervalStart) + Math.Abs(far.IntervalStart) +
                (float)Math.Sqrt(2f) * far.ProbeSpacing;
        }

        int stepCount = ((int)Math.Ceiling((float)Math.Sqrt(2f) * traceLength) + 2) * traceCount;

        Assert.That(sample.Index, Is.EqualTo(index), context);
        Assert.That(sample.ProbeWidth, Is.EqualTo(cascade.ProbeWidth), context);
        Assert.That(sample.ProbeHeight, Is.EqualTo(cascade.ProbeHeight), context);
        Assert.That(sample.DirectionCount, Is.EqualTo(cascade.DirectionCount), context);
        Assert.That(sample.IntervalStart, Is.EqualTo(cascade.IntervalStart), context);
        Assert.That(sample.IntervalEnd, Is.EqualTo(cascade.IntervalEnd), context);
        Assert.That(sample.StepCount, Is.EqualTo(stepCount), context);
        Assert.That(sample.RayCount, Is.EqualTo(cascade.EntryCount), context);
        Assert.That(sample.RayStepCount, Is.EqualTo((long)cascade.EntryCount * stepCount), context);
        Assert.That(sample.MergeTapCount, Is.EqualTo(mergeTaps), context);
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }

    private static T Pick<T>(System.Random random, params T[] options)
    {
        return options[random.Next(options.Length)];
    }
}
