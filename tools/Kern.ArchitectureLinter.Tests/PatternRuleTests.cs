#nullable enable

using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Rules.Forbidden;
using Mono.Cecil;
using NUnit.Framework;

namespace Kern.ArchitectureLinter.Tests;

[TestFixture]
public sealed class PatternRuleTests
{
    [Test]
    public async Task BootstrapCompositionRootPatternsRemainAllowedAfterScopeRelocation()
    {
        string projectRoot = CreateProjectRoot();
        try
        {
            await WriteSourceAsync(
                projectRoot,
                "Assets/Scripts/Core/Bootstrap/Scopes/BootstrapLifetimeScope.cs",
                "class BootstrapLifetimeScope { void Awake() { DontDestroyOnLoad(gameObject); } }");
            await WriteSourceAsync(
                projectRoot,
                "Assets/Scripts/Core/Bootstrap/Scopes/GameLifetimeScope.cs",
                "class GameLifetimeScope { void Stop() { Container.TryResolve(out object dependency); } }");

            IReadOnlyList<RuleViolation> violations = await EvaluateAsync(projectRoot);

            Assert.That(violations, Is.Empty);
        }
        finally
        {
            Directory.Delete(projectRoot, recursive: true);
        }
    }

    [Test]
    public async Task TryResolveOutsideCompositionRootStillFails()
    {
        string projectRoot = CreateProjectRoot();
        try
        {
            await WriteSourceAsync(
                projectRoot,
                "Assets/Scripts/World/Feature.cs",
                "class Feature { void Stop() { Container.TryResolve(out object dependency); } }");

            IReadOnlyList<RuleViolation> violations = await EvaluateAsync(projectRoot);

            Assert.That(violations, Has.Count.EqualTo(1));
            Assert.That(violations[0].Message, Does.Contain("DI fallback resolution"));
        }
        finally
        {
            Directory.Delete(projectRoot, recursive: true);
        }
    }

    private static string CreateProjectRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "Kern.ArchitectureLinter.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task WriteSourceAsync(string projectRoot, string relativePath, string source)
    {
        string path = Path.Combine(projectRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, source);
    }

    private static Task<IReadOnlyList<RuleViolation>> EvaluateAsync(string projectRoot)
    {
        var context = new LinterContext
        {
            ProjectRoot = projectRoot,
            AssemblyPaths = Array.Empty<string>(),
            UnityAssemblyPaths = Array.Empty<string>(),
            ExcludePatterns = Array.Empty<string>(),
            IncludedRuleIds = new HashSet<string>(),
        };

        return new PatternRule().EvaluateAsync(Array.Empty<AssemblyDefinition>(), context);
    }
}
