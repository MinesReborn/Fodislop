using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter;

public sealed class ArchitectureLinter
{
    private readonly LinterContext _context;
    private readonly IReadOnlyList<IRule> _rules;

    public ArchitectureLinter(LinterContext context, IReadOnlyList<IRule>? rules = null)
    {
        _context = context;
        IReadOnlyList<IRule> discoveredRules = rules ?? CreateDefaultRules();
        _rules = SelectRules(discoveredRules, context.IncludedRuleIds);
    }

    internal static bool SelectedRulesRequireAssemblies(IReadOnlySet<string> includedRuleIds)
    {
        return SelectRules(CreateDefaultRules(), includedRuleIds)
            .Any(rule => rule.RequiresAssemblies);
    }

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        Console.WriteLine("Kern Architecture Linter v1.0.0");
        Console.WriteLine($"Project root: {_context.ProjectRoot}");
        Console.WriteLine($"Assemblies: {_context.AssemblyPaths.Count}");
        Console.WriteLine($"Rules: {_rules.Count}");
        Console.WriteLine();

        try
        {
            IReadOnlyList<Mono.Cecil.AssemblyDefinition> assemblies = [];
            if (_rules.Any(rule => rule.RequiresAssemblies))
            {
                if (_context.AssemblyPaths.Count == 0)
                {
                    Console.Error.WriteLine("No assemblies found for the selected rules.");
                    return 2;
                }

                assemblies = await CecilAssemblyScanner.LoadAssembliesAsync(
                    _context.AssemblyPaths,
                    _context.UnityAssemblyPaths,
                    ct);
                Console.WriteLine($"Loaded {assemblies.Count} assemblies successfully.");
            }
            else
            {
                Console.WriteLine("Selected rules scan source files only; assembly loading skipped.");
            }

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

            if (allViolations.Count > 0)
            {
                var grouped = allViolations.GroupBy(v => v.Severity).OrderBy(g => g.Key);
                foreach (var group in grouped)
                {
                    Console.WriteLine();
                    Console.WriteLine($"{group.Count()} {group.Key}(s):");
                    foreach (var v in group.OrderBy(v => v.AssemblyName).ThenBy(v => v.TypeName))
                    {
                        var location = string.IsNullOrEmpty(v.TypeName)
                            ? v.AssemblyName ?? "<unknown>"
                            : $"{v.AssemblyName ?? "<unknown>"}!{v.TypeName}{(string.IsNullOrEmpty(v.MemberName) ? "" : "." + v.MemberName)}";
                        Console.WriteLine($"  {v.RuleId}: {v.Message} [{location}]");
                    }
                }
                Console.WriteLine();
            }

            if (_context.EnableSarif && !string.IsNullOrEmpty(_context.SarifOutputPath))
            {
                await WriteSarifAsync(allViolations, _context.SarifOutputPath, ct);
            }

            if (allViolations.Count == 0)
                return 0;

            var hasErrors = allViolations.Any(v =>
                SeverityRank(v.Severity) >= SeverityRank(_context.FailOnSeverity));
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
                    tool = new { name = "Kern Architecture Linter", version = "1.0.0" },
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
        Type ruleContract = typeof(IRule);
        return ruleContract.Assembly
            .GetTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false } &&
                ruleContract.IsAssignableFrom(type))
            .Select(CreateRule)
            .OrderBy(rule => rule.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<IRule> SelectRules(
        IReadOnlyList<IRule> rules,
        IReadOnlySet<string> includedRuleIds)
    {
        ValidateRuleCatalog(rules);

        if (includedRuleIds.Count == 0)
        {
            return rules;
        }

        string[] knownIds = rules.Select(rule => rule.Id).ToArray();
        string[] unknownIds = includedRuleIds
            .Where(id => !knownIds.Contains(id, StringComparer.OrdinalIgnoreCase))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unknownIds.Length > 0)
        {
            throw new ArgumentException($"Unknown architecture rule(s): {string.Join(", ", unknownIds)}");
        }

        return rules
            .Where(rule => includedRuleIds.Contains(rule.Id))
            .ToArray();
    }

    private static void ValidateRuleCatalog(IReadOnlyList<IRule> rules)
    {
        string[] emptyIdRules = rules
            .Where(rule => string.IsNullOrWhiteSpace(rule.Id))
            .Select(rule => rule.GetType().FullName ?? rule.GetType().Name)
            .ToArray();
        if (emptyIdRules.Length > 0)
        {
            throw new InvalidOperationException(
                $"Architecture rules require non-empty IDs: {string.Join(", ", emptyIdRules)}");
        }

        string[] duplicateIds = rules
            .GroupBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (duplicateIds.Length > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate architecture rule IDs: {string.Join(", ", duplicateIds)}");
        }
    }

    private static int SeverityRank(RuleSeverity severity)
    {
        return severity switch
        {
            RuleSeverity.Info => 0,
            RuleSeverity.Warning => 1,
            RuleSeverity.Error => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null),
        };
    }

    private static IRule CreateRule(Type ruleType)
    {
        if (Activator.CreateInstance(ruleType) is IRule rule)
        {
            return rule;
        }

        throw new InvalidOperationException(
            $"Architecture rule '{ruleType.FullName}' requires a public parameterless constructor.");
    }
}
