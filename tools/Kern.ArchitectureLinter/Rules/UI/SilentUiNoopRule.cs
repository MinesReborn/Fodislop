#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.UI;

/// <summary>
/// Guards on rootVisualElement == null that silently return are the failure mode
/// that black-screens with ZERO console output. Every such guard must either log,
/// or carry a comment explaining why the silent return is expected.
/// Ported from check-architecture.js checkSilentUiNoop().
/// </summary>
public sealed class SilentUiNoopRule : IRule
{
    public string Id => "KERN-SILENT-UI-NOOP";
    public string Description => "Silent UI no-op detection";
    public RuleSeverity Severity => RuleSeverity.Warning;
    public bool RequiresAssemblies => false;

    private static readonly Regex GuardPattern = new(
        @"if\s*\([^)]*\brootVisualElement\b[^)]*==\s*null\)",
        RegexOptions.Compiled);

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var uiSrc = Path.Combine(context.ProjectRoot, "Assets", "Scripts", "UI");

        if (!Directory.Exists(uiSrc))
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);

        foreach (var file in SourceScanner.EnumerateCsFiles(uiSrc, "Tests"))
        {
            var content = File.ReadAllText(file);
            var lines = content.Split('\n');
            var relative = SourceScanner.GetProjectRelativePath(context.ProjectRoot, file);

            for (var i = 0; i < lines.Length; i++)
            {
                if (!GuardPattern.IsMatch(lines[i]))
                    continue;

                // Collect guard body
                var blockLines = new List<string> { lines[i] };
                var openLine = i;
                if (!lines[i].Contains("{") && i + 1 < lines.Length && lines[i + 1].Contains("{"))
                {
                    openLine = i + 1;
                    blockLines.Add(lines[i + 1]);
                }

                if (lines[i].Contains("{") || openLine > i)
                {
                    var depth = lines[openLine].Count(c => c == '{') - lines[openLine].Count(c => c == '}');
                    var j = openLine + 1;
                    while (j < lines.Length && depth > 0)
                    {
                        blockLines.Add(lines[j]);
                        depth += lines[j].Count(c => c == '{');
                        depth -= lines[j].Count(c => c == '}');
                        j++;
                    }
                }

                var blockText = string.Join("\n", blockLines);
                if (!Regex.IsMatch(blockText, @"\breturn\s*[^;]*;"))
                    continue;

                if (Regex.IsMatch(blockText, @"Debug\.(Log|LogWarning|LogError|LogException)"))
                    continue;

                // Check for comment justification
                var above = string.Join("\n", lines[Math.Max(0, i - 2)..i]);
                var hasComment = blockText.Contains("//") || blockText.Contains("/*") || above.Contains("//");
                if (hasComment) continue;

                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"строка {i + 1}: guard на rootVisualElement == null молча делает return без Debug-лога и без комментария. При неготовой панели экран не построится, а в консоль не попадёт ничего.",
                    Severity = Severity,
                    TypeName = $"{relative}:{i + 1}"
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
