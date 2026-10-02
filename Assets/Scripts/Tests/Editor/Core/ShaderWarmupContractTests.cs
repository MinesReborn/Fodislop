#nullable enable

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Rendering;

namespace Kern.Tests.Core;

public sealed class ShaderWarmupContractTests
{
    [Test]
    public void RecordedAmbientOcclusionClearStates_HaveProceduralVertexInputs()
    {
        GraphicsStateCollection collection = Resources.Load<GraphicsStateCollection>(
            ProjectRuntimeContracts.ResourcePaths.GraphicsStateCollection);
        Shader shader = Resources.Load<Shader>("Shaders/Lighting/LightingFieldRectClear");
        Assert.That(collection, Is.Not.Null);
        Assert.That(shader, Is.Not.Null);
        var variants = new List<GraphicsStateCollection.ShaderVariant>();
        collection.GetVariants(variants);
        GraphicsStateCollection.ShaderVariant[] recorded = variants
            .Where(variant => variant.shader == shader &&
                variant.passId.SubshaderIndex == 0 && variant.passId.PassIndex == 0)
            .ToArray();
        Assert.That(recorded, Is.Not.Empty, "Regional AO clear must be in the shipped warmup trace.");
        var states = new List<GraphicsStateCollection.GraphicsState>();
        foreach (GraphicsStateCollection.ShaderVariant variant in recorded)
        {
            states.Clear();
            collection.GetGraphicsStatesForVariant(variant, states);
            Assert.That(states, Is.Not.Empty);
            foreach (GraphicsStateCollection.GraphicsState state in states)
            {
                Assert.That(state.vertexAttributes, Is.Empty,
                    "Production clear uses SV_VertexID; a mesh vertex layout warms a different pipeline.");
            }
        }
    }

    [Test]
    public void RecordedWorldSurfaceStates_HaveLightingVertexInputs()
    {
        GraphicsStateCollection collection = Resources.Load<GraphicsStateCollection>(
            ProjectRuntimeContracts.ResourcePaths.GraphicsStateCollection);
        Assert.That(collection, Is.Not.Null);
        Shader worldSurface = Shader.Find(ProjectRuntimeContracts.ShaderNames.WorldSurface);
        Assert.That(worldSurface, Is.Not.Null);
        var variants = new List<GraphicsStateCollection.ShaderVariant>();
        collection.GetVariants(variants);
        var states = new List<GraphicsStateCollection.GraphicsState>();
        foreach (GraphicsStateCollection.ShaderVariant variant in variants)
        {
            if (variant.shader != worldSurface) { continue; }
            states.Clear();
            collection.GetGraphicsStatesForVariant(variant, states);
            foreach (GraphicsStateCollection.GraphicsState state in states)
            {
                string inputs = string.Join(",", state.vertexAttributes.Select(input =>
                    $"{input.attribute}:{input.dimension}@{input.stream}"));
                string keywords = string.Join(",", variant.keywords.Select(keyword => keyword.name));
                TestContext.WriteLine($"shader={variant.shader.name}; pass={variant.passId.SubshaderIndex}/{variant.passId.PassIndex}; keywords={keywords}; inputs={inputs}");
                if (variant.keywords.Any(keyword => keyword.name is "KERN_SURFACE_TRANSIT" or "KERN_SURFACE_REDROCK"))
                {
                    Assert.That(state.vertexAttributes.Any(input => input.attribute == VertexAttribute.TexCoord1),
                        Is.True, $"Recorded {variant.shader.name}, pass {variant.passId}, keywords {keywords} lacks the production lighting input: {inputs}");
                }
            }
        }
    }

#if UNITY_EDITOR
    // Explicit repair command, never called by a test or startup. Preserve each
    // recorded render state and use a vertex layout actually recorded for that
    // same variant. Do not synthesize a layout or clear the collection.
    public static void RepairRecordedWorldSurfaceVertexInputs()
    {
        GraphicsStateCollection collection = Resources.Load<GraphicsStateCollection>(
            ProjectRuntimeContracts.ResourcePaths.GraphicsStateCollection);
        Shader worldSurface = Shader.Find(ProjectRuntimeContracts.ShaderNames.WorldSurface);
        Assert.That(collection, Is.Not.Null);
        Assert.That(worldSurface, Is.Not.Null);
        var variants = new List<GraphicsStateCollection.ShaderVariant>();
        collection.GetVariants(variants);
        var repairs = new List<(GraphicsStateCollection.ShaderVariant Variant,
            List<GraphicsStateCollection.GraphicsState> States)>();
        int repaired = 0;
        foreach (GraphicsStateCollection.ShaderVariant variant in variants)
        {
            if (variant.shader != worldSurface || !variant.keywords.Any(keyword =>
                keyword.name is "KERN_SURFACE_TRANSIT" or "KERN_SURFACE_REDROCK")) { continue; }
            var states = new List<GraphicsStateCollection.GraphicsState>();
            collection.GetGraphicsStatesForVariant(variant, states);
            var updated = new List<GraphicsStateCollection.GraphicsState>(states.Count);
            bool changed = false;
            foreach (GraphicsStateCollection.GraphicsState recorded in states)
            {
                GraphicsStateCollection.GraphicsState state = recorded;
                if (!recorded.vertexAttributes.Any(input => input.attribute == VertexAttribute.TexCoord1))
                {
                    GraphicsStateCollection.GraphicsState[] compatible = states.Where(candidate =>
                        candidate.vertexAttributes.Any(input => input.attribute == VertexAttribute.TexCoord1) &&
                        recorded.vertexAttributes.All(input => candidate.vertexAttributes.Contains(input))).ToArray();
                    Assert.That(compatible, Is.Not.Empty,
                        $"No observed compatible input layout for {worldSurface.name} pass {variant.passId.PassIndex}.");
                    state.vertexAttributes = (VertexAttributeDescriptor[])compatible[0].vertexAttributes.Clone();
                    changed = true;
                    repaired++;
                }
                updated.Add(state);
            }
            if (changed) { repairs.Add((variant, updated)); }
        }
        // All replacements are validated before touching the loaded asset.
        foreach (var repair in repairs)
        {
            collection.RemoveGraphicsStatesForVariant(repair.Variant);
            foreach (GraphicsStateCollection.GraphicsState state in repair.States)
            {
                collection.AddGraphicsStateForVariant(repair.Variant, state);
            }
        }
        string path = UnityEditor.AssetDatabase.GetAssetPath(collection);
        Assert.That(path, Is.Not.Empty);
        Assert.That(collection.SaveToFile(path), Is.True);
        UnityEditor.AssetDatabase.ImportAsset(path);
        Debug.Log($"Repaired {repaired} recorded surface vertex layouts in {path}; " +
            $"remaining graphics states={collection.totalGraphicsStateCount}.");
    }
#endif

