using Mono.Cecil;
using Fodinae.ArchitectureLinter.Core;
using Fodinae.ArchitectureLinter.Scanning;

namespace Fodinae.ArchitectureLinter.Rules;

public sealed class MonoBehaviourAccessRule : IRule
{
    private static readonly string[] LifecycleMethods =
    {
        "Awake", "Start", "Update", "OnEnable", "OnDisable", "OnDestroy",
        "LateUpdate", "FixedUpdate", "OnValidate", "Reset"
    };

    public string Id => "FOD-MONO-ACCESS";
    public string Description => "MonoBehaviour lifecycle method accessibility";
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
                if (!CecilAssemblyScanner.DerivesFrom(type, "UnityEngine.MonoBehaviour"))
                    continue;

                ScanType(type, violations);
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private void ScanType(TypeDefinition type, List<RuleViolation> violations)
    {
        foreach (var method in type.Methods)
        {
            if (!LifecycleMethods.Contains(method.Name))
                continue;

            if (method.IsStatic)
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"Lifecycle method '{method.Name}' in '{type.FullName}' is static.",
                    Severity = Severity,
                    AssemblyName = type.Module.Assembly.Name.Name,
                    TypeName = type.FullName,
                    MemberName = method.Name
                });
            }

            if (method.IsAbstract)
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"Lifecycle method '{method.Name}' in '{type.FullName}' is abstract.",
                    Severity = Severity,
                    AssemblyName = type.Module.Assembly.Name.Name,
                    TypeName = type.FullName,
                    MemberName = method.Name
                });
            }
        }

        foreach (var nested in type.NestedTypes)
            ScanType(nested, violations);
    }
}
