#nullable enable

using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.PlayMode;

public sealed class WorldBloomKernelContractTests
{
    [TestCase("Prefilter")]
    [TestCase("Downsample")]
    [TestCase("Upsample")]
    public void ProductionBloomKernelExists(string kernelName)
    {
        ComputeShader shader = Resources.Load<ComputeShader>("Shaders/PostProcessing/WorldBloom");
        Assert.That(shader, Is.Not.Null);
        Assert.That(shader.HasKernel(kernelName), Is.True);
    }

    [Test]
    public void ProductionBloomAddShaderExists()
    {
        Assert.That(Resources.Load<Shader>("Shaders/PostProcessing/WorldBloomAdd"), Is.Not.Null);
    }
}
