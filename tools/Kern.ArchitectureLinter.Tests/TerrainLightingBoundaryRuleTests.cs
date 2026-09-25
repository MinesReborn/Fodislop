#nullable enable

using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Rules.Contracts;
using Mono.Cecil;
using NUnit.Framework;

namespace Kern.ArchitectureLinter.Tests;

[TestFixture]
public sealed class TerrainLightingBoundaryRuleTests
{
    [TestCase("using Kern.World.Lighting.Core; class C { }")]
    [TestCase("using Lighting = global::Kern.World.Lighting.Core; class C { }")]
    [TestCase("using global::Kern.World.Lighting.Core; class C { }")]
    public async Task TerrainRejectsLightingNamespaceImports(string source)
    {
        IReadOnlyList<RuleViolation> violations = await EvaluateAsync(
            "Assets/Scripts/World/Terrain/Fixture.cs", source);

        Assert.That(violations, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task LightingRejectsAliasedTerrainImplementationType()
    {
        IReadOnlyList<RuleViolation> violations = await EvaluateAsync(
            "Assets/Scripts/World/Lighting/Fixture.cs",
            "using Renderer = global::Kern.World.Terrain.TerrainRenderer; class C { }");

        Assert.That(violations, Has.Count.EqualTo(1));
        Assert.That(violations[0].Message, Does.Contain("using directive"));
    }

    [Test]
    public async Task TerrainRejectsAliasedNamespaceChain()
    {
        IReadOnlyList<RuleViolation> violations = await EvaluateAsync(
            "Assets/Scripts/World/Terrain/Fixture.cs",
            "using Lighting = Kern.World.Lighting; using Coordinator = Lighting.Core.LightingUpdateCoordinator; class C { }");

        Assert.That(violations, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task LightingRejectsFullyQualifiedTerrainReference()
    {
        IReadOnlyList<RuleViolation> violations = await EvaluateAsync(
            "Assets/Scripts/World/Lighting/Fixture.cs",
            "class C { global::Kern.World.Terrain.TerrainRenderer? renderer; }");

        Assert.That(violations, Has.Count.EqualTo(1));
        Assert.That(violations[0].Message, Does.Contain("fully qualified"));
    }

    [Test]
    public async Task NeutralContractReferenceIsAllowed()
    {
        IReadOnlyList<RuleViolation> violations = await EvaluateAsync(
            "Assets/Scripts/World/Terrain/Fixture.cs",
            "using Kern.Core.Interfaces.WorldLighting; class C { }");

        Assert.That(violations, Is.Empty);
    }

    private static async Task<IReadOnlyList<RuleViolation>> EvaluateAsync(string relativePath, string source)
    {
        string projectRoot = Path.Combine(Path.GetTempPath(), "Kern.ArchitectureLinter.Tests", Guid.NewGuid().ToString("N"));
        string sourcePath = Path.Combine(projectRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, source);

        try
        {
            var context = new LinterContext
            {
                ProjectRoot = projectRoot,
                AssemblyPaths = Array.Empty<string>(),
                UnityAssemblyPaths = Array.Empty<string>(),
                ExcludePatterns = Array.Empty<string>(),
                IncludedRuleIds = new HashSet<string>(),
            };
            return await new TerrainLightingBoundaryRule().EvaluateAsync(
                Array.Empty<AssemblyDefinition>(), context);
        }
        finally
        {
            Directory.Delete(projectRoot, recursive: true);
        }
    }
}
