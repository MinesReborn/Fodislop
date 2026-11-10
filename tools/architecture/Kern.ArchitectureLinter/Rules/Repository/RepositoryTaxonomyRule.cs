using System.Text.RegularExpressions;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;
using Mono.Cecil;

namespace Kern.ArchitectureLinter.Rules.Repository;

public sealed class RepositoryTaxonomyRule : IRule
{
    private static readonly HashSet<string> UnownedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Common", "Managers", "Utils", "Utilities", "Helpers", "Misc", "Support",
    };

    public string Id => "KERN-REPOSITORY-TAXONOMY";
    public string Description => "Source categories, singleton folders, and neutral contract ownership";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        string root = Path.Combine(context.ProjectRoot, "Assets/Scripts");
        if (!Directory.Exists(root))
        {
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
        }

        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            string[] segments = relative.Split('/');
            if (segments[0] == "VContainer")
            {
                continue;
            }

            string owner = Path.GetDirectoryName(file)!;
            string? unowned = segments.SkipLast(1).FirstOrDefault(UnownedCategories.Contains);
            if (unowned != null)
            {
                Add(violations, relative, $"Category '{unowned}' does not identify the owning responsibility.");
            }

            if (segments.Length < 4)
            {
                Add(violations, relative, "Place the source in a module, feature, and responsibility folder, including singleton files.");
            }

            if (Directory.EnumerateDirectories(owner).Any())
            {
                Add(violations, relative, "This category contains both source files and child categories; give the source its own responsibility folder.");
            }

            if (Regex.IsMatch(Path.GetFileNameWithoutExtension(file), @"(?:Utils|Utilities|Utility|Helpers?|Support)$"))
            {
                Add(violations, relative, "Name this source for its operation and owner instead of a utility/support role.");
            }

            if (segments.Contains("Contracts", StringComparer.Ordinal) &&
                Regex.IsMatch(CSharpSourceText.StripComments(File.ReadAllText(file)), @"\b(?:File\.(?:Write|Append|Delete)|Directory\.(?:Create|Delete)|Time\.(?:time|unscaledTime|frameCount)|Debug\.Log)"))
            {
                Add(violations, relative, "A neutral contract contains runtime I/O, timing, or logging; place that implementation with its runtime owner.");
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private void Add(ICollection<RuleViolation> violations, string file, string message)
    {
        violations.Add(new RuleViolation
        {
            RuleId = Id,
            Message = message,
            Severity = Severity,
            AssemblyName = "Assets/Scripts/" + file,
            TypeName = file,
        });
    }
}
