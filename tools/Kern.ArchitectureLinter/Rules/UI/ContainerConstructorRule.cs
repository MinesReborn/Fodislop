#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.UI;

/// <summary>
/// VContainer picks the constructor with the most parameters (including non-public).
/// If a type has multiple constructors and none marked [Inject], the container
/// will pick the longest and may fail at runtime.
/// Ported from check-architecture.js checkContainerConstructorChoice().
/// </summary>
public sealed class ContainerConstructorRule : IRule
{
    public string Id => "KERN-CONTAINER-CONSTRUCTOR";
    public string Description => "VContainer constructor choice validation";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    private static readonly Regex RegisterPattern = new(@"builder\s*\.\s*Register<\s*([A-Za-z_][\w]*)\s*>\s*\(\s*Lifetime\.", RegexOptions.Compiled);
    private static readonly Regex InjectPattern = new(@"\[Inject\]", RegexOptions.Compiled);

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;
        var scriptsRoot = Path.Combine(projectRoot, "Assets", "Scripts");

        // Find all registered types
        var registered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in SourceScanner.EnumerateCsFiles(scriptsRoot, "Tests", "Editor", "VContainer"))
        {
            var relative = SourceScanner.GetProjectRelativePath(projectRoot, file);
            var content = File.ReadAllText(file);

            foreach (Match m in RegisterPattern.Matches(content))
            {
                var typeName = m.Groups[1].Value;
                if (!registered.ContainsKey(typeName))
                    registered[typeName] = relative;
            }
        }

        if (registered.Count == 0)
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);

        // Check each registered type
        foreach (var file in SourceScanner.EnumerateCsFiles(scriptsRoot, "Tests", "Editor", "VContainer"))
        {
            var relative = SourceScanner.GetProjectRelativePath(projectRoot, file);
            var content = File.ReadAllText(file);

            foreach (var (typeName, scope) in registered)
            {
                if (!Regex.IsMatch(content, $@"\b(?:class|record)\s+{typeName}\b"))
                    continue;

                var ctorPattern = $@"(?:public|internal|private|protected)(?:\s+(?:sealed|static|unsafe|extern))*\s+{typeName}\s*\(";
                var ctors = Regex.Matches(content, ctorPattern)
                    .Cast<Match>()
                    .Where(m => !m.Value.Contains("static"))
                    .ToList();

                if (ctors.Count < 2)
                    continue;

                // Strip comments for [Inject] check
                var code = SourceScanner.StripComments(content);
                if (InjectPattern.IsMatch(code))
                    continue;

                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"{typeName} регистрируется в {scope} как Register<{typeName}>(Lifetime...), но имеет {ctors.Count} конструктора и ни одного [Inject]: VContainer возьмёт самый длинный и уронит сборку.",
                    Severity = Severity,
                    TypeName = relative
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
