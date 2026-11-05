#nullable enable

using System;
using Kern.Rendering;
using NUnit.Framework;

using Kern.Rendering.Display;

namespace Kern.DisplayTests;

public sealed class HDROutputControllerTests
{
    [Test]
    public void MatchingInitialModeSettlesWithoutRequest()
    {
        var backend = new FakeBackend(CreateSnapshot(active: false));
        var controller = new HDROutputController(backend);

        controller.SetPreference(enabled: false);
        controller.Update(now: 0);

        Assert.That(controller.Status, Is.EqualTo(HDROutputController.Phase.SDR));
        Assert.That(controller.Attempts, Is.Zero);
        Assert.That(backend.RequestCount, Is.Zero);
    }

    [Test]
    public void RequestedModeIsRetriedOnlyAfterTheBackoff()
    {
        var backend = new FakeBackend(CreateSnapshot(active: false));
        var controller = new HDROutputController(backend);

        controller.SetPreference(enabled: true);
        controller.Update(now: 0);
        controller.Update(now: 1);
        controller.Update(now: 2);

        Assert.That(controller.Attempts, Is.EqualTo(2));
        Assert.That(backend.RequestCount, Is.EqualTo(2));
        Assert.That(backend.LastRequestedMode, Is.True);
    }

    [Test]
    public void ReadFailureIsTerminalUntilExplicitRetry()
    {
        var backend = new FakeBackend(CreateSnapshot(active: false))
        {
            ReadException = new InvalidOperationException("display query failed"),
        };
        var controller = new HDROutputController(backend);

        controller.SetPreference(enabled: false);
        controller.Update(now: 0);
        controller.Update(now: 1);

        Assert.That(controller.Status, Is.EqualTo(HDROutputController.Phase.Failed));
        Assert.That(controller.HasReadFailure, Is.True);
        Assert.That(backend.ReadCount, Is.EqualTo(1));
    }

    private static HDROutputController.Snapshot CreateSnapshot(bool active)
    {
        return new HDROutputController.Snapshot(
            new HDROutputController.OutputIdentity("test", 0, 0, 1920, 1080, 0),
            Supported: true,
            PipelineSupported: true,
            Available: true,
            Active: active,
            Pending: false,
            Switchable: true);
    }

    private sealed class FakeBackend(HDROutputController.Snapshot snapshot) : HDROutputController.IBackend
    {
        public int ReadCount { get; private set; }
        public int RequestCount { get; private set; }
        public bool LastRequestedMode { get; private set; }
        public Exception? ReadException { get; set; }

        public HDROutputController.Snapshot Read()
        {
            ReadCount++;
            if (ReadException is not null)
            {
                throw ReadException;
            }

            return snapshot;
        }

        public void Request(bool enabled)
        {
            RequestCount++;
            LastRequestedMode = enabled;
        }
    }
}
