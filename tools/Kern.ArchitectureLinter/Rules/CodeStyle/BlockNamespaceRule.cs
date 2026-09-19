using System.Text.RegularExpressions;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;
using Mono.Cecil;

namespace Kern.ArchitectureLinter.Rules.CodeStyle;

/// <summary>
/// Validates namespace syntax. Unity-inheriting types must use block namespace { }.
/// File-scoped namespace causes MonoScript.GetClass() == null.
/// </summary>
public sealed class BlockNamespaceRule : IRule
{
    private static readonly string[] UnityBaseTypes =
    [
        "UnityEngine.MonoBehaviour",
        "UnityEngine.ScriptableObject",
        "UnityEngine.Rendering.Universal.ScriptableRendererFeature",
        "UnityEngine.Rendering.VolumeComponent",
    ];

    public string Id => "KERN-BLOCK-NAMESPACE";
    public string Description => "Unity-inheriting types must use block namespace (not file-scoped)";
    public RuleSeverity Severity => RuleSeverity.Warning;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, string[]> sourcesByTypeName = SourceScanner
            .EnumerateAllCsFiles(
                Path.Combine(context.ProjectRoot, "Assets", "Scripts"),
                Path.Combine(context.ProjectRoot, "Assets", "Editor"))
            .Select(path => (Path: path, TypeName: Path.GetFileNameWithoutExtension(path)))
            .Where(entry => !string.IsNullOrEmpty(entry.TypeName))
            .GroupBy(entry => entry.TypeName!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(entry => entry.Path)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        var violations = new List<RuleViolation>();

        foreach (AssemblyDefinition assembly in assemblies)
        {
            if (context.ShouldExclude(assembly.Name.Name))
            {
                continue;
            }

            foreach (TypeDefinition type in EnumerateTypes(assembly.MainModule.Types))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsUnityInheritingType(type) || type.IsNested)
                {
                    continue;
                }

                string typeName = RemoveGenericArity(type.Name);
                if (!sourcesByTypeName.TryGetValue(typeName, out string[]? sourcePaths))
                {
                    continue;
                }

                string? sourcePath = sourcePaths.FirstOrDefault(path => DeclaresType(path, typeName));
                if (sourcePath == null)
                {
                    continue;
                }

                string source = SourceScanner.StripComments(File.ReadAllText(sourcePath));
                Match namespaceDeclaration = Regex.Match(
                    source,
                    $@"^\s*namespace\s+{Regex.Escape(type.Namespace)}\s*(?<delimiter>[;{{])",
                    RegexOptions.Multiline | RegexOptions.CultureInvariant);
                if (!namespaceDeclaration.Success ||
                    namespaceDeclaration.Groups["delimiter"].Value != ";")
                {
                    continue;
                }

                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"Type '{type.FullName}' inherits from a Unity type and uses a " +
                              "file-scoped namespace. Use a block namespace so MonoScript.GetClass() remains valid.",
                    Severity = Severity,
                    AssemblyName = SourceScanner.GetProjectRelativePath(context.ProjectRoot, sourcePath),
                    TypeName = type.FullName,
                    Line = CountLine(source, namespaceDeclaration.Index),
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private static IEnumerable<TypeDefinition> EnumerateTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (TypeDefinition type in types)
        {
            yield return type;
            foreach (TypeDefinition nestedType in EnumerateTypes(type.NestedTypes))
            {
                yield return nestedType;
            }
        }
    }

    private static bool IsUnityInheritingType(TypeDefinition type)
    {
        return UnityBaseTypes.Any(unityBase => CecilAssemblyScanner.DerivesFrom(type, unityBase));
    }

    private static bool DeclaresType(string path, string typeName)
    {
        string source = SourceScanner.StripComments(File.ReadAllText(path));
        return Regex.IsMatch(
            source,
            $@"\b(?:class|record\s+class)\s+{Regex.Escape(typeName)}\b",
            RegexOptions.CultureInvariant);
    }

    private static string RemoveGenericArity(string typeName)
    {
        int separator = typeName.IndexOf('`');
        return separator < 0 ? typeName : typeName[..separator];
    }

    private static int CountLine(string source, int index)
    {
        return source.AsSpan(0, index).Count('\n') + 1;
    }
}
