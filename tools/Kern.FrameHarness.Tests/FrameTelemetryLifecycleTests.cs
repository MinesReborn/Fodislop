using Kern.Core;
using NUnit.Framework;
using UnityEngine;

namespace Kern.FrameHarness.Tests;

[TestFixture]
public sealed class FrameTelemetryLifecycleTests
{
    [Test]
    public void ResetStampsCurrentProducerFrameAndDisposeInvalidatesLifecycle()
    {
        using var telemetry = new FrameTelemetry();
        Assert.That(telemetry.ProducerLifecycleValid, Is.False);

        Time.frameCount = 417;
        telemetry.ResetFrameTimers();

        Assert.That(telemetry.ProducerFrameId, Is.EqualTo(417));
        Assert.That(telemetry.ProducerLifecycleValid, Is.True);
        telemetry.Dispose();
        Assert.That(telemetry.ProducerLifecycleValid, Is.False);
    }

    [Test]
    public void SkippedResetKeepsOldStampUntilNextReset()
    {
        using var telemetry = new FrameTelemetry();
        Assert.That(telemetry.ProducerFrameId, Is.EqualTo(-1));
        Time.frameCount = 10;
        telemetry.ResetFrameTimers();
        telemetry.LightingStaticSolveFrameCount = 7;

        Time.frameCount = 11;
        Assert.That(telemetry.ProducerFrameId, Is.EqualTo(10));
        Assert.That(telemetry.LightingStaticSolveFrameCount, Is.EqualTo(7));

        telemetry.ResetFrameTimers();
        Assert.That(telemetry.ProducerFrameId, Is.EqualTo(11));
        Assert.That(telemetry.ProducerLifecycleValid, Is.True);
        Assert.That(telemetry.LightingStaticSolveFrameCount, Is.Zero);
    }

    [Test]
    public void ResetAfterDisposeCannotReviveProducer()
    {
        using var telemetry = new FrameTelemetry();
        Time.frameCount = 20;
        telemetry.ResetFrameTimers();
        telemetry.Dispose();
        telemetry.Dispose();
        Time.frameCount = 21;

        Assert.Throws<ObjectDisposedException>(() => telemetry.ResetFrameTimers());
        Assert.That(telemetry.ProducerLifecycleValid, Is.False);
        Assert.That(telemetry.ProducerFrameId, Is.EqualTo(20));
    }
}
