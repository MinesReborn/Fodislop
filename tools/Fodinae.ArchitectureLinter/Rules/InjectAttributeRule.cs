using Mono.Cecil;
using Fodinae.ArchitectureLinter.Core;
using Fodinae.ArchitectureLinter.Scanning;

namespace Fodinae.ArchitectureLinter.Rules;

public sealed class InjectAttributeRule : IRule
{
    private static readonly string InjectAttribute = "VContainer.InjectAttribute";

    public string Id => "FOD-INJECT-ATTRIBUTE";
    public string Description => "[Inject] attribute usage validation";
    public RuleSeverity Severity => RuleSeverity.Error;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();

        foreach (var assembly in assemblies)
        {
            if (context.ShouldExclude(assembly.Name.Name))
                continue;

            foreach (var type in assembly.MainModule.Types)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ScanType(type, violations);
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private void ScanType(TypeDefinition type, List<RuleViolation> violations)
    {
        foreach (var field in type.Fields)
        {
            if (!CecilAssemblyScanner.HasAttribute(field, InjectAttribute))
                continue;

            if (field.IsStatic)
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"Field '{field.Name}' in '{type.FullName}' is marked [Inject] but is static. [Inject] fields must not be static.",
                    Severity = Severity,
                    AssemblyName = type.Module.Assembly.Name.Name,
                    TypeName = type.FullName,
                    MemberName = field.Name
                });
            }
        }

        foreach (var nested in type.NestedTypes)
            ScanType(nested, violations);
    }
}
