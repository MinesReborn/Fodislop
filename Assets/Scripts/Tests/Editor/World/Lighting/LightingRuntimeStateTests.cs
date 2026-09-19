#nullable enable

using Kern.World.Lighting;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World.Lighting;

[TestFixture]
public sealed class LightingRuntimeStateTests
{
    [Test]
    public void OffscreenInvalidationWaitsUntilTheViewportReachesIt()
    {
        var state = new LightingRuntimeState
        {
            FieldDirty = false,
        };
        state.QueueRegionInvalidation(new RectInt(200, 0, 8, 8));

        Assert.That(
            state.ActivatePendingRegionIfVisible(new RectInt(0, 0, 32, 32)),
            Is.False);
        Assert.That(state.FieldDirty, Is.False);
    }

    [Test]
    public void VisibleInvalidationActivatesExactlyWhenItIntersectsTheViewport()
    {
        var state = new LightingRuntimeState
        {
            FieldDirty = false,
        };
        state.QueueRegionInvalidation(new RectInt(24, 24, 8, 8));

        Assert.That(
            state.ActivatePendingRegionIfVisible(new RectInt(0, 0, 32, 32)),
            Is.True);
        Assert.That(state.FieldDirty, Is.True);
        Assert.That(
            state.ActivatePendingRegionIfVisible(new RectInt(0, 0, 32, 32)),
            Is.False);
    }

    [Test]
    public void ActiveInvalidationsKeepTheirIndividualRegions()
    {
        var state = new LightingRuntimeState
        {
            FieldDirty = false,
        };
        RectInt left = new(4, 4, 8, 8);
        RectInt right = new(40, 12, 6, 10);
        state.QueueRegionInvalidation(left);
        state.QueueRegionInvalidation(right);

        Assert.That(
            state.ActivatePendingRegionIfVisible(new RectInt(0, 0, 64, 32)),
            Is.True);
        Assert.That(state.ActiveRegionInvalidations, Has.Count.EqualTo(2));
        Assert.That(state.ActiveRegionInvalidations, Does.Contain(left));
        Assert.That(state.ActiveRegionInvalidations, Does.Contain(right));
    }

    [Test]
    public void DistantPendingRegionsDoNotFormAFalseBoundingIntersection()
    {
        var state = new LightingRuntimeState
        {
            FieldDirty = false,
        };
        state.QueueRegionInvalidation(new RectInt(-100, 0, 8, 8));
        state.QueueRegionInvalidation(new RectInt(100, 0, 8, 8));

        Assert.That(
            state.ActivatePendingRegionIfVisible(new RectInt(-4, 0, 8, 8)),
            Is.False);
        Assert.That(state.FieldDirty, Is.False);
    }

    [Test]
    public void BudgetedActivationPrioritizesNearestRegionAndDefersExcess()
    {
        var state = new LightingRuntimeState
        {
            FieldDirty = false,
        };
        // Viewport: center at (16, 16), size (32, 32)
        RectInt viewport = new(0, 0, 32, 32);
        RectInt nearCenter = new(14, 14, 4, 4); // area = 16
        RectInt farCorner = new(28, 28, 4, 4);  // area = 16

        state.QueueRegionInvalidation(farCorner);
        state.QueueRegionInvalidation(nearCenter);

        // Budget maxAreaCells = 16 allows only 1 region per frame
        bool activated = state.ActivatePendingRegionsBudgeted(viewport, maxAreaCells: 16);

        Assert.That(activated, Is.True);
        Assert.That(state.ActiveRegionInvalidations, Has.Count.EqualTo(1));
        Assert.That(state.ActiveRegionInvalidations, Does.Contain(nearCenter), "Nearest region must activate first");

        // Next frame activates the deferred far region
        state.CompleteActiveRegionInvalidation();
        bool nextActivated = state.ActivatePendingRegionsBudgeted(viewport, maxAreaCells: 16);

        Assert.That(nextActivated, Is.True);
        Assert.That(state.ActiveRegionInvalidations, Has.Count.EqualTo(1));
        Assert.That(state.ActiveRegionInvalidations, Does.Contain(farCorner), "Deferred region must activate in next slice");
    }
}
