#nullable enable

using Kern.World.Streaming;
using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World.Streaming;

// Опубликованное окно без поставленного света не должно запирать кадр:
// иначе свет и меш показа не ставятся никогда, и экран остаётся чёрным,
// пока запрос окна не станет резидентным.
public sealed class TerrainFramePlanSelectionTests
{
    [Test]
    public void CommittedWindowWithoutLightingYetIsProcessedWhileRequestIsNotResident()
    {
        var calculator = new TerrainViewportCalculator();
        var committed = new StreamingWindow(new Vector2Int(100, 200), new Vector2Int(192, 160));
        var requested = new StreamingWindow(new Vector2Int(100, 200), new Vector2Int(256, 192));
        var camera = new RectInt(150, 240, 60, 40);

        TerrainFramePlan plan = calculator.SelectFramePlan(
            requested,
            committed,
            camera,
            retainedLightingViewport: default,
            isRequestedResident: false,
            requestedDimensionsChanged: true,
            cellsCommitted: true,
            cpuBuildInFlight: false,
            holdingPublishedView: false);

        Assert.That(plan.ShouldProcess, Is.True);
        Assert.That(plan.ActiveWindow, Is.EqualTo(committed));
        Assert.That(plan.DimensionsChanged, Is.False);
        Assert.That(plan.LightingViewport, Is.EqualTo(camera));
    }

    [Test]
    public void CameraViewportOutsideCommittedWindowIsNotClampedIntoLightingDemand()
    {
        var calculator = new TerrainViewportCalculator();
        var committed = new StreamingWindow(new Vector2Int(0, 0), new Vector2Int(100, 80));
        var camera = new RectInt(90, -10, 40, 30);

        TerrainFramePlan plan = calculator.SelectFramePlan(
            committed,
            committed,
            camera,
            retainedLightingViewport: default,
            isRequestedResident: true,
            requestedDimensionsChanged: false,
            cellsCommitted: true,
            cpuBuildInFlight: true,
            holdingPublishedView: false);

        Assert.That(plan.ShouldProcess, Is.True);
        Assert.That(plan.LightingViewport, Is.EqualTo(camera));
    }

    [Test]
    public void NothingIsProcessedBeforeTheFirstPublication()
    {
        var calculator = new TerrainViewportCalculator();
        var window = new StreamingWindow(new Vector2Int(0, 0), new Vector2Int(100, 80));

        TerrainFramePlan plan = calculator.SelectFramePlan(
            window,
            window,
            new RectInt(10, 10, 20, 20),
            retainedLightingViewport: default,
            isRequestedResident: false,
            requestedDimensionsChanged: false,
            cellsCommitted: false,
            cpuBuildInFlight: false,
            holdingPublishedView: false);

        Assert.That(plan.ShouldProcess, Is.False);
    }
}
