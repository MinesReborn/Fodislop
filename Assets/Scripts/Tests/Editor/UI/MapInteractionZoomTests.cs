#nullable enable

using Kern.UI;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.UI;

[TestFixture]
public sealed class MapInteractionZoomTests
{
    // Колесо вверх в UI Toolkit приходит с отрицательной delta.y и обязано
    // приближать: положительный шаг зума уменьшает cellsPerPixel. Конвенция
    // та же, что у камеры (CameraFollow: колесо вверх уменьшает orthographicSize).
    [TestCase(-1f, 1f)]
    [TestCase(-0.5f, 0.5f)]
    [TestCase(-100f, 4f)]
    public void WheelDeltaToZoomSteps_WheelUp_ZoomsIn(float wheelDelta, float expectedSteps)
    {
        float steps = MapInteractionController.WheelDeltaToZoomSteps(wheelDelta);

        Assert.That(steps, Is.EqualTo(expectedSteps).Within(0.0001f));

        // Семантика шага: положительные шаги приближают (меньше клеток на пиксель).
        float cellsPerPixel = 1f * Mathf.Pow(0.85f, steps);
        Assert.That(
            cellsPerPixel,
            Is.LessThan(1f),
            "Колесо вверх должно уменьшать cellsPerPixel (приближать).");
    }

    [TestCase(1f, -1f)]
    [TestCase(0.5f, -0.5f)]
    [TestCase(100f, -4f)]
    public void WheelDeltaToZoomSteps_WheelDown_ZoomsOut(float wheelDelta, float expectedSteps)
    {
        float steps = MapInteractionController.WheelDeltaToZoomSteps(wheelDelta);

        Assert.That(steps, Is.EqualTo(expectedSteps).Within(0.0001f));

        // Семантика шага: отрицательные шаги отдаляют (больше клеток на пиксель).
        float cellsPerPixel = 1f * Mathf.Pow(0.85f, steps);
        Assert.That(
            cellsPerPixel,
            Is.GreaterThan(1f),
            "Колесо вниз должно увеличивать cellsPerPixel (отдалять).");
    }

    [Test]
    public void WheelDeltaToZoomSteps_ZeroDelta_DoesNothing()
    {
        Assert.That(MapInteractionController.WheelDeltaToZoomSteps(0f), Is.EqualTo(0f));
    }
}
