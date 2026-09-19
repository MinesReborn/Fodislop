#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Scene;

/// <summary>
/// Scene composition roots must register their controller as an authored scene
/// component so VContainer injects it during resolution.
/// Ported from check-architecture.js checkSceneScopeInjection().
/// </summary>
public sealed class SceneScopeInjectionRule : IRule
{
    public string Id => "KERN-SCENE-SCOPE-INJECTION";
    public string Description => "Scene scope injection validation";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    private static readonly (string File, string Component, string InjectVar)[] Contracts = new[]
    {
        ("Assets/Scripts/Core/Bootstrap/GatewayLifetimeScope.cs", "GatewayController", "controller"),
        ("Assets/Scripts/Core/Bootstrap/MainMenuLifetimeScope.cs", "MainMenu", "controller"),
    };

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();

        foreach (var (relative, component, injectVar) in Contracts)
        {
            var path = Path.Combine(context.ProjectRoot, relative);
            if (!File.Exists(path)) continue;

            var content = File.ReadAllText(path);
            var pattern = $@"RegisterComponent\([^)]*_{injectVar}[^)]*\)";

            if (!Regex.IsMatch(content, pattern))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"{component} должен быть зарегистрирован как authored scene component чтобы VContainer инжектировал его при сборке.",
                    Severity = Severity,
                    TypeName = relative
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
