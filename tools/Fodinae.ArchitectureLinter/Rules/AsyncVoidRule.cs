using Mono.Cecil;
using Mono.Cecil.Cil;
using Fodinae.ArchitectureLinter.Core;
using Fodinae.ArchitectureLinter.Scanning;

namespace Fodinae.ArchitectureLinter.Rules;

public sealed class AsyncVoidRule : IRule
{
    private static readonly string[] AllowedAsyncVoidContainers =
    {
        "UnityEngine.MonoBehaviour",
        "UnityEditor.EditorWindow",
        "UnityEditor.Editor"
    };

    public string Id => "FOD-ASYNC-VOID";
    public string Description => "async void methods outside event handlers in MonoBehaviours";
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
        var isAllowedContainer = AllowedAsyncVoidContainers.Any(baseName =>
            CecilAssemblyScanner.DerivesFrom(type, baseName));

        if (!isAllowedContainer)
            return;

        foreach (var method in type.Methods)
        {
            if (!method.HasBody)
                continue;
            if (method.ReturnType.FullName != "System.Void")
                continue;
            if (!IsAsyncMethod(method))
                continue;
            if (IsAllowedEventHandler(method))
                continue;

            violations.Add(new RuleViolation
            {
                RuleId = Id,
                Message = $"Method '{method.Name}' in '{type.FullName}' returns void and is async. " +
                          "Use async Task or move to an event handler (e.g. *Click, On*).",
                Severity = Severity,
                AssemblyName = type.Module.Assembly.Name.Name,
                TypeName = type.FullName,
                MemberName = method.Name
            });
        }

        foreach (var nested in type.NestedTypes)
            ScanType(nested, violations);
    }

    private static bool IsAsyncMethod(MethodDefinition method)
    {
        if (!method.HasBody)
            return false;

        return method.Body.Instructions.Any(i =>
            i.OpCode == OpCodes.Call &&
            i.Operand is MethodReference mr &&
            mr.DeclaringType.FullName == "System.Runtime.CompilerServices.AsyncTaskMethodBuilder" &&
            mr.Name == "Create");
    }

    private static bool IsAllowedEventHandler(MethodDefinition method)
    {
        var name = method.Name;
        return name.EndsWith("Click") || name.StartsWith("On") || name.EndsWith("Event");
    }
}
