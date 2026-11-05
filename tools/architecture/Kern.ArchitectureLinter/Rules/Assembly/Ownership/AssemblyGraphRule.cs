using Kern.ArchitectureLinter.Core;
using Mono.Cecil;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Assembly;

/// <summary>
/// Validates assembly graph structure and namespace visibility.
/// Detects cross-assembly references without proper using directives.
/// </summary>
public sealed class AssemblyGraphRule : IRule
{
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
        foreach (var file in Directory.EnumerateFiles(Path.Combine(projectRoot, "Assets/Scripts"), "*.asmdef", SearchOption.AllDirectories))
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

        var assemblyNamesByGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(projectRoot, "Assets", "Scripts"), "*.asmdef", SearchOption.AllDirectories))
        {
            string metadata = file + ".meta";
            if (!File.Exists(metadata))
            {
                continue;
            }

            Match guid = Regex.Match(File.ReadAllText(metadata), @"^guid:\s*(\w+)", RegexOptions.Multiline);
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            if (guid.Success)
            {
                assemblyNamesByGuid["GUID:" + guid.Groups[1].Value] = document.RootElement.GetProperty("name").GetString()!;
            }
        }

        var graph = asmdefs.ToDictionary(
            a => a.Name,
            a => new HashSet<string>(a.Refs.Select(reference => assemblyNamesByGuid.GetValueOrDefault(reference, reference)), StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(projectRoot, "Assets", "Scripts"), "*.asmref", SearchOption.AllDirectories))
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            string reference = document.RootElement.GetProperty("reference").GetString()!;
            string name = assemblyNamesByGuid.GetValueOrDefault(reference, reference);
            if (graph.TryGetValue(name, out var references))
            {
                asmdefs.Add((name, Path.GetDirectoryName(file)!, references.ToList()));
            }
        }

        // The nearest asmdef or asmref owns a source file; namespace names do not assign ownership.
        asmdefs.Sort((left, right) => right.Dir.Length.CompareTo(left.Dir.Length));
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recursionStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();

        foreach (var node in graph.Keys)
        {
            if (visited.Contains(node)) continue;
            Dfs(node, graph, visited, recursionStack, path, violations, Id);
        }

        string scriptsRoot = Path.Combine(projectRoot, "Assets/Scripts");
        SyntaxTree[] trees = CSharpSourceFiles.EnumerateCsFiles(scriptsRoot, "Tests", "Plugins", "VContainer", "Editor")
            .Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: Path.GetFullPath(file)))
            .ToArray();
        MetadataReference[] platformReferences = new[]
            {
                typeof(object).Assembly.Location,
                typeof(Enumerable).Assembly.Location,
                typeof(Task).Assembly.Location,
            }
            .Distinct(StringComparer.Ordinal)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "Kern.SourceOwnership",
            trees,
            platformReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        foreach (SyntaxTree tree in trees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SemanticModel model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (SimpleNameSyntax name in tree.GetRoot(cancellationToken).DescendantNodes().OfType<SimpleNameSyntax>())
            {
                ISymbol? symbol = model.GetSymbolInfo(name, cancellationToken).Symbol;
                if (symbol is IAliasSymbol alias)
                {
                    symbol = alias.Target;
                }

                if (symbol is not INamedTypeSymbol type || type.TypeKind == TypeKind.Error)
                {
                    continue;
                }

                string identity = type.OriginalDefinition.ToDisplayString();
                if (!reported.Add(identity))
                {
                    continue;
                }

                string? declaration = type.OriginalDefinition.Locations
                    .FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath;
                if (declaration == null || declaration == tree.FilePath ||
                    IsReachable(tree.FilePath, declaration, asmdefs, graph))
                {
                    continue;
                }

                string consumer = ProjectSourcePaths.GetProjectRelativePath(projectRoot, tree.FilePath);
                string producer = ProjectSourcePaths.GetProjectRelativePath(projectRoot, declaration);
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"Type {identity} declared in {producer} is not reachable from the assembly owning {consumer}.",
                    Severity = Severity,
                    AssemblyName = consumer,
                    TypeName = identity,
                    Line = name.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                });
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
