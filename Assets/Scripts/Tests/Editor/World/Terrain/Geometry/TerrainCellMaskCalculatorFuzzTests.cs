#nullable enable

using Kern.World.Terrain;
using MinesServer.Data;
using NUnit.Framework;

namespace Kern.Tests.World;

[TestFixture]
[Category("FuzzPure")]
public class TerrainCellMaskCalculatorFuzzTests
{
    [TestCase(0, 0)]
    [TestCase(7, 15)]
    [TestCase(0, 7)]
    [TestCase(255, 255)]
    [TestCase(0, 255)]
    [TestCase(128, 128)]
    public void CalculateReliefMask_AlwaysFitsInFourBits(int data, int neighbor)
    {
        byte mask = TerrainCellMaskCalculator.CalculateReliefMask(
            new CachedCellData { ReliefGroup = (byte)data },
            new CachedCellData { ReliefGroup = (byte)neighbor },
            new CachedCellData { ReliefGroup = (byte)neighbor },
            new CachedCellData { ReliefGroup = (byte)neighbor },
            new CachedCellData { ReliefGroup = (byte)neighbor });
        Assert.That(mask, Is.InRange(0, 15), $"data={data}, neighbor={neighbor}");
    }

    [Test]
    public void CalculateReliefMask_SameGroup_ProducesFullMask()
    {
        var c = new CachedCellData { ReliefGroup = 5 };
        byte mask = TerrainCellMaskCalculator.CalculateReliefMask(c, c, c, c, c);
        Assert.That(mask, Is.EqualTo(15));
    }

    // Бит ставится, когда сосед не ниже центра: высокий рельеф примыкает к низкому.
    [Test]
    public void CalculateReliefMask_HigherNeighbors_ProduceFullMask()
    {
        var center = new CachedCellData { ReliefGroup = 5 };
        var side = new CachedCellData { ReliefGroup = 7 };
        byte mask = TerrainCellMaskCalculator.CalculateReliefMask(center, side, side, side, side);
        Assert.That(mask, Is.EqualTo(15));
    }

    [Test]
    public void CalculateReliefMask_LowerNeighbors_ProduceZero()
    {
        var center = new CachedCellData { ReliefGroup = 7 };
        var side = new CachedCellData { ReliefGroup = 5 };
        byte mask = TerrainCellMaskCalculator.CalculateReliefMask(center, side, side, side, side);
        Assert.That(mask, Is.EqualTo(0));
    }

    [Test]
    public void CalculateCornerSideMask_NonWallCell_AlwaysZero()
    {
        var data = new CachedCellData { Type = CellType.Rock };
        int mask = TerrainCellMaskCalculator.CalculateCornerSideMask(
            data, new CachedCellData(), new CachedCellData(), new CachedCellData(), new CachedCellData());
        Assert.That(mask, Is.EqualTo(0));
    }

    [Test]
    public void CalculateSolidBoundaryMask_AlwaysFitsInEightBits()
    {
        byte mask = TerrainCellMaskCalculator.CalculateSolidBoundaryMask(
            new CachedCellData(), new CachedCellData(), new CachedCellData(), new CachedCellData(),
            new CachedCellData(), new CachedCellData(), new CachedCellData(), new CachedCellData());
        Assert.That(mask, Is.InRange(0, 255));
    }

    [Test]
    public void CalculateTilingDescriptor_AlwaysValidTileIndex()
    {
        int desc = TerrainCellMaskCalculator.CalculateTilingDescriptor(
            new CachedCellData(), new CachedCellData(), new CachedCellData(), new CachedCellData(),
            new CachedCellData(), new CachedCellData(), new CachedCellData(), new CachedCellData(),
            new CachedCellData());
        int baseIndex = desc & 0x1F;
        Assert.That(baseIndex, Is.LessThanOrEqualTo(13));
    }
}
