#nullable enable

using Kern.Core.Interfaces;
using Kern.World.Streaming;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World.Streaming;

public sealed class WorldViewTransitionTests
{
    [Test]
    public void WorldEntryIsNotStableUntilItsReadinessGatePublishes()
    {
        var transition = new WorldViewTransition();
        ulong id = transition.BeginWorldEntry();

        Assert.That(transition.IsStable, Is.False);
        Assert.That(transition.HasPublishedView, Is.False);
        Assert.That(transition.PublishWorldEntry(id), Is.True);
        Assert.That(transition.IsStable, Is.True);
        Assert.That(transition.HasPublishedView, Is.True);
    }

    [Test]
    public void TeleportCommitsOnlyWhenTheReadyRegionCoversTheWholeViewport()
    {
        var transition = CreateStableTransition();
        var destination = new Vector3(500.5f, 300.5f, -10f);
        Assert.That(transition.TryBeginTeleport(destination, out ulong id), Is.True);

        Assert.That(transition.IsTeleportPreparing, Is.True);
        Assert.That(transition.IsTeleportReadyFor(id, destination, 20f, 12f, 1f), Is.False);

        Assert.That(transition.MarkTeleportReady(id, new RectInt(470, 280, 50, 40)), Is.True);
        Assert.That(transition.IsTeleportReadyFor(id, destination, 20f, 12f, 1f), Is.False);
        Assert.That(transition.CommitTeleport(id, destination, 20f, 12f, 1f), Is.False);

        Assert.That(transition.MarkTeleportReady(id, new RectInt(470, 280, 60, 40)), Is.True);
        Assert.That(transition.IsTeleportReadyFor(id, destination, 20f, 12f, 1f), Is.True);
        Assert.That(transition.CommitTeleport(id, destination, 20f, 12f, 1f), Is.True);
        Assert.That(transition.IsStable, Is.True);
    }

    [Test]
    public void SupersedingTeleportRejectsReadinessAndCommitFromOlderTransition()
    {
        var transition = CreateStableTransition();
        Assert.That(transition.TryBeginTeleport(new Vector3(500f, 300f), out ulong staleId), Is.True);
        Assert.That(transition.TryBeginTeleport(new Vector3(900f, 700f), out ulong activeId), Is.True);

        Assert.That(transition.MarkTeleportReady(staleId, new RectInt(0, 0, 2000, 2000)), Is.False);
        Assert.That(transition.CommitTeleport(staleId, Vector3.zero, 0f, 0f, 1f), Is.False);
        Assert.That(transition.MarkTeleportReady(activeId, new RectInt(0, 0, 2000, 2000)), Is.True);
        Assert.That(transition.IsTeleportReadyFor(activeId, new Vector3(900f, 700f), 10f, 10f, 1f), Is.True);
        Assert.That(transition.CommitTeleport(activeId, new Vector3(900f, 700f), 10f, 10f, 1f), Is.True);
    }

    [Test]
    public void DestinationChangeInvalidatesPreviouslyReportedReadiness()
    {
        var transition = CreateStableTransition();
        Assert.That(transition.TryBeginTeleport(new Vector3(10f, 10f), out ulong id), Is.True);
        Assert.That(transition.MarkTeleportReady(id, new RectInt(-100, -100, 300, 300)), Is.True);

        Assert.That(transition.UpdateTeleportDestination(id, new Vector3(500f, 500f)), Is.True);
        Assert.That(transition.Current.Phase, Is.EqualTo(WorldPresentationPhase.Preparing));
        Assert.That(transition.IsTeleportReadyFor(id, new Vector3(500f, 500f), 5f, 5f, 1f), Is.False);
    }

    private static WorldViewTransition CreateStableTransition()
    {
        var transition = new WorldViewTransition();
        ulong entryId = transition.BeginWorldEntry();
        Assert.That(transition.PublishWorldEntry(entryId), Is.True);
        return transition;
    }
}
