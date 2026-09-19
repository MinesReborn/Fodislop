#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Contracts;

/// <summary>
/// Validates scene readiness contracts:
/// - GameLifetimeScope must expose WaitUntilReadyAsync, MarkReady, MarkFailed
/// - Bootstrap must await SceneTransitionTicket presentation readiness
/// - GameBootstrap must publish both success and failure outcomes
/// - GameManager must wait for all subsystems before OnWorldLoaded
/// Ported from check-architecture.js checkSceneReadinessContracts().
/// </summary>
public sealed class SceneReadinessContractRule : IRule
{
    public string Id => "KERN-SCENE-READINESS";
    public string Description => "Scene readiness contract validation";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;

        // GameLifetimeScope
        var scopePath = Path.Combine(projectRoot, "Assets/Scripts/Core/Bootstrap/GameLifetimeScope.cs");
        if (File.Exists(scopePath))
        {
            var scopeContent = File.ReadAllText(scopePath);
            if (!scopeContent.Contains("WaitUntilReadyAsync") ||
                !scopeContent.Contains("MarkReady") ||
                !scopeContent.Contains("MarkFailed"))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "GameLifetimeScope должен предоставлять детерминированный ready/failed сигнал для Bootstrap scene transitions.",
                    Severity = Severity,
                    TypeName = "Assets/Scripts/Core/Bootstrap/GameLifetimeScope.cs"
                });
            }
        }

        // Bootstrap
        var bootstrapPath = Path.Combine(projectRoot, "Assets/Scripts/Core/Bootstrap/BootstrapLifetimeScope.cs");
        if (File.Exists(bootstrapPath))
        {
            var bootstrapContent = File.ReadAllText(bootstrapPath);
            if (!bootstrapContent.Contains("WaitForPresentationAsync") ||
                !bootstrapContent.Contains("SceneTransitionTicket"))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "Bootstrap должен ожидать SceneTransitionTicket presentation readiness прежде чем выгружать предыдущую сцену.",
                    Severity = Severity,
                    TypeName = "Assets/Scripts/Core/Bootstrap/BootstrapLifetimeScope.cs"
                });
            }
        }

        // GameBootstrap
        var gameBootstrapPath = Path.Combine(projectRoot, "Assets/Scripts/Core/Bootstrap/GameBootstrap.cs");
        if (File.Exists(gameBootstrapPath))
        {
            var gameBootstrapContent = File.ReadAllText(gameBootstrapPath);
            if (!gameBootstrapContent.Contains("_scope.MarkReady()") ||
                !gameBootstrapContent.Contains("_scope.MarkFailed(exception)"))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "GameBootstrap должен публиковать оба исхода старта: успешный и неудачный.",
                    Severity = Severity,
                    TypeName = "Assets/Scripts/Core/Bootstrap/GameBootstrap.cs"
                });
            }
        }

        // GameManager
        var gameManagerPath = Path.Combine(projectRoot, "Assets/Scripts/Game/Managers/GameManager.cs");
        if (File.Exists(gameManagerPath))
        {
            var gameManagerContent = File.ReadAllText(gameManagerPath);
            if (!gameManagerContent.Contains("IsVisualsLoaded") ||
                !gameManagerContent.Contains("PendingAssetCount") ||
                !gameManagerContent.Contains("PendingCellTextureRequests") ||
                !gameManagerContent.Contains("_surfaceRenderer.IsInitialized") ||
                !gameManagerContent.Contains("_lightingEngine.IsInitialized"))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "OnWorldLoaded должен ожидать player visuals, surface, lighting и pending asset/texture queues.",
                    Severity = Severity,
                    TypeName = "Assets/Scripts/Game/Managers/GameManager.cs"
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
