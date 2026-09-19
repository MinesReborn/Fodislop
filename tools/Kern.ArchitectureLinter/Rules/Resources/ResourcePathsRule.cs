using Kern.ArchitectureLinter.Core;
using Mono.Cecil;
using System.Text.RegularExpressions;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Resources;

/// <summary>
/// Validates that every ResourcePaths constant resolves to an existing asset.
/// A broken path gives neither compile nor runtime error.
/// </summary>
public sealed class ResourcePathsRule : IRule
{
    private const string ContractsPath = "Assets/Scripts/Core/Interfaces/Contracts/ProjectRuntimeContracts.cs";

    public string Id => "KERN-RESOURCE-PATHS";
    public string Description => "ResourcePaths validation";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;
        var src = File.ReadAllText(Path.Combine(projectRoot, ContractsPath));
        if (src == null)
        {
            violations.Add(new RuleViolation { RuleId = Id, Message = "ProjectRuntimeContracts.cs not found.", Severity = Severity, AssemblyName = ContractsPath });
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
        }

        var block = Regex.Match(src, @"class ResourcePaths\s*\{([\s\S]*?)\n\s*\}");
        if (!block.Success)
        {
            violations.Add(new RuleViolation { RuleId = Id, Message = "ResourcePaths class not found.", Severity = Severity, AssemblyName = ContractsPath });
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
        }

        var paths = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(block.Groups[1].Value, @"public const string (\w+)\s*=\s*""([^""]*)"""))
            paths[m.Groups[1].Value] = m.Groups[2].Value;

        foreach (Match m in Regex.Matches(block.Groups[1].Value, @"public const string (\w+)\s*=\s*(\w+)\s*\+\s*""([^""]*)""\s*\+\s*(\w+)"))
        {
            if (paths.TryGetValue(m.Groups[2].Value, out var left) && paths.TryGetValue(m.Groups[4].Value, out var right))
                paths[m.Groups[1].Value] = left + m.Groups[3].Value + right;
        }

        var roots = new List<string>();
        foreach (var dir in Directory.EnumerateDirectories(Path.Combine(projectRoot, "Assets"), "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(dir).Equals("Resources", StringComparison.OrdinalIgnoreCase))
                roots.Add(dir);
        }

        var extensions = new[] { "", ".asset", ".prefab", ".uxml", ".uss", ".compute", ".shader", ".png", ".jpg", ".mat", ".ttf", ".otf", ".json", ".txt", ".anim", ".controller" };
        foreach (var (name, value) in paths)
        {
            if (string.IsNullOrEmpty(value)) continue;
            var found = roots.Any(root => extensions.Any(ext => File.Exists(Path.Combine(root, value + ext))));
            if (!found)
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"ResourcePaths.{name} = \"{value}\" не указывает ни на один существующий ассет. Resources.Load вернёт null молча.",
                    Severity = Severity,
                    AssemblyName = ContractsPath,
                    TypeName = name
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
