using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Rules.Repository;
using Mono.Cecil;
using NUnit.Framework;

namespace Kern.ArchitectureLinter.Tests;

[TestFixture]
public sealed class RepositoryTaxonomyRuleTests
{
    [TestCase("World/Coordinates/Conversion/WorldCoordinateConverter.cs", "class WorldCoordinateConverter {}", false)]
    [TestCase("World/Common/Coordinates/WorldCoordinateConverter.cs", "class WorldCoordinateConverter {}", true)]
    [TestCase("World/Coordinates/Conversion/CoordinateUtils.cs", "class CoordinateUtils {}", true)]
    [TestCase("Diagnostics/Contracts/Reports/ReportWriter.cs", "class ReportWriter { void Write() { File.WriteAllText(path, text); } }", true)]
    [TestCase("Diagnostics/Artifacts/Reports/ReportWriter.cs", "class ReportWriter { void Write() { File.WriteAllText(path, text); } }", false)]
    public async Task CategoriesDescribeResponsibilityAndContractsRemainNeutral(string path, string source, bool rejects)
    {
        string root = Path.Combine(Path.GetTempPath(), "Kern.RepositoryTaxonomy", Guid.NewGuid().ToString("N"));
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
            IReadOnlyList<RuleViolation> violations = await new RepositoryTaxonomyRule()
                .EvaluateAsync([], context);
            Assert.That(violations.Count > 0, Is.EqualTo(rejects));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
