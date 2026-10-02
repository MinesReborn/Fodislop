#nullable enable

using Kern.World.Streaming;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World.Streaming;

// Readiness describes whether an explicitly held publication covers a viewport.
// Camera teleport policy is tested separately in CameraFollowTests.
public sealed class WorldViewTransitionTests
{
    [Test]
    public void NotReadyUntilTerrainMarksAWindowCoveringTheWholeFrame()
    {
        var transition = new WorldViewTransition { CanHold = true };
        var destination = new Vector3(500.5f, 300.5f, -10f);
        transition.Hold(destination);

        Assert.That(transition.IsHolding, Is.True);
        Assert.That(transition.IsReadyFor(destination, 20f, 12f, 1f), Is.False);

        // Окно накрывает кадр не целиком: не хватает клетки справа.
        transition.MarkReady(new RectInt(470, 280, 50, 40));
        Assert.That(transition.IsReadyFor(destination, 20f, 12f, 1f), Is.False);

        transition.MarkReady(new RectInt(470, 280, 60, 40));
        Assert.That(transition.IsReadyFor(destination, 20f, 12f, 1f), Is.True);
    }

    [Test]
    public void ReadinessIsDroppedOnReleaseAndOnANewHold()
    {
        var transition = new WorldViewTransition { CanHold = true };
        var destination = new Vector3(10f, 10f, -10f);
        transition.Hold(destination);
        transition.MarkReady(new RectInt(-100, -100, 300, 300));
        transition.Release();

        Assert.That(transition.IsHolding, Is.False);
        transition.Hold(destination);
        Assert.That(transition.IsReadyFor(destination, 5f, 5f, 1f), Is.False, "готовность прежнего перехода");
    }
}
