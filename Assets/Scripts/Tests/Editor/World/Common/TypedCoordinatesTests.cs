#if UNITY_EDITOR
#nullable enable

using Kern.World.Coordinates;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

[TestFixture]
public sealed class TypedCoordinatesTests
{
    private const int WorldHeight = 256;
    private const float CellSize = 1.0f;

    [Test]
    public void WorldCoord_ToCell_FloorCorrectly()
    {
        var world = new WorldCoord(10.7f, 25.2f);
        var cell = world.ToCell(CellSize);

        Assert.AreEqual(10, cell.X);
        Assert.AreEqual(25, cell.Y);
    }

    [Test]
    public void CellCoord_ToWorldCenter_CentersInCell()
    {
        var cell = new CellCoord(5, 12);
        var center = cell.ToWorldCenter(CellSize);

        Assert.AreEqual(5.5f, center.X, 0.001f);
        Assert.AreEqual(12.5f, center.Y, 0.001f);
    }

    [Test]
    public void CellCoord_ToServer_InvertsYWithWorldHeight()
    {
        var cell = new CellCoord(10, 0);
        var server = cell.ToServer(WorldHeight);

        Assert.AreEqual(10, server.X);
        Assert.AreEqual(255, server.Y);
    }

    [Test]
    public void Roundtrip_CellToServerToUnity_PreservesCoordinates()
    {
        var original = new CellCoord(42, 100);
        var server = original.ToServer(WorldHeight);
        var roundtrip = server.ToUnityCell(WorldHeight);

        Assert.AreEqual(original.X, roundtrip.X);
        Assert.AreEqual(original.Y, roundtrip.Y);
    }

    [Test]
    public void ImplicitConversions_PreserveValues()
    {
        Vector2 v = new Vector2(3.14f, 2.71f);
        WorldCoord wc = v;
        Vector2 v2 = wc;

        Assert.AreEqual(v.x, v2.x);
        Assert.AreEqual(v.y, v2.y);
    }
}
#endif
