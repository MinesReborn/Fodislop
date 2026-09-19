#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.DI;

/// <summary>
/// RegisterBuildCallback may only inject authored scene behaviours.
/// Move Resolve/scene loading/startup work to IPostStartable.
/// Ported from check-architecture.js checkLifetimeScopeConfigure().
/// </summary>
public sealed class ConfigureReentrancyRule : IRule
{
    public string Id => "KERN-CONFIGURE-REENTRANCY";
    public string Description => "Configure reentrancy validation";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    private static readonly Regex RegisterBuildCallback = new(@"\bRegisterBuildCallback\b", RegexOptions.Compiled);
    private static readonly Regex InjectSceneBehaviours = new(@"\bInjectSceneBehaviours\b", RegexOptions.Compiled);

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var scriptsRoot = Path.Combine(context.ProjectRoot, "Assets", "Scripts");

        foreach (var file in SourceScanner.EnumerateCsFiles(scriptsRoot, "Tests", "Editor", "VContainer"))
        {
            var content = File.ReadAllText(file);
            if (!content.Contains("LifetimeScope")) continue;

            var relative = SourceScanner.GetProjectRelativePath(context.ProjectRoot, file);
            var lines = content.Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                if (!RegisterBuildCallback.IsMatch(lines[i])) continue;
                if (InjectSceneBehaviours.IsMatch(lines[i])) continue;

                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"строка {i + 1}: RegisterBuildCallback может только инжектировать authored scene behaviours. Перенесите Resolve/scene loading/startup в IPostStartable.",
                    Severity = Severity,
                    TypeName = $"{relative}:{i + 1}"
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
