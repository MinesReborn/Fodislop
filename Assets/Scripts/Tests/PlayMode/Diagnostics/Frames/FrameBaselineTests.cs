#nullable enable

using Kern.Diagnostics.Frames;
using NUnit.Framework;

namespace Kern.Tests.PlayMode;

[TestFixture]
public sealed class FrameBaselineTests
{
    [Test]
    public void Baseline_IsNotReadyUntilThirtyValidFrames()
    {
        var baseline = new FrameBaseline();

        for (int frame = 1; frame < 30; frame++)
        {
            baseline.Push(frame);
            Assert.That(baseline.IsReady, Is.False);
        }

        baseline.Push(30);

        Assert.That(baseline.IsReady, Is.True);
        Assert.That(baseline.Milliseconds, Is.EqualTo(16d));
    }

    [Test]
    public void Baseline_IgnoresNonPositiveAndNonFiniteSamples()
    {
        var baseline = new FrameBaseline();
        baseline.Push(10d);
        baseline.Push(0d);
        baseline.Push(-1d);
        baseline.Push(double.NaN);
        baseline.Push(double.PositiveInfinity);

        Assert.That(baseline.IsReady, Is.False);
        Assert.That(baseline.Milliseconds, Is.EqualTo(10d));
    }

    [Test]
    public void Clear_RequiresFreshWarmup()
    {
        var baseline = new FrameBaseline();
        for (int frame = 0; frame < 30; frame++)
        {
            baseline.Push(16d);
        }

        baseline.Clear();

        Assert.That(baseline.IsReady, Is.False);
        Assert.That(baseline.Milliseconds, Is.Zero);
    }
}
