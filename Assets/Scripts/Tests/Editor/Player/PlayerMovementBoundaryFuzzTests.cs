#nullable enable

using Kern.Player.Logic;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Player;

[TestFixture]
public class PlayerMovementBoundaryFuzzTests
{
    [Test]
    public void IsWithinWorldBounds_ZeroOrNegativeSize_False()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 100; i++)
        {
            int w = random.Next(-10, 1);
            int h = random.Next(-10, 1);
            Vector2Int p = new Vector2Int(random.Next(-50, 50), random.Next(-50, 50));
            Assert.IsFalse(PlayerMovementValidator.IsWithinWorldBounds(p, w, h), $"i={i}");
        }
    }

    [Test]
    public void IsWithinWorldBounds_CornersAndCenter_True()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 100; i++)
        {
            int w = random.Next(1, 4097);
            int h = random.Next(1, 4097);
            Assert.IsTrue(PlayerMovementValidator.IsWithinWorldBounds(new Vector2Int(0, 0), w, h));
            Assert.IsTrue(PlayerMovementValidator.IsWithinWorldBounds(new Vector2Int(w - 1, h - 1), w, h));
        }
    }

    [Test]
    public void IsWithinWorldBounds_JustOutside_False()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 100; i++)
        {
            int w = random.Next(1, 1024);
            int h = random.Next(1, 1024);
            Assert.IsFalse(PlayerMovementValidator.IsWithinWorldBounds(new Vector2Int(w, 0), w, h));
            Assert.IsFalse(PlayerMovementValidator.IsWithinWorldBounds(new Vector2Int(0, h), w, h));
            Assert.IsFalse(PlayerMovementValidator.IsWithinWorldBounds(new Vector2Int(-1, 0), w, h));
            Assert.IsFalse(PlayerMovementValidator.IsWithinWorldBounds(new Vector2Int(0, -1), w, h));
        }
    }

    [Test]
    public void MovementToDeltaServer_StandardDirections()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 100; i++)
        {
            Vector2Int[] dirs = [new(0, 0), new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];
            Vector2Int d = dirs[random.Next(dirs.Length)];
            Vector2Int delta = PlayerMovementMath.MovementToDeltaServer(d);
            Assert.That(delta.x, Is.EqualTo(d.x));
            if (d.y > 0) Assert.That(delta.y, Is.EqualTo(-1));
            else if (d.y < 0) Assert.That(delta.y, Is.EqualTo(1));
            else Assert.That(delta.y, Is.EqualTo(0));
        }
    }

    [Test]
    public void IsPassable_Empty_True()
    {
        bool p = PlayerMovementValidator.IsPassable(
            CellType.Empty,
            new CellConfigurationPacket(CellConfigProperties.None, CellDistortionType.Neutral, CellAnimationType.None, 0, 0, 0, 0));
        Assert.IsTrue(p);
    }

    [Test]
    public void IsPassable_NonEmptyWithPassableFlag_True()
    {
        var random = new System.Random(42);
        CellType[] types = [CellType.Road, CellType.Gate, CellType.BuildingDoor];
        for (int i = 0; i < 50; i++)
        {
            bool p = PlayerMovementValidator.IsPassable(
                types[random.Next(types.Length)],
                new CellConfigurationPacket(CellConfigProperties.Passable, CellDistortionType.Neutral, CellAnimationType.None, 0, 0, 0, 0));
            Assert.IsTrue(p, $"i={i}");
        }
    }

    [Test]
    public void IsPassable_Solid_False()
    {
        var random = new System.Random(42);
        CellType[] types = [CellType.Rock, CellType.BuildingWall, CellType.Lava, CellType.Box];
        for (int i = 0; i < 50; i++)
        {
            bool p = PlayerMovementValidator.IsPassable(
                types[random.Next(types.Length)],
                new CellConfigurationPacket(CellConfigProperties.None, CellDistortionType.Neutral, CellAnimationType.None, 0, 0, 0, 0));
            Assert.IsFalse(p, $"i={i}");
        }
    }
}
