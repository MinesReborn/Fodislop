using Mono.Cecil;

namespace Kern.ArchitectureLinter.Core;

public interface IRule
{
    string Id { get; }
    string Description { get; }
    RuleSeverity Severity { get; }
    bool RequiresAssemblies => true;

    Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default);
}
