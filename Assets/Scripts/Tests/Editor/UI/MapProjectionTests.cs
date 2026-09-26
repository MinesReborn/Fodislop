#nullable enable

using Kern.UI;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.UI;

[TestFixture]
public sealed class MapProjectionTests
{
    [TestCase(0f, 0f, 0.25f)]
    [TestCase(480.5f, 269.5f, 1f)]
    [TestCase(959f, 539f, 8f)]
    public void MapPixelToServer_UsesServerYDown(float pixelX, float pixelY, float scale)
    {
        Vector2 actual = MapProjection.MapPixelToServer(
            pixelX,
            pixelY,
            1200f,
            700f,
            scale,
            960,
            540);

        Assert.That(actual.x, Is.EqualTo(1200f + (pixelX - 480f) * scale).Within(0.001f));
        Assert.That(actual.y, Is.EqualTo(700f + (pixelY - 270f) * scale).Within(0.001f));
    }

    [Test]
    public void ServerCellToTexturePixel_MapsServerDownToTextureUp()
    {
        Vector2 actual = MapProjection.ServerCellToTexturePixel(
            101f,
            205f,
            100f,
            200f,
            1f,
            960,
            540);

        Assert.That(actual.x, Is.EqualTo(481f));
        Assert.That(actual.y, Is.EqualTo(264f));
    }

    [Test]
    public void MinimapProjection_CentersPlayerCellAndMapsServerYDownToLowerTextureRows()
    {
        Vector2Int centerPixel = MapProjection.ServerCellToMinimapPixel(30, 40, 30, 40, 160);
        Vector2Int belowPlayer = MapProjection.ServerCellToMinimapPixel(30, 41, 30, 40, 160);
        Vector2Int belowWorldSample = MapProjection.MinimapPixelToServerCell(80, 79, 30, 40, 160);

        Assert.That(centerPixel, Is.EqualTo(new Vector2Int(80, 80)));
        Assert.That(belowPlayer, Is.EqualTo(new Vector2Int(80, 79)));
        Assert.That(belowWorldSample, Is.EqualTo(new Vector2Int(30, 41)));
    }

    [Test]
    public void UnknownCellColor_IsDeterministicAndStriped()
    {
        Color32 first = MapProjection.UnknownCellColor(4, 6);
        Color32 repeated = MapProjection.UnknownCellColor(4, 6);
        Color32 nextStripe = MapProjection.UnknownCellColor(6, 6);

        Assert.That(repeated, Is.EqualTo(first));
        Assert.That(nextStripe, Is.Not.EqualTo(first));
    }

    [Test]
    public void SampleCellColor_DistinguishesUnknownFromOutOfBounds()
    {
        var sampler = new MapCellSampler();
        var colors = new Color32[256];

        Color32 unknown = MapProjection.SampleCellColor(
            sampler,
            colors,
            2,
            2,
            10,
            10,
            Color.black,
            out bool unknownWasLoaded);
        Color32 outOfBounds = MapProjection.SampleCellColor(
            sampler,
            colors,
            -1,
            2,
            10,
            10,
            Color.black,
            out bool outOfBoundsWasLoaded);

        Assert.That(unknown, Is.EqualTo(MapProjection.UnknownCellColor(2, 2)));
        Assert.That(unknownWasLoaded, Is.False);
        Assert.That(outOfBounds, Is.EqualTo((Color32)Color.black));
        Assert.That(outOfBoundsWasLoaded, Is.False);
    }
}
