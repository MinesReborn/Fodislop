using Kern.Rendering;
using NUnit.Framework;

namespace Kern.FrameHarness.Tests;

/// <summary>Independent layout expectations only; these tests do not claim production image coverage.</summary>
[TestFixture]
public sealed class WorldRenderGridTests
{
    [Test]
    public void CellHas32PixelsAndTwoGuardPixels()
    {
        var grid = WorldRenderGrid.Create(0, 0, 1, 1);
        Assert.That((grid.MinPixelX, grid.MinPixelY, grid.Width, grid.Height), Is.EqualTo((-1, -1, 34, 34)));
        Assert.That(grid.PixelCount, Is.EqualTo(1156));
        Assert.That(grid.PixelCenter(1, 1), Is.EqualTo((1.0 / 64, 1.0 / 64)));
        Assert.That(grid.ViewportToWorldUv(0, 0), Is.EqualTo((1.0 / 34, 1.0 / 34)));
        Assert.That(grid.ViewportToWorldUv(1, 1), Is.EqualTo((33.0 / 34, 33.0 / 34)));
    }

    [Test]
    public void NegativeFractionalCoverageRoundsOutward()
    {
        var grid = WorldRenderGrid.Create(-0.01, -1.01, 1, 2);
        Assert.That((grid.MinPixelX, grid.MinPixelY, grid.Width, grid.Height), Is.EqualTo((-2, -34, 35, 67)));
        Assert.That(grid.PixelCenter(0, 0), Is.EqualTo((-1.5 / 32, -33.5 / 32)));
        Assert.That(grid.WorldToViewportUv(0.49, -0.01).U, Is.EqualTo(0.5).Within(1e-12));
        Assert.That(grid.WorldToViewportUv(0.49, -0.01).V, Is.EqualTo(0.5).Within(1e-12));
    }

    [Test]
    public void TranslationReusesSizeButChangesWorldAddress()
    {
        var before = WorldRenderGrid.Create(-2, 3, 12, 8);
        var after = WorldRenderGrid.Create(-2 + 1.0 / 32, 3 - 1.0 / 32, 12, 8);
        Assert.That(before.HasSameTargetSize(after), Is.True);
        Assert.That(after.MinPixelX, Is.EqualTo(before.MinPixelX + 1));
        Assert.That(after.MinPixelY, Is.EqualTo(before.MinPixelY - 1));
        Assert.That(before.HasSameTargetSize(WorldRenderGrid.Create(-2, 3, 13, 8)), Is.False);
    }

    [Test]
    public void DistantZoomCanExceedDisplayWithoutDensityReduction()
    {
        var grid = WorldRenderGrid.Create(-100, -50, 200, 100);
        Assert.That((grid.Width, grid.Height), Is.EqualTo((6402, 3202)));
        Assert.That(grid.PixelCount, Is.EqualTo(20_499_204));
    }

    [TestCase(double.NaN, 0, 1, 1)]
    [TestCase(0, double.PositiveInfinity, 1, 1)]
    [TestCase(0, 0, 0, 1)]
    [TestCase(0, 0, 1, -1)]
    [TestCase(0, 0, double.NaN, 1)]
    public void InvalidCoverageFails(double x, double y, double width, double height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldRenderGrid.Create(x, y, width, height));
    }

    [Test]
    public void UnrepresentableBoundsAndDefaultGridFail()
    {
        Assert.Throws<OverflowException>(() => WorldRenderGrid.Create(int.MaxValue, 0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldRenderGrid.Create(1e30, 0, 1, 1));
        Assert.Throws<InvalidOperationException>(() => default(WorldRenderGrid).ViewportToWorldUv(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldRenderGrid.Create(0, 0, 1, 1).PixelCenter(34, 0));
    }
}
