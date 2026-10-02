#nullable enable

using System;
using Kern.World.Terrain;
using NUnit.Framework;

namespace Kern.Tests.World.Streaming;

public sealed class TerrainMemoryEstimateTests
{
    [Test]
    public void UsesActualChannelFormatsAndTwoLayers()
    {
        TerrainMemoryEstimate estimate = TerrainMemoryEstimate.ForWindow(10, 20, stagingRows: 128);

        // 10 * 20 cells, two layers, 80 bytes per texel across nine channels.
        Assert.That(estimate.GPUTargetBytes, Is.EqualTo(10 * 20 * 2 * 80));
        Assert.That(estimate.GPUStagingBytes, Is.EqualTo(10 * 40 * 80));
        Assert.That(estimate.CPUTexelBytes, Is.EqualTo(estimate.GPUTargetBytes));
        Assert.That(estimate.PeakBytes, Is.EqualTo(
            (estimate.GPUTargetBytes * 2) + estimate.GPUStagingBytes));
    }

    [Test]
    public void MaximumSupportedWindowStaysUnderSixtyMegabytes()
    {
        TerrainMemoryEstimate estimate = TerrainMemoryEstimate.ForWindow(384, 384);

        Assert.That(estimate.GPUTargetBytes, Is.EqualTo(384L * 384 * 2 * 80));
        Assert.That(estimate.PeakBytes, Is.LessThan(60L * 1024 * 1024));
    }

    [Test]
    public void StagingRowsAreClampedToTextureHeight()
    {
        TerrainMemoryEstimate estimate = TerrainMemoryEstimate.ForWindow(4, 3, stagingRows: 128);

        Assert.That(estimate.GPUStagingBytes, Is.EqualTo(4 * 3 * 2 * 80));
    }

    [Test]
    public void RejectsInvalidWindowDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TerrainMemoryEstimate.ForWindow(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TerrainMemoryEstimate.ForWindow(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TerrainMemoryEstimate.ForWindow(1, 1, 0));
    }
}
