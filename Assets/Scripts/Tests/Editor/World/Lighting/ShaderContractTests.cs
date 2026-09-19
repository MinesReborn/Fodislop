#if UNITY_EDITOR
#nullable enable

using System.IO;
using System.Text.RegularExpressions;
using Kern.World.Lighting;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Kern.Tests.World.Lighting;

[TestFixture]
public sealed class ShaderContractTests
{
    private static readonly string _shaderDirectory = Path.Combine(
        Application.dataPath,
        "Resources/Shaders/Lighting");

    [Test]
    public void DdaBanRule_ForbiddenStagesNeverCallDdaTraversal()
    {
        string[] forbiddenStageFiles =
        {
            Path.Combine(_shaderDirectory, "Cascades/CascadeResolve.hlsl"),
            Path.Combine(_shaderDirectory, "Dynamic/DynamicLightTrace.hlsl"),
            Path.Combine(_shaderDirectory, "Bounce/BounceSolve.hlsl"),
            Path.Combine(_shaderDirectory, "Composite/CompositeLighting.hlsl"),
        };

        Regex ddaCallPattern = new(@"\b(TraceLightSegment|TraceRadianceSegment)\s*\(", RegexOptions.Compiled);

        foreach (string file in forbiddenStageFiles)
        {
            Assert.That(File.Exists(file), Is.True, $"Shader file not found: {file}");
            string content = File.ReadAllText(file);
            Match match = ddaCallPattern.Match(content);
            Assert.That(
                match.Success,
                Is.False,
                $"CRITICAL ARCHITECTURE VIOLATION: Forbidden DDA call '{match.Value}' found in '{Path.GetFileName(file)}'. " +
                $"DDA traversal is strictly prohibited outside CascadeTrace, DynamicPolar, and BounceCache.");
        }
    }

    [Test]
    public void DynamicLightGpuData_StrideMatchesShaderExpectation()
    {
        // Shaders/Lighting/LightingTypes.hlsl defines DynamicLight as:
        // float4 positionRadius (16 bytes) + float4 colorIntensity (16 bytes) = 32 bytes
        int actualSize = UnsafeUtility.SizeOf<DynamicLightGpuData>();
        Assert.AreEqual(32, actualSize, "DynamicLightGpuData must be exactly 32 bytes to match HLSL struct.");
    }

    [Test]
    public void ComputeBinderProperties_AllDeclaredInComputeShader()
    {
        string computeFile = Path.Combine(_shaderDirectory, "WorldLighting.compute");
        Assert.That(File.Exists(computeFile), Is.True);

        string allShaderText = LoadShaderWithIncludes(computeFile);

        // Core uniforms and textures that must exist in shader
        string[] expectedIdentifiers =
        {
            "_MaterialField",
            "_EmissionField",
            "_RadianceAtlas",
            "_DirectTexture",
            "_DirectInput",
            "_StaticDirectInput",
            "_BounceTexture",
            "_BounceInput",
            "_Result",
            "_FieldSize",
            "_BounceSize",
            "_BounceDispatchOrigin",
            "_BounceDispatchSize",
            "_CompositeDispatchOrigin",
            "_CompositeDispatchSize",
            "_WorldRect",
            "_AmbientColor",
            "_EmptyExtinctionRGB",
            "_SolidExtinctionRGB",
            "_CellSize",
            "_CellSolidMask",
            "_DynamicLights",
            "_LightingCounters",
            "_DirtyRegions",
            "_DirtyRegionCount",
            "_CascadeChangedMask",
            "_CascadeMaskEnabled",
            "_EnableBilinearFix",
        };

        foreach (string id in expectedIdentifiers)
        {
            Assert.That(
                allShaderText.Contains(id),
                Is.True,
                $"Shader uniform '{id}' bound in C# was not found in WorldLighting.compute or included headers.");
        }
    }

    private static string LoadShaderWithIncludes(string rootFile)
    {
        string dir = Path.GetDirectoryName(rootFile)!;
        string text = File.ReadAllText(rootFile);

        return Regex.Replace(text, @"#include\s+""([^""]+)""", m =>
        {
            string incPath = Path.Combine(dir, m.Groups[1].Value);
            return File.Exists(incPath) ? LoadShaderWithIncludes(incPath) : "";
        });
    }
}
#endif
