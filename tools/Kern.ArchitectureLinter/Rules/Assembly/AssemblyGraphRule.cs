using Kern.ArchitectureLinter.Core;
using Mono.Cecil;
using System.Text.RegularExpressions;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Assembly;

/// <summary>
/// Validates assembly graph structure and namespace visibility.
/// Detects cross-assembly references without proper using directives.
/// </summary>
public sealed class AssemblyGraphRule : IRule
{
    private static readonly string[] AssemblyPrimitives =
    {
        "string", "int", "float", "double", "bool", "byte", "long", "short", "char",
        "decimal", "uint", "ulong", "ushort", "sbyte", "object", "var", "const",
        "enum", "namespace"
    };

    private static readonly Regex TypeDeclarationRegex = new Regex(
        @"\b(?:public|internal)\s+" +
        @"(?:readonly\s+|sealed\s+|abstract\s+|static\s+|partial\s+|unsafe\s+|new\s+)*" +
        @"(?:class|struct|interface|enum|record(?:\s+struct)?)\s+([A-Z]\w*)",
        RegexOptions.Compiled);

    private static readonly Regex TypeReferenceRegex = new Regex(@"(?:\w+|\.)?\s*\b([A-Z]\w*)\b(\s*=(?!=))?", RegexOptions.Compiled);

    public string Id => "KERN-ASSEMBLY-GRAPH";
    public string Description => "Assembly graph and namespace visibility";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;

        // Collect asmdef assemblies
        var asmdefs = new List<(string Name, string Dir, List<string> Refs)>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(projectRoot, "Assets", "Scripts"), "*.asmdef", SearchOption.AllDirectories))
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(file))!;
                var name = json.GetValueOrDefault("name")?.ToString() ?? Path.GetFileNameWithoutExtension(file);
                var refs = new List<string>();
                if (json.TryGetValue("references", out var refsObj) && refsObj is System.Text.Json.JsonElement refsEl && refsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var r in refsEl.EnumerateArray())
                        refs.Add(r.GetString() ?? "");
                }
                asmdefs.Add((name, Path.GetDirectoryName(file)!, refs));
            }
            catch (System.Exception) { }
        }

        if (asmdefs.Count < 2)
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);

        // Cycle detection
        var graph = asmdefs.ToDictionary(a => a.Name, a => new HashSet<string>(a.Refs, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recursionStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();

        foreach (var node in graph.Keys)
        {
            if (visited.Contains(node)) continue;
            Dfs(node, graph, visited, recursionStack, path, violations, Id);
        }

        // Namespace visibility via source scan
        var scriptsRoot = Path.Combine(projectRoot, "Assets", "Scripts");
        if (Directory.Exists(scriptsRoot))
        {
            var sources = new Dictionary<string, (string Source, HashSet<string> Types, HashSet<string> Usings)>();
            foreach (var file in SourceScanner.EnumerateCsFiles(scriptsRoot, "Tests", "Plugins", "VContainer", "Editor"))
            {
                var relative = SourceScanner.GetProjectRelativePath(projectRoot, file);
                var source = File.ReadAllText(file);
                var types = new HashSet<string>();
                foreach (Match m in TypeDeclarationRegex.Matches(source))
                    types.Add(m.Groups[1].Value);

                var usings = new HashSet<string>();
                foreach (Match m in Regex.Matches(source, @"^\s*using\s+(?:static\s+)?([\w.]+)\s*;", RegexOptions.Multiline))
                    usings.Add(m.Groups[1].Value);
                foreach (Match m in Regex.Matches(source, @"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline))
                {
                    var ns = m.Groups[1].Value;
                    usings.Add(ns);
                    var parts = ns.Split('.');
                    for (var i = 1; i <= parts.Length; i++)
                        usings.Add(string.Join(".", parts.Take(i)));
                }

                sources[relative] = (source, types, usings);
            }

            var declaredIn = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var (file, (_, types, _)) in sources)
            {
                foreach (var type in types)
                {
                    if (!declaredIn.ContainsKey(type))
                        declaredIn[type] = new HashSet<string>();
                    declaredIn[type].Add(file);
                }
            }

            foreach (var (file, (source, _, usings)) in sources)
            {
                var scanned = Regex.Replace(source, @"/\*[\s\S]*?\*/", " ");
                scanned = Regex.Replace(scanned, @"//[^\n]*", " ");
                scanned = Regex.Replace(source, @"""[^""]*""", "\"\"");
                scanned = Regex.Replace(scanned, @"\benum\s+[A-Za-z0-9_]+\s*\{[\s\S]*?\}", " ");
                scanned = Regex.Replace(scanned, @"\[[\s\S]*?\]", " ");

                var reported = new HashSet<string>();
                foreach (Match m in TypeReferenceRegex.Matches(scanned))
                {
                    var before = m.Groups[1].Value;
                    var type = m.Groups[2].Value;
                    var afterEquals = m.Groups[3].Success;
                    if (string.IsNullOrEmpty(before) || before == "." || AssemblyPrimitives.Contains(before) || afterEquals)
                        continue;
                    if (reported.Contains(type)) continue;
                    reported.Add(type);

                    if (!declaredIn.TryGetValue(type, out var owners))
                        continue;
                    var unreachable = owners.Where(o => o != file && !IsReachable(file, o, asmdefs, graph)).ToList();
                    if (unreachable.Count == 0 || unreachable.Count < owners.Count)
                        continue;

                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"Type {type} declared in {string.Join(", ", unreachable)} but {file} has no assembly reference to reach it.",
                        Severity = Severity,
                        AssemblyName = file,
                        TypeName = type
                    });
                }
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private static void Dfs(string node, Dictionary<string, HashSet<string>> graph, HashSet<string> visited, HashSet<string> recursionStack, List<string> path, List<RuleViolation> violations, string ruleId)
    {
        visited.Add(node);
        recursionStack.Add(node);
        path.Add(node);

        if (graph.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (!graph.ContainsKey(neighbor)) continue;
                if (!visited.Contains(neighbor))
                {
                    Dfs(neighbor, graph, visited, recursionStack, path, violations, ruleId);
                }
                else if (recursionStack.Contains(neighbor))
                {
                    var cycleStart = path.IndexOf(neighbor);
                    var cycle = path.Skip(cycleStart).ToList();
                    cycle.Add(neighbor);
                    violations.Add(new RuleViolation
                    {
                        RuleId = ruleId,
                        Message = $"Assembly dependency cycle: {string.Join(" -> ", cycle)}",
                        Severity = RuleSeverity.Error
                    });
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        recursionStack.Remove(node);
    }

    private static bool IsReachable(string from, string to, List<(string Name, string Dir, List<string> Refs)> asmdefs, Dictionary<string, HashSet<string>> graph)
    {
        var fromAsm = asmdefs.FirstOrDefault(a => from.StartsWith(a.Dir.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
        var toAsm = asmdefs.FirstOrDefault(a => to.StartsWith(a.Dir.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
        if (fromAsm.Name == toAsm.Name) return true;
        if (!graph.ContainsKey(fromAsm.Name)) return false;
        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(fromAsm.Name);
        reachable.Add(fromAsm.Name);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (graph.TryGetValue(current, out var deps))
            {
                foreach (var dep in deps)
                {
                    if (dep == toAsm.Name) return true;
                    if (reachable.Add(dep)) queue.Enqueue(dep);
                }
            }
        }
        return false;
    }
}
