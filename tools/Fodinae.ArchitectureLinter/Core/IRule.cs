using Mono.Cecil;

namespace Fodinae.ArchitectureLinter.Core;

public interface IRule
{
    string Id { get; }
    string Description { get; }
    RuleSeverity Severity { get; }

    Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default);
}
