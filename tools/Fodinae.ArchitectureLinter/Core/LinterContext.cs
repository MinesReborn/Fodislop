using Mono.Cecil;

namespace Fodinae.ArchitectureLinter.Core;

public sealed class LinterContext
{
    public required string ProjectRoot { get; init; }
    public required IReadOnlyList<string> AssemblyPaths { get; init; }
    public required IReadOnlyList<string> UnityAssemblyPaths { get; init; }
    public required IReadOnlyList<string> ExcludePatterns { get; init; } = Array.Empty<string>();
    public bool EnableSarif { get; init; }
    public string? SarifOutputPath { get; init; }
    public int FailOnSeverity { get; init; } = (int)RuleSeverity.Error;
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
