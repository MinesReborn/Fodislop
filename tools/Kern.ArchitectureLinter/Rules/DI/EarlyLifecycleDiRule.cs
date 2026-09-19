#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.DI;

/// <summary>
/// [Inject] fields must not be accessed in Awake/OnEnable without a null check.
/// Calling Resolve&lt;T&gt;() in Awake/OnEnable is forbidden — use TryResolve&lt;T&gt;().
/// Ported from check-architecture.js checkEarlyLifecycleDiAndCallgraph().
/// </summary>
public sealed class EarlyLifecycleDiRule : IRule
{
    public string Id => "KERN-EARLY-LIFECYCLE-DI";
    public string Description => "Early lifecycle DI access validation";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    private static readonly string[] EntryPoints = { "Awake", "OnEnable" };
    private static readonly Regex InjectField = new(@"\[Inject\]\s*(?:private|protected|public)?\s*[A-Za-z0-9_<>?]+\s+([_A-Za-z0-9]+)\s*(=|;)", RegexOptions.Compiled);
    private static readonly Regex MethodPattern = new(@"(?:private|protected|public|internal)?\s*(?:override|virtual|static)?\s*(?:void|bool|int|string|Task|UniTask|UniTaskVoid)\s+([A-Za-z0-9_]+)\s*\([^)]*\)\s*\{", RegexOptions.Compiled);

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

            // Find all [Inject] fields
            var fieldNames = new HashSet<string>();
            foreach (Match m in InjectField.Matches(content))
                fieldNames.Add(m.Groups[1].Value);

            if (fieldNames.Count == 0) continue;

            // Parse methods and build call graph
            var methods = new Dictionary<string, string>();
            foreach (Match m in MethodPattern.Matches(content))
            {
                var name = m.Groups[1].Value;
                var start = m.Index + m.Length;
                var depth = 1;
                var end = start;
                while (end < content.Length && depth > 0)
                {
                    if (content[end] == '{') depth++;
                    else if (content[end] == '}') depth--;
                    end++;
                }
                methods[name] = content[start..(end - 1)];
            }

            // Trace call graph from Awake/OnEnable
            foreach (var entry in EntryPoints)
            {
                if (!methods.ContainsKey(entry)) continue;

                var visited = new HashSet<string> { entry };
                var queue = new Queue<string>();
                queue.Enqueue(entry);

                while (queue.Count > 0)
                {
                    var curr = queue.Dequeue();
                    if (!methods.TryGetValue(curr, out var body)) continue;

                    // Strip lambdas and callbacks
                    var cleanBody = Regex.Replace(body, @"=>\s*\{[^}]*\}", "=> {}");
                    cleanBody = Regex.Replace(cleanBody, @"RegisterCallback<[^>]+>\s*\([^)]*\)", "");

                    foreach (var other in methods.Keys)
                    {
                        if (visited.Contains(other) || other == entry) continue;

                        var found = false;
                        foreach (var rawLine in cleanBody.Split('\n'))
                        {
                            var line = rawLine.Trim();
                            if (line.Contains("+=") || line.Contains("-=") || line.Contains("=>")) continue;
                            if (Regex.IsMatch(line, $@"\b{Regex.Escape(other)}\s*\("))
                            {
                                found = true;
                                break;
                            }
                        }

                        if (found)
                        {
                            visited.Add(other);
                            queue.Enqueue(other);
                        }
                    }
                }

                // Check reached methods
                foreach (var reached in visited)
                {
                    if (!methods.TryGetValue(reached, out var body)) continue;
                    var norm = Regex.Replace(body, @"\s+", " ");

                    // Check for Resolve<T>() calls
                    if (Regex.IsMatch(norm, @"\b(?:Session|_session)\.Resolve"))
                    {
                        violations.Add(new RuleViolation
                        {
                            RuleId = Id,
                            Message = $"Вызов Resolve<T>() в {entry}() -> {reached}() запрещён. Используйте TryResolve<T>() с null-guard.",
                            Severity = Severity,
                            TypeName = $"{relative}:{reached}"
                        });
                    }

                    // Check for unguarded [Inject] field access
                    foreach (var fn in fieldNames)
                    {
                        if (!Regex.IsMatch(body, $@"\b{Regex.Escape(fn)}\s*[\.\(\[]"))
                            continue;

                        var hasGuard =
                            (Regex.IsMatch(body, $@"if\s*\([^)]*\b{Regex.Escape(fn)}\s*==\s*null") && body.Contains("return")) ||
                            Regex.IsMatch(body, $@"if\s*\([^)]*\b{Regex.Escape(fn)}\s*!=\s*null") ||
                            Regex.IsMatch(body, $@"if\s*\([^)]*\b{Regex.Escape(fn)}\s*is\s+not\s+null") ||
                            Regex.IsMatch(norm, $@"\b{Regex.Escape(fn)}\s*!=\s*null\s*\?") ||
                            Regex.IsMatch(body, $@"\b{Regex.Escape(fn)}\s*\?\.") ||
                            Regex.IsMatch(body, $@"if\s*\([^)]*_isInitialized[^)]*\)\s*\{{[^}}]*\b{Regex.Escape(fn)}\b");

                        if (!hasGuard)
                        {
                            violations.Add(new RuleViolation
                            {
                                RuleId = Id,
                                Message = $"Поле '{fn}' используется в {entry}() -> {reached}() без null-check.",
                                Severity = Severity,
                                TypeName = $"{relative}:{reached}"
                            });
                        }
                    }
                }
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
