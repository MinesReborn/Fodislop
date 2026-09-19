#nullable enable

using Kern.World.Terrain;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

[TestFixture]
[Category("FuzzPure")]
public class TerrainVertexDistortionCalculatorFuzzTests
{
    [TestCase(0, 0)]
    [TestCase(1, 1)]
    [TestCase(100, 100)]
    [TestCase(3221, 3469)]
    public void RandXd_AlwaysIntegerInZeroToSix(int x, int y)
    {
        float rx = TerrainVertexDistortionCalculator.RandXd(x, y);
        Assert.That(rx, Is.GreaterThanOrEqualTo(0f));
        Assert.That(rx, Is.LessThanOrEqualTo(6f));
        Assert.That(rx % 1f, Is.EqualTo(0f));
    }

    [TestCase(0, 0)]
    [TestCase(1, 1)]
    [TestCase(100, 100)]
    [TestCase(3221, 3469)]
    public void RandYd_AlwaysIntegerInZeroToSix(int x, int y)
    {
        float ry = TerrainVertexDistortionCalculator.RandYd(x, y);
        Assert.That(ry, Is.GreaterThanOrEqualTo(0f));
        Assert.That(ry, Is.LessThanOrEqualTo(6f));
        Assert.That(ry % 1f, Is.EqualTo(0f));
    }

    [Test]
    public void Rand_IsDeterministic_SameInputSameOutput()
    {
        for (int x = 0; x < 50; x++)
        {
            for (int y = 0; y < 50; y++)
            {
                Assert.That(TerrainVertexDistortionCalculator.RandXd(x, y),
                    Is.EqualTo(TerrainVertexDistortionCalculator.RandXd(x, y)),
                    $"x={x},y={y}");
            }
        }
    }

    [TestCase(0, 10, 100, 100)]
    [TestCase(100, 10, 100, 100)]
    [TestCase(10, 0, 100, 100)]
    [TestCase(10, 100, 100, 100)]
    [TestCase(1, 1, 100, 100)]
    [TestCase(99, 99, 100, 100)]
    public void ComputeOffset_OnWorldEdge_ReturnsZero(int worldX, int worldY, int w, int h)
    {
        var c = new CachedCellData { Distortion = CellDistortionType.Cause };
        Vector3 offset = TerrainVertexDistortionCalculator.ComputeOffset(c, c, c, c, worldX, worldY, w, h);
        Assert.That(offset, Is.EqualTo(Vector3.zero));
    }

    // Upstream (15bced90): клетка, окружённая источниками со всех сторон, не искажается.
    [Test]
    public void ComputeOffset_AllCauses_ReturnsZero()
    {
        var c = new CachedCellData { Distortion = CellDistortionType.Cause };
        for (int x = 1; x < 30; x++)
        {
            for (int y = 1; y < 30; y++)
            {
                Vector3 offset = TerrainVertexDistortionCalculator.ComputeOffset(c, c, c, c, x, y, 100, 100);
                Assert.That(offset, Is.EqualTo(Vector3.zero), $"x={x},y={y}");
            }
        }
    }

    [Test]
    public void ComputeOffset_AnyBlock_ReturnsZero()
    {
        var block = new CachedCellData { Distortion = CellDistortionType.Block };
        var none = new CachedCellData { Distortion = CellDistortionType.Neutral };
        Vector3 offset = TerrainVertexDistortionCalculator.ComputeOffset(none, none, none, block, 10, 10, 100, 100);
        Assert.That(offset, Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void ComputeOffset_OnlyOneCause_HasNonZeroOffset()
    {
        var cause = new CachedCellData { Distortion = CellDistortionType.Cause };
        var none = new CachedCellData { Distortion = CellDistortionType.Neutral };
        Vector3 offset = TerrainVertexDistortionCalculator.ComputeOffset(cause, none, none, none, 10, 10, 100, 100);
        Assert.That(offset.z, Is.EqualTo(0f));
        float mag = offset.x * offset.x + offset.y * offset.y;
        Assert.That(mag, Is.GreaterThan(0f), "single cause corner should produce non-zero offset");
    }

    [Test]
    public void IsCause_IsBlock_AreMutuallyExclusive()
    {
        for (byte d = 0; d < 4; d++)
        {
            var data = new CachedCellData { Distortion = (CellDistortionType)d };
            bool isCause = TerrainVertexDistortionCalculator.IsCause(data);
            bool isBlock = TerrainVertexDistortionCalculator.IsBlock(data);
            Assert.That(isCause && isBlock, Is.False, $"d={d}");
        }
    }
}
