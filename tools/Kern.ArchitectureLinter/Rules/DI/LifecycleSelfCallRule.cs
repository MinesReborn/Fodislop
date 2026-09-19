#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.DI;

/// <summary>
/// Lifecycle methods (Awake, OnEnable, Start, OnDisable, OnDestroy) must not
/// call themselves manually from their own lifecycle logic.
/// Ported from check-architecture.js checkLifecycleSelfCalls().
/// </summary>
public sealed class LifecycleSelfCallRule : IRule
{
    public string Id => "KERN-LIFECYCLE-SELF-CALL";
    public string Description => "Lifecycle methods must not call themselves";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    private static readonly string[] LifecycleMethods = { "Awake", "OnEnable", "Start", "OnDisable", "OnDestroy" };

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var scriptsRoot = Path.Combine(context.ProjectRoot, "Assets", "Scripts");

        foreach (var file in SourceScanner.EnumerateCsFiles(scriptsRoot, "Tests", "Editor", "VContainer"))
        {
            var content = File.ReadAllText(file);
            var relative = SourceScanner.GetProjectRelativePath(context.ProjectRoot, file);

            foreach (var methodName in LifecycleMethods)
            {
                // Find method body
                var methodPattern = $"(?:void|UniTask|UniTaskVoid)\\s+{methodName}\\s*\\([^)]*\\)\\s*{{";
                foreach (Match m in Regex.Matches(content, methodPattern))
                {
                    // Extract method body (brace-matched)
                    var start = m.Index + m.Length;
                    var depth = 1;
                    var end = start;
                    while (end < content.Length && depth > 0)
                    {
                        if (content[end] == '{') depth++;
                        else if (content[end] == '}') depth--;
                        end++;
                    }
                    var body = content[start..(end - 1)];

                    // Check for self-call (not base.Method())
                    if (Regex.IsMatch(body, $@"(?<!base\.)\b{methodName}\s*\(\s*\)"))
                    {
                        var line = content.Substring(0, m.Index).Count(c => c == '\n') + 1;
                        violations.Add(new RuleViolation
                        {
                            RuleId = Id,
                            Message = $"{methodName}() не должен вызываться вручную из своей же lifecycle логики. Используйте явный метод инициализации.",
                            Severity = Severity,
                            TypeName = $"{relative}:{line}"
                        });
                    }
                }
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
