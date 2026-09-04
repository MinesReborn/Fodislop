using Mono.Cecil;
using Fodinae.ArchitectureLinter.Core;
using Fodinae.ArchitectureLinter.Scanning;

namespace Fodinae.ArchitectureLinter.Rules;

public sealed class NamingConventionRule : IRule
{
    public string Id => "FOD-NAMING";
    public string Description => "Naming convention validation";
    public RuleSeverity Severity => RuleSeverity.Warning;

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
        if (type.IsInterface && !type.Name.StartsWith("I"))
        {
            violations.Add(new RuleViolation
            {
                RuleId = Id,
                Message = $"Interface '{type.FullName}' does not start with 'I'.",
                Severity = Severity,
                AssemblyName = type.Module.Assembly.Name.Name,
                TypeName = type.FullName
            });
        }

        foreach (var field in type.Fields)
        {
            if (field.IsPrivate && !field.Name.StartsWith("_") && !IsCompilerGenerated(field))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"Private field '{field.Name}' in '{type.FullName}' does not start with '_'.",
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

    private static bool IsCompilerGenerated(FieldDefinition field)
    {
        if (field.Name.StartsWith("<"))
            return true;

        if (field.CustomAttributes.Any(a =>
            a.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute"))
            return true;

        return false;
    }
}
