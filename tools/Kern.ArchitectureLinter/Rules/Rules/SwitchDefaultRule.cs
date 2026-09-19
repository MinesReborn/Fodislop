using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Rules;

/// <summary>
/// Switch statements with 3+ cases must have a default branch.
/// An unhandled value falls through silently, with no error and no log.
/// Ported from check-architecture.js checkSwitchDefaultCoverage().
/// </summary>
public sealed class SwitchDefaultRule : IRule
{
    private static readonly Regex SwitchRegex = new(@"^\s*switch\s*\(\s*[\w.]+\s*\)\s*$", RegexOptions.Compiled);

    public string Id => "KERN-SWITCH-DEFAULT";
    public string Description => "Switch statements with 3+ cases must have default";
    public RuleSeverity Severity => RuleSeverity.Warning;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;
        var scriptsRoot = Path.Combine(projectRoot, "Assets", "Scripts");
        var editorRoot = Path.Combine(projectRoot, "Assets", "Editor");

        foreach (var file in SourceScanner.EnumerateAllCsFiles(scriptsRoot, editorRoot))
        {
            var relative = SourceScanner.GetProjectRelativePath(projectRoot, file);
            if (IsExcluded(relative)) continue;

            cancellationToken.ThrowIfCancellationRequested();
            var content = File.ReadAllText(file);
            var lines = content.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (!SwitchRegex.IsMatch(lines[i]))
                    continue;

                var body = new List<string>();
                var depth = 0;
                var started = false;
                for (var j = i + 1; j < lines.Length && j < i + 140; j++)
                {
                    depth += (lines[j].Split('{').Length - 1);
                    depth -= (lines[j].Split('}').Length - 1);
                    body.Add(lines[j]);
                    if (lines[j].Trim() == "{" || (started && depth <= 0))
                    {
                        started = true;
                        if (depth <= 0 && started)
                            break;
                    }
                }

                var text = string.Join("\n", body);
                var cases = Regex.Matches(text, @"^\s*case\s", RegexOptions.Multiline).Count;
                if (cases >= 3 && !Regex.IsMatch(text, @"^\s*default\s*:", RegexOptions.Multiline))
                {
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"switch с {cases} case не имеет default — необработанное значение пройдёт молча. Добавьте default с throw/log.",
                        Severity = Severity,
                        AssemblyName = relative,
                        TypeName = $"{relative}:{i + 1}"
                    });
                }
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private static bool IsExcluded(string relative)
    {
        return relative.StartsWith("Assets/Scripts/Tests/") ||
               relative.StartsWith("Assets/Scripts/VContainer/") ||
               relative.StartsWith("Assets/Plugins/");
    }
}
