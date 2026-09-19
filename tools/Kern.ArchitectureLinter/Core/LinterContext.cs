using Mono.Cecil;

namespace Kern.ArchitectureLinter.Core;

public sealed class LinterContext
{
    public required string ProjectRoot { get; init; }
    public required IReadOnlyList<string> AssemblyPaths { get; init; }
    public required IReadOnlyList<string> UnityAssemblyPaths { get; init; }
    public required IReadOnlyList<string> ExcludePatterns { get; init; } = Array.Empty<string>();
    public required IReadOnlySet<string> IncludedRuleIds { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public bool EnableSarif { get; init; }
    public string? SarifOutputPath { get; init; }
    public RuleSeverity FailOnSeverity { get; init; } = RuleSeverity.Error;
    public bool Strict { get; init; }

    public bool ShouldExclude(string assemblyName)
    {
        foreach (var pattern in ExcludePatterns)
        {
            if (assemblyName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
