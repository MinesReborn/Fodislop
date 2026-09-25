#nullable enable

using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Rules.Localization;
using Mono.Cecil;
using NUnit.Framework;

namespace Kern.ArchitectureLinter.Tests;

[TestFixture]
public sealed class LocalizationRuleTests
{
    [Test]
    public async Task NestedUxmlTextReferencesAreCountedAsLocalizationUsage()
    {
        string projectRoot = CreateProjectRoot();
        try
        {
            string localizationDirectory = Path.Combine(projectRoot, "Assets/Resources/Localization");
            string nestedUiDirectory = Path.Combine(projectRoot, "Assets/Resources/UI/Menus");
            Directory.CreateDirectory(localizationDirectory);
            Directory.CreateDirectory(nestedUiDirectory);

            const string key = "mainmenu.settings_lighting";
            await File.WriteAllTextAsync(
                Path.Combine(localizationDirectory, "en.json"),
                "{ \"mainmenu.settings_lighting\": \"Lighting\" }");
            await File.WriteAllTextAsync(
                Path.Combine(localizationDirectory, "ru.json"),
                "{ \"mainmenu.settings_lighting\": \"Освещение\" }");
            await File.WriteAllTextAsync(
                Path.Combine(nestedUiDirectory, "MainMenu.uxml"),
                "<ui:Label text=\"mainmenu.settings_lighting\" />");

            IReadOnlyList<RuleViolation> violations = await EvaluateAsync(projectRoot);

            Assert.That(violations.Any(violation =>
                violation.Message.Contains($"Dead key '{key}'", StringComparison.Ordinal)), Is.False);
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

        return new LocalizationRule().EvaluateAsync(Array.Empty<AssemblyDefinition>(), context);
    }
}
