using System.Diagnostics;
using Kern.Rendering.PostProcessing;
using NUnit.Framework;

namespace Kern.FrameHarness.Tests;

public sealed class PostProcessWorkloadTests
{
    [Test]
    public void LogicalPixelsAndGpuThreadsAreDifferentForOddSizesAndFusedKernel()
    {
        var work = new PostProcessWorkloadAccumulator();
        work.RecordDispatch(17, 9, 8, 8);
        work.RecordDispatch(17, 9, 16, 16);
        var snapshot = work.Complete(42, 17, 9, 0, 1, 17 * 9 * 8, Stopwatch.GetTimestamp());
        Assert.That(snapshot.DispatchCount, Is.EqualTo(2));
        Assert.That(snapshot.LogicalPixels, Is.EqualTo(306));
        Assert.That(snapshot.DispatchedThreads, Is.EqualTo(384 + 128));
        Assert.That(snapshot.TexturePayloadBytes, Is.EqualTo(1224));
        Assert.That(snapshot.CpuRecordingMs, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void NewFrameDoesNotReplayPreviousCounts()
    {
        var owner = new PostProcessWorkload();
        Assert.That(owner.Latest, Is.Null);
        var frame = new PostProcessWorkloadAccumulator();
        frame.RecordDispatch(8, 8, 8, 8);
        owner.Publish(frame.Complete(1, 8, 8, 0, 1, 512, Stopwatch.GetTimestamp()));
        var nextFrame = new PostProcessWorkloadAccumulator();
        owner.Publish(nextFrame.Complete(2, 8, 8, 0, 0, 0, Stopwatch.GetTimestamp()));
        Assert.That(owner.Latest!.Value.FrameId, Is.EqualTo(2));
        Assert.That(owner.Latest.Value.DispatchCount, Is.Zero);
    }
}
