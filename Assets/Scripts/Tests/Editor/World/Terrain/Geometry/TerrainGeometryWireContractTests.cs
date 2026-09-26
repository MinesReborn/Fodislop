#nullable enable

using Kern.World.Terrain;
using NUnit.Framework;

namespace Kern.Tests.World;

[TestFixture]
public sealed class TerrainGeometryWireContractTests
{
    [TestCase(-2, -2, -2, -2, 1)]
    [TestCase(0, 0, 0, 0, 313)]
    [TestCase(2, 2, 2, 2, 625)]
    public void OrganicEdgeCodeUsesStableBaseFiveOrder(
        int bottom,
        int right,
        int top,
        int left,
        int expected)
    {
        Assert.That(
            TerrainCellGeometry.EncodeOrganicEdges(bottom, right, top, left),
            Is.EqualTo(expected));
    }

    [TestCase(0, 0, 0)]
    [TestCase(1, 1, 128)]
    [TestCase(313, 57, 129)]
    [TestCase(625, 113, 130)]
    public void PackedMetaBytesDecodeToKnownOrganicEdgeCode(int code, byte low, byte high)
    {
        (byte actualLow, byte actualHigh) = TerrainCellGeometry.PackOrganicEdgeMetadata(code);

        Assert.That(actualLow, Is.EqualTo(low));
        Assert.That(actualHigh, Is.EqualTo(high));
        Assert.That(DecodeMetaBytes(actualLow, actualHigh), Is.EqualTo(code));
    }

    private static int DecodeMetaBytes(byte low, byte high) =>
        high >= 128 && high < byte.MaxValue
            ? low + ((high - 128) * 256)
            : 0;
}
