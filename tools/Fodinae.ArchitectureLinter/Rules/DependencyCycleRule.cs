using Mono.Cecil;
using Fodinae.ArchitectureLinter.Core;

namespace Fodinae.ArchitectureLinter.Rules;

public sealed class DependencyCycleRule : IRule
{
    private static readonly string[] FodinaeAssemblyPrefixes = { "Fodinae" };

    public string Id => "FOD-DEP-CYCLE";
    public string Description => "Dependency cycle detection between Fodinae assemblies";
    public RuleSeverity Severity => RuleSeverity.Error;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();

        var fodinaeAssemblies = assemblies
            .Where(a => FodinaeAssemblyPrefixes.Any(p => a.Name.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (fodinaeAssemblies.Count < 2)
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);

        var graph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in fodinaeAssemblies)
        {
            var name = assembly.Name.Name;
            if (!graph.ContainsKey(name))
                graph[name] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var reference in assembly.MainModule.AssemblyReferences)
            {
                if (FodinaeAssemblyPrefixes.Any(p => reference.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                {
                    graph[name].Add(reference.Name);
                }
            }
        }

        var cycles = DetectCycles(graph);
        foreach (var cycle in cycles)
        {
            violations.Add(new RuleViolation
            {
                RuleId = Id,
                Message = $"Dependency cycle detected: {string.Join(" -> ", cycle)}",
                Severity = Severity
            });
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private static List<List<string>> DetectCycles(Dictionary<string, HashSet<string>> graph)
    {
        var cycles = new List<List<string>>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recursionStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();

        foreach (var node in graph.Keys)
        {
            if (!visited.Contains(node))
                Dfs(node, graph, visited, recursionStack, path, cycles);
        }

        return cycles;
    }

    private static void Dfs(
        string node,
        Dictionary<string, HashSet<string>> graph,
        HashSet<string> visited,
        HashSet<string> recursionStack,
        List<string> path,
        List<List<string>> cycles)
    {
        visited.Add(node);
        recursionStack.Add(node);
        path.Add(node);

        if (graph.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (!graph.ContainsKey(neighbor))
                    continue;

                if (!visited.Contains(neighbor))
                {
                    Dfs(neighbor, graph, visited, recursionStack, path, cycles);
                }
                else if (recursionStack.Contains(neighbor))
                {
                    var cycleStart = path.IndexOf(neighbor);
                    var cycle = path.Skip(cycleStart).ToList();
                    cycle.Add(neighbor);
                    cycles.Add(cycle);
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        recursionStack.Remove(node);
    }
}
