#nullable enable

using System;
using Kern.World;
using NUnit.Framework;

namespace Kern.Tests.World;

[TestFixture]
[Category("FuzzPure")]
public class CoordinateUtilsFuzzTests
{
    [TestCase(1, 0)]
    [TestCase(32, 31)]
    [TestCase(1024, 1023)]
    [TestCase(8192, 4096)]
    public void ServerToUnityToServerY_RoundTripsForEveryValidCell(int worldHeight, int serverY)
    {
        float unityY = CoordinateUtils.ServerToUnityY(serverY, worldHeight);
        int back = CoordinateUtils.UnityToServerY(unityY, worldHeight);
        Assert.That(back, Is.EqualTo(serverY), $"h={worldHeight}, y={serverY}");
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(32)]
    [TestCase(1024)]
    public void ServerToUnityY_AlwaysReturnsHalfUnitOffset(int worldHeight)
    {
        for (int y = 0; y < Math.Min(worldHeight, 16); y++)
        {
            float u = CoordinateUtils.ServerToUnityY(y, worldHeight);
            float frac = u - (float)Math.Floor(u);
            Assert.That(frac, Is.EqualTo(0.5f).Within(0.0001f), $"h={worldHeight}, y={y}");
        }
    }

    [TestCase(0, 0)]
    [TestCase(-1, 0)]
    [TestCase(0, -1)]
    [TestCase(int.MinValue, 0)]
    [TestCase(0, int.MaxValue)]
    public void ZeroOrNegativeHeight_Throws(int worldHeight, int y)
    {
        Assert.Throws<InvalidOperationException>(() => CoordinateUtils.ServerToUnityY(y, worldHeight));
    }

    [TestCase(10, -1f, 10)]
    [TestCase(10, 10f, 10)]
    [TestCase(10, 100f, 10)]
    [TestCase(10, -100f, 10)]
    public void UnityToServerY_OutsideWorld_Throws(int worldHeight, float unityY, int _unused)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CoordinateUtils.UnityToServerY(unityY, worldHeight));
    }

    [TestCase(10, 0, 9)]
    [TestCase(100, 0, 99)]
    [TestCase(8192, 1, 8190)]
    public void ServerToUnityY_TopRow_ReturnsZero(int worldHeight, int serverY, int expectedFloor)
    {
        float u = CoordinateUtils.ServerToUnityY(serverY, worldHeight);
        Assert.That((float)Math.Floor(u), Is.EqualTo((float)expectedFloor), $"h={worldHeight}, y={serverY}");
    }

    [Test]
    public void ServerToUnityPos_CenteredVectorWithZ()
    {
        var p = CoordinateUtils.ServerToUnityPos(3, 5, 10, 7f);
        Assert.That(p.z, Is.EqualTo(7f));
        Assert.That(p.x, Is.EqualTo(3.5f));
        Assert.That(p.y, Is.EqualTo(4.5f));
    }
}
