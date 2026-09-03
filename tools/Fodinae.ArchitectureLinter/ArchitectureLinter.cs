using Fodinae.ArchitectureLinter.Core;
using Fodinae.ArchitectureLinter.Rules;
using Fodinae.ArchitectureLinter.Scanning;
using Fodinae.ArchitectureLinter.Reporting;

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
        IReporter reporter = _context.EnableSarif && !string.IsNullOrEmpty(_context.SarifOutputPath)
            ? new SarifReporter(_context.SarifOutputPath)
            : new ConsoleReporter();

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

            await reporter.ReportAsync(allViolations);

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
