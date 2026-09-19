#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.UI;

/// <summary>
/// View initialization must be single-road: Start (dependencies injected, panel ready)
/// + readiness event for async deps. Per-frame TryInitialize from Update is a silent
/// no-op pipeline: the view silently waits, screen doesn't build, console stays empty.
/// Ported from check-architecture.js checkSingleRoadInit().
/// </summary>
public sealed class OneRoadInitRule : IRule
{
    public string Id => "KERN-ONE-ROAD-INIT";
    public string Description => "Single-road view initialization enforcement";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    private static readonly Regex UpdateMethod = new(@"void\s+Update\s*\(", RegexOptions.Compiled);
    private static readonly Regex TryInitialize = new(@"\bTryInitialize\s*\(", RegexOptions.Compiled);

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
                if (!UpdateMethod.IsMatch(lines[i]))
                    continue;

                // Collect Update method body
                var openLine = i;
                if (!lines[i].Contains("{") && i + 1 < lines.Length && lines[i + 1].Contains("{"))
                    openLine = i + 1;
                if (!lines[openLine].Contains("{"))
                    continue;

                var depth = lines[openLine].Count(c => c == '{') - lines[openLine].Count(c => c == '}');
                var body = new List<string> { lines[openLine] };
                var j = openLine + 1;
                while (j < lines.Length && depth > 0)
                {
                    body.Add(lines[j]);
                    depth += lines[j].Count(c => c == '{');
                    depth -= lines[j].Count(c => c == '}');
                    j++;
                }

                if (TryInitialize.IsMatch(string.Join("\n", body)))
                {
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"строка {i + 1}: Update() вызывает TryInitialize — инициализация обязана быть событийной: Start + событие готовности для async-зависимостей. Per-frame ретрай — это тихий конвейер no-op.",
                        Severity = Severity,
                        TypeName = $"{relative}:{i + 1}"
                    });
                }
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