    private static readonly string[] s_requiredShaders =
    [
        ProjectRuntimeContracts.ShaderNames.Terrain,
        ProjectRuntimeContracts.ShaderNames.WorldSurface,
        ProjectRuntimeContracts.ShaderNames.WorldEntity,
        ProjectRuntimeContracts.ShaderNames.Starfield,
        ProjectRuntimeContracts.ShaderNames.MenuLineUnlit,
        ProjectRuntimeContracts.ShaderNames.UnpremultiplyAlpha,
        ProjectRuntimeContracts.ShaderNames.MissionVirtualRing,
    ];

    private static readonly string[] s_requiredLightingKernels =
    [
        "SolveCascade",
        "ScrollRadianceAtlas",
        "SolveDynamicLighting",
        "ComposeDynamicLighting",
        "TraceDynamicPolar",
        "ClearDynamicDirect",
        "ResolveDirect",
        "ResolveTransmissionDebug",
        "CompositeLighting",
        "BuildCellSolidMask",
    ];

    [Test]
    public void RequiredShaders_AreFoundAndSupported()
    {
        for (int i = 0; i < s_requiredShaders.Length; i++)
        {
            string shaderName = s_requiredShaders[i];
            Shader? shader = shaderName == ProjectRuntimeContracts.ShaderNames.MissionVirtualRing
                ? Resources.Load<Shader>(ProjectRuntimeContracts.ResourcePaths.MissionVirtualRingShader)
                : Shader.Find(shaderName);
            Assert.That(shader, Is.Not.Null, $"Required shader '{shaderName}' was not found.");
            Assert.That(shader!.isSupported, Is.True, $"Shader '{shaderName}' is not supported on the active graphics device.");
        }
    }

    [Test]
    public void WorldLightingCompute_IsFoundAndContainsAllKernels()
    {
        var compute = Resources.Load<ComputeShader>(ProjectRuntimeContracts.ResourcePaths.WorldLightingCompute);
        Assert.That(compute, Is.Not.Null, "WorldLighting.compute resource was not found.");

        for (int i = 0; i < s_requiredLightingKernels.Length; i++)
        {
            string kernelName = s_requiredLightingKernels[i];
            Assert.That(compute.HasKernel(kernelName), Is.True, $"Kernel '{kernelName}' missing in WorldLighting.compute.");
        }
    }

    [UnityTest]
    public IEnumerator ShaderWarmupService_CompletesWithoutExceptions()
    {
        var service = new ShaderWarmupService();
        float finalProgress = 0f;

        yield return service.WarmupAsync(
            (_, progress) => finalProgress = progress,
            CancellationToken.None).ToCoroutine();

        Assert.That(finalProgress, Is.EqualTo(1.0f).Within(0.001f), "ShaderWarmupService did not reach 100% completion.");
    }
}
