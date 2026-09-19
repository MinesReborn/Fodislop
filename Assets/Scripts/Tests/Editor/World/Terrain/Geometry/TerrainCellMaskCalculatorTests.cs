#nullable enable

namespace Kern.Tests.World;

using Kern.World.Terrain;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using NUnit.Framework;

[TestFixture]
public class TerrainCellMaskCalculatorTests
{
    [Test]
    public void EnsureCapacity_AllocatesCorrectArraySizes()
    {
        var calculator = new TerrainCellMaskCalculator();
        calculator.EnsureCapacity(16, 32);

        Assert.IsNotNull(calculator.CellTilingDescriptors);
        Assert.AreEqual(16, calculator.CellTilingDescriptors.GetLength(0));
        Assert.AreEqual(32, calculator.CellTilingDescriptors.GetLength(1));

        Assert.IsNotNull(calculator.CellCornerVariants);
        Assert.AreEqual(16, calculator.CellCornerVariants.GetLength(0));
        Assert.AreEqual(32, calculator.CellCornerVariants.GetLength(1));

        Assert.IsNotNull(calculator.CellReliefMasks);
        Assert.AreEqual(16, calculator.CellReliefMasks.GetLength(0));
        Assert.AreEqual(32, calculator.CellReliefMasks.GetLength(1));

        Assert.IsNotNull(calculator.CellSolidBoundaryMasks);
        Assert.AreEqual(16, calculator.CellSolidBoundaryMasks.GetLength(0));
        Assert.AreEqual(32, calculator.CellSolidBoundaryMasks.GetLength(1));
    }

    [Test]
    public void CalculateTilingDescriptor_WithoutTileGroup_ReturnsZero()
    {
        var empty = new CachedCellData { HasTileGroup = false, TileGroupID = 0 };
        var neighbor = new CachedCellData { HasTileGroup = true, TileGroupID = 1 };

        int descriptor = TerrainCellMaskCalculator.CalculateTilingDescriptor(
            empty, neighbor, neighbor, neighbor, neighbor, neighbor, neighbor, neighbor, neighbor);

        Assert.AreEqual(0, descriptor);
    }

    [Test]
    public void CalculateCornerSideMask_OnlyForBuildingWall_FlagsCorners()
    {
        var wall = new CachedCellData { Type = CellType.BuildingWall };
        var floor = new CachedCellData { Type = CellType.Empty };
        var corner = new CachedCellData { Type = CellType.BuildingCorner };
        var regular = new CachedCellData { Type = CellType.Rock };

        int emptyCenter = TerrainCellMaskCalculator.CalculateCornerSideMask(floor, corner, corner, corner, corner);
        Assert.AreEqual(0, emptyCenter);

        int allCorners = TerrainCellMaskCalculator.CalculateCornerSideMask(wall, corner, corner, corner, corner);
        Assert.AreEqual(1 | 2 | 4 | 8, allCorners);

        int leftTop = TerrainCellMaskCalculator.CalculateCornerSideMask(wall, corner, regular, corner, regular);
        Assert.AreEqual(1 | 4, leftTop);
    }

    [Test]
    public void CalculateReliefMask_NeighborsHigherOrEqual_SetsBitmask()
    {
        var center = new CachedCellData { ReliefGroup = 5 };
        var higher = new CachedCellData { ReliefGroup = 6 };
        var equal = new CachedCellData { ReliefGroup = 5 };
        var lower = new CachedCellData { ReliefGroup = 4 };

        byte allHigher = TerrainCellMaskCalculator.CalculateReliefMask(center, higher, equal, higher, equal);
        Assert.AreEqual(1 | 2 | 4 | 8, (int)allHigher);

        byte allLower = TerrainCellMaskCalculator.CalculateReliefMask(center, lower, lower, lower, lower);
        Assert.AreEqual(0, (int)allLower);

        byte topAndRight = TerrainCellMaskCalculator.CalculateReliefMask(center, higher, lower, lower, equal);
        Assert.AreEqual(1 | 8, (int)topAndRight);
    }

    [Test]
    public void CalculateSolidBoundaryMask_DropsShadowFlag_Sets8NeighborBits()
    {
        var shadow = new CachedCellData { Properties = CellConfigProperties.DropsShadow };
        var empty = new CachedCellData { Properties = CellConfigProperties.None };

        byte allShadow = TerrainCellMaskCalculator.CalculateSolidBoundaryMask(
            shadow, shadow, shadow, shadow, shadow, shadow, shadow, shadow);
        Assert.AreEqual(255, (int)allShadow);

        byte noneShadow = TerrainCellMaskCalculator.CalculateSolidBoundaryMask(
            empty, empty, empty, empty, empty, empty, empty, empty);
        Assert.AreEqual(0, (int)noneShadow);

        byte topOnly = TerrainCellMaskCalculator.CalculateSolidBoundaryMask(
            shadow, empty, empty, empty, empty, empty, empty, empty);
        Assert.AreEqual(1, (int)topOnly);

        byte leftOnly = TerrainCellMaskCalculator.CalculateSolidBoundaryMask(
            empty, shadow, empty, empty, empty, empty, empty, empty);
        Assert.AreEqual(2, (int)leftOnly);

        byte bottomRightOnly = TerrainCellMaskCalculator.CalculateSolidBoundaryMask(
            empty, empty, empty, empty, empty, empty, empty, shadow);
        Assert.AreEqual(128, (int)bottomRightOnly);
    }
}
