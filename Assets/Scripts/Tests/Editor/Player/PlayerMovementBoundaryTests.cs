#nullable enable

using Kern.Player.Logic;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Player;

[TestFixture]
public class PlayerMovementBoundaryTests
{
    [Test]
    [TestCase(0, 0, 128, 128, true)]
    [TestCase(127, 127, 128, 128, true)]
    [TestCase(-1, 0, 128, 128, false)]
    [TestCase(0, -1, 128, 128, false)]
    [TestCase(128, 0, 128, 128, false)]
    [TestCase(0, 128, 128, 128, false)]
    [TestCase(0, 0, 0, 128, false)]
    [TestCase(0, 0, 128, 0, false)]
    public void IsWithinWorldBounds_RejectsOutsideCoordinates(
        int x,
        int y,
        int width,
        int height,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            PlayerMovementController.IsWithinWorldBounds(new Vector2Int(x, y), width, height));
    }
}
