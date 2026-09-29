#nullable enable

using Kern.Game;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Player;

[TestFixture]
public class RobotMovementTests
{
    [Test]
    public void SnapTo_SetsPositionsImmediatelyAndClearsVelocity()
    {
        var movement = new RobotMovement();
        Assert.That(movement.HasReceivedInitialPosition, Is.False);

        Vector3 snapPos = new(100.5f, 200.5f, 0f);
        movement.SnapTo(snapPos, 90f);

        Assert.That(movement.HasReceivedInitialPosition, Is.True);
        Assert.That(movement.TargetPosition, Is.EqualTo(snapPos));
        Assert.That(movement.ServerPosition, Is.EqualTo(snapPos));
        Assert.That(movement.SmoothPosition, Is.EqualTo(snapPos));
        Assert.That(movement.TargetAngle, Is.EqualTo(90f));
    }
}
