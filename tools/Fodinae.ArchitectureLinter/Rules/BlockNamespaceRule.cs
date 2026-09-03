using Mono.Cecil;
using Fodinae.ArchitectureLinter.Core;
using Fodinae.ArchitectureLinter.Scanning;

namespace Fodinae.ArchitectureLinter.Rules;

public sealed class BlockNamespaceRule : IRule
{
    private static readonly string[] UnityBaseTypes =
    {
        "UnityEngine.MonoBehaviour",
        "UnityEngine.ScriptableObject",
        "UnityEngine.ScriptableRendererFeature",
        "UnityEngine.VolumeComponent",
        "UnityEngine.Object"
    };

    public string Id => "FOD-BLOCK-NAMESPACE";
    public string Description => "Unity-inheriting types must use block namespace (not file-scoped)";
    public RuleSeverity Severity => RuleSeverity.Warning;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var namespaceTypes = new Dictionary<string, List<TypeDefinition>>(StringComparer.Ordinal);
        var candidates = new List<TypeDefinition>();

        foreach (var assembly in assemblies)
        {
            if (context.ShouldExclude(assembly.Name.Name))
                continue;

            foreach (var type in assembly.MainModule.Types)
            {
                cancellationToken.ThrowIfCancellationRequested();

                CollectCandidates(type, candidates);

                foreach (var candidate in candidates)
                {
                    var ns = candidate.Namespace ?? string.Empty;
                    if (!namespaceTypes.TryGetValue(ns, out var list))
                    {
                        list = new List<TypeDefinition>();
                        namespaceTypes[ns] = list;
                    }
                    if (!list.Contains(candidate))
                    {
                        list.Add(candidate);
                    }
                }
                candidates.Clear();
            }
        }

        var violations = new List<RuleViolation>();

        foreach (var (ns, types) in namespaceTypes)
        {
            if (types.Count > 1)
                continue;

            var type = types[0];
            violations.Add(new RuleViolation
            {
                RuleId = Id,
                Message = $"Type '{type.FullName}' inherits from a Unity type but is the only type in namespace " +
                          $"'{ns}'. It is likely declared with a file-scoped namespace. " +
                          "Unity-inheriting types must use a block namespace; otherwise MonoScript.GetClass() may return null.",
                Severity = Severity,
                AssemblyName = type.Module.Assembly.Name.Name,
                TypeName = type.FullName
            });
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private void CollectCandidates(TypeDefinition type, List<TypeDefinition> candidates)
    {
        if (IsUnityInheritingType(type))
        {
            candidates.Add(type);
        }

        foreach (var nested in type.NestedTypes)
        {
            CollectCandidates(nested, candidates);
        }
    }

    private bool IsUnityInheritingType(TypeDefinition type)
    {
        foreach (var unityBase in UnityBaseTypes)
        {
            if (CecilAssemblyScanner.DerivesFrom(type, unityBase))
                return true;
        }
        return false;
    }
}
