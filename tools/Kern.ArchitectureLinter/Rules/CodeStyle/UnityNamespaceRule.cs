#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.CodeStyle;

/// <summary>
/// Validates that classes inheriting from Unity types (MonoBehaviour, ScriptableObject,
/// VolumeComponent, ScriptableRendererFeature) use block-scoped namespace syntax,
/// not file-scoped. File-scoped namespace causes MonoScript.GetClass() == null.
/// Ported from check-architecture.js checkUnityNamespaces().
/// </summary>
public sealed class UnityNamespaceRule : IRule
{
    public string Id => "KERN-UNITY-NAMESPACE";
    public string Description => "Unity types must use block namespace";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    private static readonly Regex UnityBaseClass = new(
        @"\bclass\s+[A-Za-z0-9_]+\s*:[^{]*\b(?:MonoBehaviour|ScriptableObject|VolumeComponent|ScriptableRendererFeature)\b",
        RegexOptions.Compiled);

    private static readonly Regex FileScopedNamespace = new(
        @"^\s*namespace\s+[A-Za-z0-9_.]+\s*;",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var scriptsRoot = Path.Combine(context.ProjectRoot, "Assets", "Scripts");

        foreach (var file in SourceScanner.EnumerateAllCsFiles(scriptsRoot))
        {
            var relative = SourceScanner.GetProjectRelativePath(context.ProjectRoot, file);
            if (IsExcluded(relative)) continue;

            var source = File.ReadAllText(file);

            if (!UnityBaseClass.IsMatch(source))
                continue;

            if (FileScopedNamespace.IsMatch(source))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "Класс наследуется от Unity-типа, но использует file-scoped namespace. Используйте block namespace { } чтобы избежать MonoScript.GetClass() == null.",
                    Severity = Severity,
                    AssemblyName = null,
                    TypeName = relative,
                    MemberName = null
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private static bool IsExcluded(string relative)
    {
        return relative.StartsWith("Assets/Scripts/Tests/") ||
               relative.StartsWith("Assets/Scripts/VContainer/") ||
               relative.StartsWith("Assets/Plugins/");
    }
}
