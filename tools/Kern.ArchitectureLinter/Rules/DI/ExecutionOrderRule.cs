using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.DI;

/// <summary>
/// Validates DefaultExecutionOrder contracts on LifetimeScopes/MapManager.
/// </summary>
public sealed class ExecutionOrderRule : IRule
{
    private static readonly Dictionary<string, int> RequiredExecutionOrders = new(StringComparer.Ordinal)
    {
        ["Kern.Core.BootstrapLifetimeScope"] = -30000,
        ["Kern.Core.GameLifetimeScope"] = -20000,
        ["Kern.World.MapManager"] = -10000,
    };

    public string Id => "KERN-EXECUTION-ORDER";
    public string Description => "DefaultExecutionOrder contract validation";
    public RuleSeverity Severity => RuleSeverity.Error;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var assembly in assemblies)
        {
            if (context.ShouldExclude(assembly.Name.Name))
                continue;

            foreach (var type in assembly.MainModule.Types)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CheckType(type, violations, found);
            }
        }

        foreach (var required in RequiredExecutionOrders)
        {
            if (!found.Contains(required.Key))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"Type '{required.Key}' with DefaultExecutionOrder={required.Value} not found in any loaded assembly.",
                    Severity = Severity,
                    TypeName = required.Key
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private void CheckType(TypeDefinition type, List<RuleViolation> violations, HashSet<string> found)
    {
        if (!RequiredExecutionOrders.ContainsKey(type.FullName))
        {
            foreach (var nested in type.NestedTypes)
                CheckType(nested, violations, found);
            return;
        }

        found.Add(type.FullName);

        var customAttr = type.CustomAttributes.FirstOrDefault(a =>
            a.AttributeType.FullName is
                "UnityEngine.DefaultExecutionOrder" or
                "UnityEngine.DefaultExecutionOrderAttribute");

        if (customAttr == null)
        {
            violations.Add(new RuleViolation
            {
                RuleId = Id,
                Message = $"Type '{type.FullName}' is missing DefaultExecutionOrderAttribute (expected {RequiredExecutionOrders[type.FullName]}).",
                Severity = Severity,
                AssemblyName = type.Module.Assembly.Name.Name,
                TypeName = type.FullName
            });
            return;
        }

        if (customAttr.ConstructorArguments.Count == 0 ||
            customAttr.ConstructorArguments[0].Value is not int order)
        {
            violations.Add(new RuleViolation
            {
                RuleId = Id,
                Message = $"Type '{type.FullName}' has an unreadable DefaultExecutionOrder value.",
                Severity = Severity,
                AssemblyName = type.Module.Assembly.Name.Name,
                TypeName = type.FullName,
            });
            return;
        }

        if (order != RequiredExecutionOrders[type.FullName])
        {
            violations.Add(new RuleViolation
            {
                RuleId = Id,
                Message = $"Type '{type.FullName}' has DefaultExecutionOrder={order}, expected {RequiredExecutionOrders[type.FullName]}.",
                Severity = Severity,
                AssemblyName = type.Module.Assembly.Name.Name,
                TypeName = type.FullName,
            });
        }

        foreach (var nested in type.NestedTypes)
            CheckType(nested, violations, found);
    }
}
