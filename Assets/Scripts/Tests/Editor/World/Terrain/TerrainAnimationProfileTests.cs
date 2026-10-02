#nullable enable

using Kern.World.Terrain;
using MinesServer.Data;
using NUnit.Framework;

namespace Kern.Tests.World;

[TestFixture]
public sealed class TerrainAnimationProfileTests
{
    [Test]
    public void LavaUsesAnimatedMoltenSurfaceProfile()
    {
        TerrainAnimationSettings settings = TerrainAnimationProfileCatalog.Get(CellType.Lava, configuredSpeed: 0f);

        Assert.That(settings.Profile, Is.EqualTo(TerrainAnimationProfile.MoltenSurface));
        Assert.That(settings.Speed, Is.EqualTo(10f));
    }
}
