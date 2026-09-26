#nullable enable

using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

[TestFixture]
public sealed class TerrainCellRegionTests
{
    [Test]
    public void WorldRegionConvertsToWindowLocalWithNeighbourHalo()
    {
        var worldRegion = new TerrainWorldCellRegion(new RectInt(12, 23, 2, 3));

        TerrainWindowCellRegion local = worldRegion.ToWindowLocal(
            new Vector2Int(10, 20),
            new Vector2Int(8, 8),
            neighbourHalo: 1);

        Assert.That(local, Is.EqualTo(new TerrainWindowCellRegion(1, 2, 4, 5)));
    }

    [Test]
    public void WorldRegionClipsHaloAtWindowEdges()
    {
        var worldRegion = new TerrainWorldCellRegion(new RectInt(10, 20, 1, 1));

        TerrainWindowCellRegion local = worldRegion.ToWindowLocal(
            new Vector2Int(10, 20),
            new Vector2Int(8, 8),
            neighbourHalo: 1);

        Assert.That(local, Is.EqualTo(new TerrainWindowCellRegion(0, 0, 2, 2)));
    }

    [Test]
    public void WorldRegionOutsideWindowProducesEmptyLocalRegion()
    {
        var worldRegion = new TerrainWorldCellRegion(new RectInt(40, 40, 2, 2));

        TerrainWindowCellRegion local = worldRegion.ToWindowLocal(
            new Vector2Int(10, 20),
            new Vector2Int(8, 8),
            neighbourHalo: 1);

        Assert.That(local.IsEmpty, Is.True);
    }
}
