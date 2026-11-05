#nullable enable

using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
#if KERN_SETTINGS_PROBE
using Kern.ArchitectureLinter.SettingsProbe;
#endif

namespace Kern.ArchitectureLinter.Rules.Settings;

/// <summary>
/// Wraps Kern.SettingsProbe checks into an IRule.
/// Validates settings logic outside Unity: defaults, clamps, ranges, audio buses, etc.
/// </summary>
public sealed class SettingsProbeRule : IRule
{
    public string Id => "KERN-SETTINGS-PROBE";
    public string Description => "Settings validation probe (defaults, clamps, ranges, buses)";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();

#if KERN_SETTINGS_PROBE
        try
        {
            // Use the project root from the linter context, not command line args
            var failures = SettingsProbe.Program.RunAllChecks(context.ProjectRoot);
            foreach (var failure in failures)
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = failure,
                    Severity = Severity
                });
            }
        }
        catch (Exception ex)
        {
            violations.Add(new RuleViolation
            {
                RuleId = Id,
                Message = $"Settings probe failed: {ex.Message}",
                Severity = Severity
            });
        }
#endif

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
