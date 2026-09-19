#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Contracts;

/// <summary>
/// Validates GameBootstrap startup dependency contract:
/// - GameStartupServices is deleted (obsolete)
/// - GameLifetimeScope must register GameBootstrap as entry point
/// - Required startup services must be registered
/// - GameBootstrap must only coordinate GameStartupPipeline
/// - No manual Resolve from container in GameBootstrap
/// Ported from check-architecture.js checkGameBootstrapResolvesRegisteredManagers().
/// </summary>
public sealed class GameBootstrapDependencyRule : IRule
{
    public string Id => "KERN-GAME-BOOTSTRAP-DEPENDENCY";
    public string Description => "GameBootstrap startup dependency contract";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    private static readonly string[] RequiredServices =
    {
        "GameInfrastructureStartup",
        "GamePresentationStartup",
        "GameStartupPipeline"
    };

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;

        var scopePath = Path.Combine(projectRoot, "Assets/Scripts/Core/Bootstrap/GameLifetimeScope.cs");
        var bootstrapPath = Path.Combine(projectRoot, "Assets/Scripts/Core/Bootstrap/GameBootstrap.cs");

        if (File.Exists(scopePath))
        {
            var scope = File.ReadAllText(scopePath);

            if (scope.Contains("GameStartupServices"))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "GameStartupServices удалён: GameBootstrap получает только реальные startup dependencies через constructor injection.",
                    Severity = Severity,
                    TypeName = "Assets/Scripts/Core/Bootstrap/GameLifetimeScope.cs"
                });
            }

            if (!scope.Contains("RegisterEntryPoint<GameBootstrap>"))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "GameLifetimeScope должен регистрировать GameBootstrap как entry point MainGame composition root.",
                    Severity = Severity,
                    TypeName = "Assets/Scripts/Core/Bootstrap/GameLifetimeScope.cs"
                });
            }

            foreach (var service in RequiredServices)
            {
                if (!scope.Contains($"Register<{service}>"))
                {
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"GameLifetimeScope должен регистрировать {service}.",
                        Severity = Severity,
                        TypeName = "Assets/Scripts/Core/Bootstrap/GameLifetimeScope.cs"
                    });
                }
            }
        }

        if (File.Exists(bootstrapPath))
        {
            var bootstrap = File.ReadAllText(bootstrapPath);

            if (!bootstrap.Contains("GameStartupPipeline") ||
                Regex.IsMatch(bootstrap, @"TerrainRenderer|PostProcessController|LightingEngine|PlayerHUDView|InventoryView"))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "GameBootstrap должен только координировать typed GameStartupPipeline и scene ticket.",
                    Severity = Severity,
                    TypeName = "Assets/Scripts/Core/Bootstrap/GameBootstrap.cs"
                });
            }

            if (Regex.IsMatch(bootstrap, @"\b(?:_resolver|resolver)\.Resolve\s*</") ||
                Regex.IsMatch(bootstrap, @"\bResolve\s*<[^>]+>\s*\("))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "GameBootstrap не должен резолвить из контейнера; только constructor injection.",
                    Severity = Severity,
                    TypeName = "Assets/Scripts/Core/Bootstrap/GameBootstrap.cs"
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
