#nullable enable

using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Rules.CodeStyle;
using Kern.ArchitectureLinter.Rules.Rendering;
using Mono.Cecil;
using NUnit.Framework;

namespace Kern.ArchitectureLinter.Tests;

public sealed class NamingContractTests
{
    [TestCase("private static int s_count;", 0)]
    [TestCase("private int _count;", 0)]
    [TestCase("private int s_count;", 1)]
    [TestCase("private static int Count;", 1)]
    [TestCase("private static int _count;", 1)]
    [TestCase("private static readonly int s_count = 1;", 0)]
    public async Task NamingRuleRecognizesStaticPrefix(string declaration, int expectedCount)
    {
        string root = CreateRoot();
        try
        {
            Write(root, "Assets/Scripts/Fixture.cs", "class Fixture {\n" + declaration + "\n}");
            var violations = await new NamingConventionRule().EvaluateAsync(
                Array.Empty<AssemblyDefinition>(), Context(root));
            Assert.That(violations, Has.Count.EqualTo(expectedCount));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestCase("HDROutput.hlsl", 0)]
    [TestCase("HdrOutput.hlsl", 2)]
    public async Task DisplayRulePreservesUnityIncludeCase(string includeName, int expectedCount)
    {
        string root = CreateRoot();
        try
        {
            const string shaderRoot = "Assets/Resources/Shaders/PostProcessing/";
            string includes = "#include \"Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl\"\n" +
                "#include \"Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl\"\n" +
                "#include \"Packages/com.unity.render-pipelines.core/ShaderLibrary/" + includeName + "\"\n";
            Write(root, shaderRoot + "PostProcess.compute", includes);
            Write(root, shaderRoot + "Scopes.compute", includes);
            Write(root, shaderRoot + "ColorGrading.hlsl", "");
            Write(root, "Assets/Scripts/Rendering/PostProcessing/Scopes/ScopesRenderPass.cs", "");
            Write(root, "Assets/Scripts/Rendering/PostProcessing/Pipeline/PostProcessRenderPass.cs", "");

            var violations = await new DisplayTransformRule().EvaluateAsync(
                Array.Empty<AssemblyDefinition>(), Context(root));
            Assert.That(violations.Count(violation => violation.Message.StartsWith(
                "HDR helpers require", StringComparison.Ordinal)), Is.EqualTo(expectedCount));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestCase("passData.HdrOutput ? cameraData.hdrDisplayColorGamut : ColorGamut.sRGB", 0)]
    [TestCase("passData.HDROutput ? cameraData.hdrDisplayColorGamut : ColorGamut.sRGB", 0)]
    [TestCase("HDROutput ? cameraData.hdrDisplayColorGamut : ColorGamut.sRGB", 0)]
    [TestCase("cameraData.hdrDisplayColorGamut", 1)]
    public async Task DisplayRuleRequiresConditionalGamutAccess(string expression, int expectedCount)
    {
        string root = CreateRoot();
        try
        {
            Write(root, "Assets/Scripts/Rendering/PostProcessing/Scopes/ScopesRenderPass.cs",
                "passData.HDRGamut = " + expression + ";");
            var violations = await new DisplayTransformRule().EvaluateAsync(
                Array.Empty<AssemblyDefinition>(), Context(root));
            Assert.That(violations.Count(violation => violation.Message.StartsWith(
                "Read HDR display gamut", StringComparison.Ordinal)), Is.EqualTo(expectedCount));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestCase("CompositeFinal")]
    [TestCase("BloomPrefilter")]
    [TestCase("BloomUpsampleComposite")]
    public async Task DisplayRuleRejectsRetiredBloomKernels(string kernel)
    {
        string root = CreateRoot();
        try
        {
            Write(root, "Assets/Resources/Shaders/PostProcessing/PostProcess.compute",
                "void " + kernel + "() {}\nvoid DisplayFinal() {}");
            var violations = await new DisplayTransformRule().EvaluateAsync(
                Array.Empty<AssemblyDefinition>(), Context(root));
            Assert.That(violations.Count(violation => violation.Message.Contains(
                "retired screen-space bloom", StringComparison.Ordinal)), Is.EqualTo(1));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestCase(true, 0)]
    [TestCase(false, 1)]
    public async Task DisplayRuleRequiresWorldBloomUpsample(bool includeUpsample, int expectedCount)
    {
        string root = CreateRoot();
        try
        {
            Write(root, "Assets/Resources/Shaders/PostProcessing/WorldBloom.compute",
                "void Prefilter() {}\nvoid Downsample() {}\n" + (includeUpsample ? "void Upsample() {}" : ""));
            var violations = await new DisplayTransformRule().EvaluateAsync(
                Array.Empty<AssemblyDefinition>(), Context(root));
            Assert.That(violations.Count(violation => violation.Message.Contains(
                "production kernel Upsample", StringComparison.Ordinal)), Is.EqualTo(expectedCount));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestCase("AfterRenderingPostProcessing", 0)]
    [TestCase("BeforeRenderingPostProcessing", 2)]
    public async Task DisplayRuleRequiresOutputPassAfterToneMapping(string stage, int expectedCount)
    {
        string root = CreateRoot();
        try
        {
            Write(root, "Assets/Scripts/Rendering/PostProcessing/Pipeline/PostProcessRenderPass.cs",
                "renderPassEvent = RenderPassEvent." + stage + ";\n" +
                "renderPassEvent2D = RenderPassEvent2D." + stage + ";");
            var violations = await new DisplayTransformRule().EvaluateAsync(
                Array.Empty<AssemblyDefinition>(), Context(root));
            Assert.That(violations.Count(violation => violation.Message.Contains(
                "must execute after URP tone mapping", StringComparison.Ordinal)), Is.EqualTo(expectedCount));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "Kern.NamingContractTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string root, string relative, string source)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
    }

    private static LinterContext Context(string root) => new()
    {
        ProjectRoot = root,
        AssemblyPaths = Array.Empty<string>(),
        UnityAssemblyPaths = Array.Empty<string>(),
        ExcludePatterns = Array.Empty<string>(),
        IncludedRuleIds = new HashSet<string>(),
    };
}
