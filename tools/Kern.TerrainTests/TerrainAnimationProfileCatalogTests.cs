#nullable enable

using Kern.World.Terrain;
using MinesServer.Data;
using NUnit.Framework;

namespace Kern.TerrainTests;

[TestFixture]
public sealed class TerrainAnimationProfileCatalogTests
{
    [TestCase(CellType.XGreen)]
    [TestCase(CellType.XBlue)]
    [TestCase(CellType.XRed)]
    [TestCase(CellType.XCyan)]
    [TestCase(CellType.XViolet)]
    public void Get_XCrystal_UsesPrismaticProfile(CellType cellType)
    {
        TerrainAnimationSettings settings =
            TerrainAnimationProfileCatalog.Get(cellType, 7f);

        Assert.That(settings.Profile, Is.EqualTo(TerrainAnimationProfile.PrismaticCrystal));
        Assert.That(settings.Speed, Is.EqualTo(50f));
    }

    [TestCase(CellType.XGreen, 1f)]
    [TestCase(CellType.XBlue, 2f)]
    [TestCase(CellType.XRed, 3f)]
    [TestCase(CellType.XViolet, 4f)]
    [TestCase(CellType.XCyan, 5f)]
    public void Get_XCrystal_CarriesDistinctPalette(CellType cellType, float palette)
    {
        Assert.That(TerrainAnimationProfileCatalog.Get(cellType, 0f).PaletteIndex, Is.EqualTo(palette));
    }

    [TestCase(CellType.PurpleAcid)]
    [TestCase(CellType.Lava)]
    public void Get_OtherCell_PreservesConfiguredAnimation(CellType cellType)
    {
        TerrainAnimationSettings settings =
            TerrainAnimationProfileCatalog.Get(cellType, 7f);

        Assert.That(settings.Profile, Is.EqualTo(TerrainAnimationProfile.Default));
        Assert.That(settings.Speed, Is.EqualTo(7f));
    }

    [TestCase(CellType.Green)]
    [TestCase(CellType.Red)]
    [TestCase(CellType.Blue)]
    [TestCase(CellType.Violet)]
    [TestCase(CellType.White)]
    [TestCase(CellType.Cyan)]
    public void Get_ColoredCrystal_UsesFacetedProfile(CellType cellType)
    {
        TerrainAnimationSettings settings =
            TerrainAnimationProfileCatalog.Get(cellType, 7f);

        Assert.That(settings.Profile, Is.EqualTo(TerrainAnimationProfile.FacetedCrystal));
        Assert.That(settings.Speed, Is.EqualTo(0.06f));
    }

}
