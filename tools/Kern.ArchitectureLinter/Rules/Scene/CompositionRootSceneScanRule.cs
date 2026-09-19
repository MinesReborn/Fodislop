#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Scene;

/// <summary>
/// Composition roots must use serialized references or their own authored hierarchy.
/// Global runtime scene scans (Find, FindGameObjectWithTag) are forbidden.
/// Ported from check-architecture.js checkCompositionRootContracts().
/// </summary>
public sealed class CompositionRootSceneScanRule : IRule
{
    public string Id => "KERN-COMPOSITION-ROOT-SCAN";
    public string Description => "Composition root scene scan prohibition";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    private static readonly string[] CompositionRoots =
    {
        "Assets/Scripts/Core/Bootstrap/BootstrapLifetimeScope.cs",
        "Assets/Scripts/Core/Bootstrap/GameLifetimeScope.cs",
        "Assets/Scripts/Core/Bootstrap/GatewayLifetimeScope.cs",
        "Assets/Scripts/Core/Bootstrap/MainMenuLifetimeScope.cs",
    };

    private static readonly Regex Forbidden = new(
        @"\bFind(?:AnyObject|FirstObject|Objects?ByType)<|\bFindGameObjectWithTag\s*\(",
        RegexOptions.Compiled);

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();

        foreach (var relative in CompositionRoots)
        {
            var path = Path.Combine(context.ProjectRoot, relative);
            if (!File.Exists(path)) continue;

            var content = File.ReadAllText(path);
            var stripped = SourceScanner.StripComments(content);

            foreach (Match m in Forbidden.Matches(stripped))
            {
                var line = content.Substring(0, m.Index).Count(c => c == '\n') + 1;
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"Composition root использует глобальный поиск по сцене ({m.Value}). Используйте сериализованные ссылки или authored hierarchy.",
                    Severity = Severity,
                    TypeName = $"{relative}:{line}"
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
