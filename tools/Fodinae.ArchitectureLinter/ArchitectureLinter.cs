using Fodinae.ArchitectureLinter.Core;
using Fodinae.ArchitectureLinter.Rules;
using Fodinae.ArchitectureLinter.Scanning;

namespace Fodinae.ArchitectureLinter;

public sealed class ArchitectureLinter
{
    private readonly LinterContext _context;
    private readonly IReadOnlyList<IRule> _rules;

    public ArchitectureLinter(LinterContext context, IReadOnlyList<IRule>? rules = null)
    {
        _context = context;
        _rules = rules ?? CreateDefaultRules();
    }

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        Console.WriteLine("Fodinae Architecture Linter v1.0.0");
        Console.WriteLine($"Project root: {_context.ProjectRoot}");
        Console.WriteLine($"Assemblies: {_context.AssemblyPaths.Count}");
        Console.WriteLine($"Rules: {_rules.Count}");
        Console.WriteLine();

        try
        {
            var assemblies = await CecilAssemblyScanner.LoadAssembliesAsync(
                _context.AssemblyPaths,
                _context.UnityAssemblyPaths,
                ct);

            Console.WriteLine($"Loaded {assemblies.Count} assemblies successfully.");
            Console.WriteLine();

            var allViolations = new List<RuleViolation>();
            foreach (var rule in _rules)
            {
                ct.ThrowIfCancellationRequested();
                Console.Write($"Running rule {rule.Id} ({rule.Description})... ");

                try
                {
                    var violations = await rule.EvaluateAsync(assemblies, _context, ct);
                    allViolations.AddRange(violations);
                    Console.WriteLine(violations.Count == 0 ? "OK" : $"{violations.Count} violation(s)");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Console.WriteLine($"FAILED: {ex.Message}");
                    allViolations.Add(new RuleViolation
                    {
                        RuleId = rule.Id,
                        Message = $"Rule execution failed: {ex.Message}",
                        Severity = RuleSeverity.Error,
                        AssemblyName = null
                    });
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Total violations: {allViolations.Count}");

            if (_context.EnableSarif && !string.IsNullOrEmpty(_context.SarifOutputPath))
            {
                await WriteSarifAsync(allViolations, _context.SarifOutputPath, ct);
            }

            if (allViolations.Count == 0)
                return 0;

            var hasErrors = allViolations.Any(v => (int)v.Severity >= _context.FailOnSeverity);
            return hasErrors ? 1 : 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return 2;
        }
    }

    private static async Task WriteSarifAsync(IReadOnlyList<RuleViolation> violations, string path, CancellationToken ct)
    {
        var sarif = new
        {
            version = "2.1.0",
            schema = "https://json.schemastore.org/sarif-2.1.0.json",
            runs = new[]
            {
                new
                {
                    tool = new { name = "Fodinae Architecture Linter", version = "1.0.0" },
                    results = violations.Select(v => new
                    {
                        ruleId = v.RuleId,
                        level = v.Severity.ToString().ToLowerInvariant(),
                        message = new { text = v.Message },
                        locations = new[]
                        {
                            new
                            {
                                physicalLocation = new
                                {
                                    artifactLocation = new { uri = v.TypeName ?? v.AssemblyName ?? "unknown" },
                                    region = new { startLine = v.Line ?? 1 }
                                }
                            }
                        }
                    }).ToArray()
                }
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(sarif, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, ct);
        Console.WriteLine($"SARIF report written to: {path}");
    }

    private static IReadOnlyList<IRule> CreateDefaultRules()
    {
        return new IRule[]
        {
            new ForbiddenApiRule(),
            new BlockNamespaceRule(),
            new AsyncVoidRule(),
            new ExecutionOrderRule(),
            new InjectAttributeRule(),
            new NamingConventionRule(),
            new DependencyCycleRule(),
            new MonoBehaviourAccessRule()
        };
    }
}
