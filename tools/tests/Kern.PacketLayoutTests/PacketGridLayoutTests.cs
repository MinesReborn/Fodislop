using Kern.UI.ServerWindows;
using NUnit.Framework;

namespace Kern.PacketLayoutTests;

public sealed class PacketGridLayoutTests
{
    [Test]
    public void EmptyGridHasNoExtent()
    {
        GridRect[] result = PacketGridLayout.Measure([], [], [], 100, 100, out float width, out float height);
        Assert.That(result, Is.Empty);
        Assert.That(width, Is.Zero);
        Assert.That(height, Is.Zero);
    }

    [Test]
    public void FractionsShareSpaceAfterAutoColumn()
    {
        GridRect[] result = PacketGridLayout.Measure(
            [0, 1, 2], [0],
            [new(0, 0, 1, 1, 30, 10), new(0, 1, 1, 1, 0, 10), new(0, 2, 1, 1, 0, 10)],
            120, 10, out _, out _);
        Assert.That(result[0].Width, Is.EqualTo(30));
        Assert.That(result[1].Width, Is.EqualTo(30));
        Assert.That(result[2].Width, Is.EqualTo(60));
        Assert.That(result[2].Left, Is.EqualTo(60));
    }

    [Test]
    public void SpanningContentExpandsAutoTracks()
    {
        GridRect[] result = PacketGridLayout.Measure(
            [0, 0], [0, 0], [new(0, 0, 2, 2, 80, 40)],
            200, 200, out float width, out float height);
        Assert.That(width, Is.EqualTo(80));
        Assert.That(height, Is.EqualTo(40));
        Assert.That(result[0], Is.EqualTo(new GridRect(0, 0, 80, 40)));
    }

    [Test]
    public void SpanningContentKeepsExistingAutoMinimums()
    {
        PacketGridLayout.Measure(
            [0, 0], [0], [new(0, 0, 1, 1, 50, 10), new(0, 0, 1, 2, 80, 10)],
            200, 100, out float width, out _);
        Assert.That(width, Is.EqualTo(80));
    }

    [Test]
    public void AutoContentDoesNotProduceNegativeFractionSizes()
    {
        GridRect[] result = PacketGridLayout.Measure(
            [0, 1], [0], [new(0, 0, 1, 1, 80, 10), new(0, 1, 1, 1, 0, 10)],
            40, 10, out float width, out _);
        Assert.That(width, Is.EqualTo(80));
        Assert.That(result[1].Width, Is.Zero);
    }
}
