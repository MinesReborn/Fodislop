#nullable enable

namespace Kern.Tests.World;

using Kern.World.Terrain;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class TerrainVertexDistortionCalculatorTests
{
    [Test]
    public void EnsureCapacity_AllocatesCorrectGridDimensions()
    {
        var calculator = new TerrainVertexDistortionCalculator();
        calculator.EnsureCapacity(10, 20);

        Assert.IsNotNull(calculator.GridVertexOffsets);
        Assert.AreEqual(11, calculator.GridVertexOffsets.GetLength(0));
        Assert.AreEqual(21, calculator.GridVertexOffsets.GetLength(1));
    }

    [Test]
    public void ComputeOffset_WorldBounds_ReturnsZero()
    {
        var cause = new CachedCellData { Distortion = CellDistortionType.Cause };

        Vector3 minX = TerrainVertexDistortionCalculator.ComputeOffset(cause, cause, cause, cause, 0, 10, 100, 100);
        Vector3 maxX = TerrainVertexDistortionCalculator.ComputeOffset(cause, cause, cause, cause, 100, 10, 100, 100);
        Vector3 minY = TerrainVertexDistortionCalculator.ComputeOffset(cause, cause, cause, cause, 10, 0, 100, 100);
        Vector3 maxY = TerrainVertexDistortionCalculator.ComputeOffset(cause, cause, cause, cause, 10, 100, 100, 100);

        Assert.AreEqual(Vector3.zero, minX);
        Assert.AreEqual(Vector3.zero, maxX);
        Assert.AreEqual(Vector3.zero, minY);
        Assert.AreEqual(Vector3.zero, maxY);
    }

    [Test]
    public void ComputeOffset_WhenAnyNeighborIsBlock_ReturnsZero()
    {
        var cause = new CachedCellData { Distortion = CellDistortionType.Cause };
        var block = new CachedCellData { Distortion = CellDistortionType.Block };

        Vector3 tlBlock = TerrainVertexDistortionCalculator.ComputeOffset(block, cause, cause, cause, 10, 10, 100, 100);
        Vector3 trBlock = TerrainVertexDistortionCalculator.ComputeOffset(cause, block, cause, cause, 10, 10, 100, 100);
        Vector3 blBlock = TerrainVertexDistortionCalculator.ComputeOffset(cause, cause, block, cause, 10, 10, 100, 100);
        Vector3 brBlock = TerrainVertexDistortionCalculator.ComputeOffset(cause, cause, cause, block, 10, 10, 100, 100);

        Assert.AreEqual(Vector3.zero, tlBlock);
        Assert.AreEqual(Vector3.zero, trBlock);
        Assert.AreEqual(Vector3.zero, blBlock);
        Assert.AreEqual(Vector3.zero, brBlock);
    }

    [Test]
    public void ComputeOffset_AllFourAreCause_ReturnsZero()
    {
        var cause = new CachedCellData { Distortion = CellDistortionType.Cause };
        int worldX = 15;
        int worldY = 25;

        // Upstream (15bced90): четыре источника вокруг — вершина не сдвигается.
        Vector3 result = TerrainVertexDistortionCalculator.ComputeOffset(cause, cause, cause, cause, worldX, worldY, 100, 100);

        Assert.AreEqual(Vector3.zero, result);
    }

    [Test]
    public void ComputeOffset_TwoOppositeAreCause_ReturnsZero()
    {
        var cause = new CachedCellData { Distortion = CellDistortionType.Cause };
        var none = new CachedCellData { Distortion = (CellDistortionType)0 };

        Vector3 diagonal1 = TerrainVertexDistortionCalculator.ComputeOffset(cause, none, none, cause, 10, 10, 100, 100);
        Vector3 diagonal2 = TerrainVertexDistortionCalculator.ComputeOffset(none, cause, cause, none, 10, 10, 100, 100);

        Assert.AreEqual(Vector3.zero, diagonal1);
        Assert.AreEqual(Vector3.zero, diagonal2);
    }

    [Test]
    public void ComputeOffset_TopAdjacentCause_PushesDown()
    {
        var cause = new CachedCellData { Distortion = CellDistortionType.Cause };
        var none = new CachedCellData { Distortion = (CellDistortionType)0 };
        int worldX = 12;
        int worldY = 18;

        float expectedRy = TerrainVertexDistortionCalculator.RandYd(worldX, worldY) / 16f;
        var expected = new Vector3(0, -expectedRy, 0);

        Vector3 result = TerrainVertexDistortionCalculator.ComputeOffset(cause, cause, none, none, worldX, worldY, 100, 100);

        Assert.AreEqual(expected, result);
    }

    [Test]
    public void RandMath_StaysWithinSeven()
    {
        for (int x = 1; x <= 50; x++)
        {
            for (int y = 1; y <= 50; y++)
            {
                float rx = TerrainVertexDistortionCalculator.RandXd(x, y);
                float ry = TerrainVertexDistortionCalculator.RandYd(x, y);

                Assert.IsTrue(rx >= 0 && rx < 7);
                Assert.IsTrue(ry >= 0 && ry < 7);
            }
        }
    }
}
