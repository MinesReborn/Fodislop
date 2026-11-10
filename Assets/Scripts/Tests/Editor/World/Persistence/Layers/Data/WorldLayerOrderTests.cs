#nullable enable

namespace Kern.Tests.World;

using Kern.Core;
using NUnit.Framework;

// Порядок слоёв мира. Слои рисуются с альфа-блендингом, поэтому номер решает,
// что чем накрыто; совпавший номер не означает «рядом», он означает «порядок
// не задан» — Unity доберёт его очередью материала и расстоянием до камеры, и
// он поедет от кадра к кадру.
//
// Числа сами по себе ничего не значат и менять их можно свободно. Значат
// только знаки разностей, и они здесь закреплены.
[TestFixture]
public class WorldLayerOrderTests
{
    private const int WorldBackground =
        WorldRenderLayers.WorldBackgroundSortingOrder;
    private const int Terrain =
        WorldRenderLayers.TerrainSortingOrder;
    private const int WorldEntities =
        WorldRenderLayers.WorldEntitySortingOrder;
    private const int TerrainForeground =
        WorldRenderLayers.TerrainForegroundSortingOrder;

    [Test]
    public void WorldBackgroundStaysStrictlyBehindTerrain()
    {
        Assert.That(
            WorldBackground,
            Is.LessThan(Terrain),
            "Подложка мира обязана лежать под террейном, а не вровень с ним.");
    }

    [Test]
    public void WorldEntitiesStayUnderTerrainBlocks()
    {
        Assert.That(
            WorldEntities,
            Is.GreaterThan(Terrain),
            "Роботы и хвосты обязаны лежать над фоновыми клетками террейна.");
        Assert.That(
            TerrainForeground,
            Is.GreaterThan(WorldEntities),
            "Стенки и передний план блоков обязаны накрывать роботов и хвосты.");
    }
}
