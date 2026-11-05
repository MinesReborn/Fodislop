using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Rules.Forbidden;
using NUnit.Framework;

namespace Kern.ArchitectureLinter.Tests;

[TestFixture]
public sealed class ForbiddenApiOwnershipTests
{
    [TestCase("Game/Entities/Robots/Creation/Robot.cs", "new GameObject(\"robot\");", true)]
    [TestCase("Tests/Editor/Fixtures/Objects/Robot.cs", "new GameObject(\"robot\");", false)]
    [TestCase("Tests/Editor/Fixtures/Objects/Robot.cs", "new GameObject(\"robot\"); var camera = Camera.main;", true)]
    public async Task FixtureConstructionIsAllowedWhileRuntimeAndOtherApisRemainChecked(string path, string source, bool rejects)
    {
        string root = Path.Combine(Path.GetTempPath(), "Kern.ApiOwnership", Guid.NewGuid().ToString("N"));
        try
        {
            string file = Path.Combine(root, "Assets/Scripts", path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, source);
            var context = new LinterContext
            {
                ProjectRoot = root,
                AssemblyPaths = [],
                UnityAssemblyPaths = [],
                ExcludePatterns = [],
                IncludedRuleIds = new HashSet<string>(),
            };
            var violations = await new ForbiddenApiRule().EvaluateAsync([], context);
            Assert.That(violations.Count > 0, Is.EqualTo(rejects));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
