using System;
using System.Linq;
using Kern.World.Terrain;
using MinesServer.Networking.Server.Packets.Connection;
using NUnit.Framework;
using UnityEngine;

namespace Kern.TerrainTests.Volume;

public sealed class VolumeTopologyTests
{
    [Test]
    public void InteriorMaterialChangeDoesNotRepublishUnchangedEdges()
    {
        var buffers = new TerrainCellBuffers();
        buffers.Type(1, 0);
        buffers.Type(3, 0);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                buffers.Set(x, y, 1);
            }
        }
        var topology = new TerrainVolumeTopology();
        topology.Publish(buffers, Vector2Int.zero, 16, 16, false);
        ulong version = topology.Version;
        buffers.Set(8, 8, 3);
        topology.Publish(buffers, Vector2Int.zero, 16, 16, false);
        Assert.That(topology.LastRebuiltCellCount, Is.EqualTo(9));
        Assert.That(topology.Version, Is.EqualTo(version));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LocalEditsMatchFreshTopologyIncludingWindowHalo(bool distortion)
    {
        var buffers = new TerrainCellBuffers();
        buffers.Type(1, 1u << TerrainCellFormat.TypeOutlineShift);
        buffers.Type(3, 3u << TerrainCellFormat.TypeOutlineShift);
        var random = new Random(177);
        var origin = new Vector2Int(-12, 17);
        for (int y = -1; y <= 16; y++)
        {
            for (int x = -1; x <= 16; x++)
            {
                buffers.Set(origin.x + x, origin.y + y, (byte)(random.Next(3) switch { 0 => 1, 1 => 2, _ => 3 }));
            }
        }
        var incremental = new TerrainVolumeTopology();
        incremental.Publish(buffers, origin, 16, 16, distortion);
        for (int edit = 0; edit < 100; edit++)
        {
            int x = random.Next(-1, 17);
            int y = random.Next(-1, 17);
            buffers.Set(origin.x + x, origin.y + y, (byte)(random.Next(3) switch { 0 => 1, 1 => 2, _ => 3 }));
            ulong previous = incremental.Version;
            incremental.Publish(buffers, origin, 16, 16, distortion);
            var fresh = new TerrainVolumeTopology();
            fresh.Publish(buffers, origin, 16, 16, distortion);
            Assert.That(incremental.Edges, Is.EqualTo(fresh.Edges), $"edit {edit} at {x},{y}");
            if (incremental.Version != previous)
            {
                Assert.That(incremental.LastRebuiltCellCount, Is.LessThanOrEqualTo(9));
            }
        }
    }

    [Test]
    public void SingleBlockHasFourPhysicalEdgesAndAdjacentBlocksHaveNoInternalWall()
    {
        var buffers = new TerrainCellBuffers();
        buffers.Type(1, 0);
        buffers.Set(2, 2, 1);
        var topology = new TerrainVolumeTopology();
        topology.Publish(buffers, Vector2Int.zero, 8, 8, false);
        Assert.That(topology.Edges.Count, Is.EqualTo(4));
        Assert.That(topology.Edges.All(edge => edge.X == 2 && edge.Y == 2), Is.True);
        buffers.Set(3, 2, 1);
        topology.Publish(buffers, Vector2Int.zero, 8, 8, false);
        Assert.That(topology.Edges.Count, Is.EqualTo(6));
        Assert.That(topology.Edges.Any(edge => edge.X == 2 && edge.Start.x == 1 && edge.End.x == 1), Is.False);
        Assert.That(topology.Edges.Any(edge => edge.X == 3 && edge.Start.x == 0 && edge.End.x == 0), Is.False);
        buffers.Set(3, 2, 2);
        topology.Publish(buffers, Vector2Int.zero, 8, 8, false);
        Assert.That(topology.Edges.Count, Is.EqualTo(4));
        Assert.That(topology.Edges.Any(edge => edge.Start.x == 1 && edge.End.x == 1), Is.True);
    }

    [Test]
    public void RoundBlockEdgesEncloseTheIndependentPixelAreaAndAreOnArtGrid()
    {
        var buffers = new TerrainCellBuffers();
        buffers.Type(1, (uint)CellOutlineKind.Round << TerrainCellFormat.TypeOutlineShift);
        buffers.Set(2, 2, 1);
        var topology = new TerrainVolumeTopology();
        topology.Publish(buffers, Vector2Int.zero, 8, 8, false);
        int pixels = 0;
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                double dx = (x + .5) / 32 - .5;
                double dy = (y + .5) / 32 - .5;
                if (dx * dx + dy * dy <= .5 * .5 || Math.Abs(Math.Abs(dx) - Math.Abs(dy)) >= .4)
                {
                    pixels++;
                }
            }
        }
        double area = 0;
        foreach (TerrainVolumeEdge edge in topology.Edges)
        {
            area += (edge.Start.x * edge.End.y - edge.End.x * edge.Start.y) * .5;
            float lengthPixels = (Math.Abs(edge.Start.x - edge.End.x) + Math.Abs(edge.Start.y - edge.End.y)) * 32;
            Assert.That(lengthPixels, Is.GreaterThanOrEqualTo(1));
            Assert.That(lengthPixels, Is.EqualTo(MathF.Round(lengthPixels)));
            Assert.That(edge.Start.x * 32, Is.EqualTo(MathF.Round(edge.Start.x * 32)));
            Assert.That(edge.Start.y * 32, Is.EqualTo(MathF.Round(edge.Start.y * 32)));
        }
        Assert.That(area, Is.EqualTo(pixels / 1024.0));
        Assert.That(topology.Edges.Count, Is.GreaterThan(4));
    }

    [Test]
    public void NegativeWindowOriginsPublishLocalAddressesAndNewTopologyVersion()
    {
        var buffers = new TerrainCellBuffers();
        buffers.Type(1, 0);
        buffers.Set(-3, -4, 1);
        var topology = new TerrainVolumeTopology();
        topology.Publish(buffers, new Vector2Int(-5, -5), 8, 8, true);
        Assert.That(topology.Edges.All(edge => edge.X == 2 && edge.Y == 1), Is.True);
        Assert.That(topology.Version, Is.EqualTo(1));
    }
    [TestCase(0, -16, false)]
    [TestCase(16, 0, false)]
    [TestCase(-16, 0, false)]
    [TestCase(0, 16, false)]
    [TestCase(1, 1, false)]
    [TestCase(-5, 9, false)]
    [TestCase(0, -16, true)]
    [TestCase(16, 0, true)]
    [TestCase(-1, 1, true)]
    public void ScrollingWindowShiftsEdgesAndRebuildsOnlyTheEnteredBand(int dx, int dy, bool distortion)
    {
        const int width = 64;
        const int height = 48;
        var buffers = new TerrainCellBuffers();
        buffers.Type(1, 1u << TerrainCellFormat.TypeOutlineShift);
        buffers.Type(3, 3u << TerrainCellFormat.TypeOutlineShift);
        var random = new Random(991);
        var origin = new Vector2Int(128, 256);
        int margin = (Math.Max(Math.Abs(dx), Math.Abs(dy)) * 4) + 2;
        for (int y = -margin - 1; y <= height + margin; y++)
        {
            for (int x = -margin - 1; x <= width + margin; x++)
            {
                buffers.Set(
                    origin.x + x,
                    origin.y + y,
                    (byte)(random.Next(3) switch { 0 => 1, 1 => 2, _ => 3 }));
            }
        }

        var incremental = new TerrainVolumeTopology();
        incremental.Publish(buffers, origin, width, height, distortion);

        for (int step = 1; step <= 4; step++)
        {
            var scrolledOrigin = new Vector2Int(origin.x + (dx * step), origin.y + (dy * step));
            incremental.Publish(buffers, scrolledOrigin, width, height, distortion);

            var fresh = new TerrainVolumeTopology();
            fresh.Publish(buffers, scrolledOrigin, width, height, distortion);
            Assert.That(incremental.Edges, Is.EqualTo(fresh.Edges), $"step {step}");
            Assert.That(incremental.LastRebuiltCellCount, Is.LessThan(width * height),
                $"step {step} rebuilt the whole window");
            Assert.That(incremental.Version, Is.EqualTo((ulong)step + 1));
        }
    }

    [Test]
    public void UnchangedCellsAndMaterialOnlyTypeChangesDoNotRepublishWallTopology()
    {
        var buffers = new TerrainCellBuffers();
        buffers.Type(1, 0);
        buffers.Set(2, 2, 1);
        var topology = new TerrainVolumeTopology();
        topology.Publish(buffers, Vector2Int.zero, 8, 8, false);
        ulong version = topology.Version;
        topology.Publish(buffers, Vector2Int.zero, 8, 8, false);
        Assert.That(topology.Version, Is.EqualTo(version));
        buffers.Type(1, 1u << 12);
        topology.Publish(buffers, Vector2Int.zero, 8, 8, false);
        Assert.That(topology.Version, Is.EqualTo(version));
        buffers.Type(1, TerrainCellFormat.TypeBackground);
        topology.Publish(buffers, Vector2Int.zero, 8, 8, false);
        Assert.That(topology.Version, Is.EqualTo(version + 1));
        Assert.That(topology.Edges, Is.Empty);
    }

}
