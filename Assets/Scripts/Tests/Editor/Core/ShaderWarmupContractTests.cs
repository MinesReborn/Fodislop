#nullable enable

using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using NUnit.Framework;
using UnityEngine;

namespace Fodinae.Tests.Core;

public sealed class ShaderWarmupContractTests
{
    private static readonly string[] RequiredShaders =
    [
        ProjectRuntimeContracts.ShaderNames.Terrain,
        ProjectRuntimeContracts.ShaderNames.WorldSurface,
        ProjectRuntimeContracts.ShaderNames.WorldEntity,
        ProjectRuntimeContracts.ShaderNames.DynamicEmission,
        ProjectRuntimeContracts.ShaderNames.PlanetSurface,
        ProjectRuntimeContracts.ShaderNames.PlanetAtmosphere,
        ProjectRuntimeContracts.ShaderNames.Starfield,
        ProjectRuntimeContracts.ShaderNames.MenuLineUnlit,
        ProjectRuntimeContracts.ShaderNames.UnpremultiplyAlpha,
    ];

    private static readonly string[] RequiredLightingKernels =
    [
        "SolveCascade",
        "SolveAutomaticNormals",
        "ResolveDirect",
        "SolveDiffuseBounce",
        "CompositeLighting",
    ];

    [Test]
    public void RequiredShaders_AreFoundAndSupported()
    {
        for (int i = 0; i < RequiredShaders.Length; i++)
        {
            string shaderName = RequiredShaders[i];
            Shader? shader = Shader.Find(shaderName);
            Assert.That(shader, Is.Not.Null, $"Required shader '{shaderName}' was not found.");
            Assert.That(shader!.isSupported, Is.True, $"Shader '{shaderName}' is not supported on the active graphics device.");
        }
    }

    [Test]
    public void WorldLightingCompute_IsFoundAndContainsAllKernels()
    {
        var compute = Resources.Load<ComputeShader>(ProjectRuntimeContracts.ResourcePaths.WorldLightingCompute);
        Assert.That(compute, Is.Not.Null, "WorldLighting.compute resource was not found.");

        for (int i = 0; i < RequiredLightingKernels.Length; i++)
        {
            string kernelName = RequiredLightingKernels[i];
            Assert.That(compute.HasKernel(kernelName), Is.True, $"Kernel '{kernelName}' missing in WorldLighting.compute.");
        }
    }

    [Test]
    public void ShaderWarmupService_CompletesWithoutExceptions()
    {
        var service = new ShaderWarmupService();
        float finalProgress = 0f;

        UniTask task = service.WarmupAsync(
            (_, progress) => finalProgress = progress,
            CancellationToken.None);

        task.GetAwaiter().GetResult();
        Assert.That(finalProgress, Is.EqualTo(1.0f).Within(0.001f), "ShaderWarmupService did not reach 100% completion.");
    }
}
