#nullable enable

using Kern.World;
using MinesServer.Data;
using NUnit.Framework;

namespace Kern.Tests.World;

// cells.json — источник каждого поля типа: значения ниже записаны там явно.
[TestFixture]
public sealed class BlockRegistryTests
{
    // Подложка — тип с drawLayer: Underlay, ровно один.
    [Test]
    public void UnderlayIsTheOneMarkedType()
    {
        Assert.That(BlockRegistry.UnderlayType, Is.EqualTo(CellType.Empty));
    }

    // Что под клеткой, не задаётся: фон лежит сам на себе, передний план —
    // на подложке.
    [TestCase(CellType.Empty, CellType.Empty)]
    [TestCase(CellType.Road, CellType.Road)]
    [TestCase(CellType.Gate, CellType.Gate)]
    [TestCase(CellType.VolcanoBackground, CellType.VolcanoBackground)]
    [TestCase(CellType.BuildingDoor, CellType.Empty)]
    [TestCase(CellType.BuildingWall, CellType.Empty)]
    [TestCase(CellType.Rock, CellType.Empty)]
    [TestCase(CellType.Unloaded, CellType.Empty)]
    public void UnderFollowsShape(CellType type, CellType expected)
    {
        Assert.That(Kern.World.Terrain.TerrainCellData.UnderOf(type), Is.EqualTo(expected));
    }

    [TestCase(CellType.WhiteSand, CellOutline.Round)]
    [TestCase(CellType.Lava, CellOutline.Round)]
    [TestCase(CellType.Empty, CellOutline.Rigid)]
    [TestCase(CellType.Rock, CellOutline.Wavy)]
    [TestCase(CellType.Boulder1, CellOutline.Rigid)]
    [TestCase(CellType.BuildingWall, CellOutline.Wall)]
    [TestCase(CellType.BuildingCorner, CellOutline.Corner)]
    [TestCase(CellType.BuildingDoor, CellOutline.Door)]
    public void OutlineIsAuthored(CellType type, CellOutline outline)
    {
        Assert.That(BlockRegistry.Get(type).Outline, Is.EqualTo(outline));
    }

    [Test]
    public void LavaIsMoltenAtItsOwnSpeedAndGlows()
    {
        BlockDefinition lava = BlockRegistry.Get(CellType.Lava);
        Assert.That(lava.AnimationType, Is.EqualTo(CellAnimationType.Molten));
        Assert.That(lava.FrameAnimationSpeed, Is.EqualTo(10f));
        Assert.That(lava.Emission, Is.True);
    }

    [TestCase(CellType.GrayAcid, CellAnimationType.Blinking)]
    [TestCase(CellType.PurpleAcid, CellAnimationType.Shimmer)]
    [TestCase(CellType.Lava, CellAnimationType.Molten)]
    [TestCase(CellType.XGreen, CellAnimationType.Prismatic)]
    [TestCase(CellType.Green, CellAnimationType.Faceted)]
    public void AnimationTypeIsAuthored(CellType type, CellAnimationType animation)
    {
        BlockDefinition block = BlockRegistry.Get(type);
        Assert.That(block.AnimationType, Is.EqualTo(animation));
    }

}
