#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Contracts;

/// <summary>
/// Validates scene transition state contracts:
/// - Bootstrap must clear _currentSceneName before loading replacement
/// - TransitionChanged must publish typed completion and failure
/// - Legacy split transition events are forbidden
/// - ISceneNavigator must expose single TransitionChanged event
/// - SceneTransitionTicket failure/terminal invariants
/// - Transition observers must be invoked independently
/// Ported from check-architecture.js checkTransitionStateContracts().
/// </summary>
public sealed class TransitionStateContractRule : IRule
{
    public string Id => "KERN-TRANSITION-STATE";
    public string Description => "Scene transition state contract validation";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;

        var bootstrapPath = Path.Combine(projectRoot, "Assets/Scripts/Core/Bootstrap/BootstrapLifetimeScope.cs");
        if (File.Exists(bootstrapPath))
        {
            var source = File.ReadAllText(bootstrapPath);

            if (!source.Contains("_currentSceneName = null;"))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "Переход от загруженной сцены должен очищать текущее состояние сцены до загрузки новой.",
                    Severity = Severity,
                    TypeName = "Assets/Scripts/Core/Bootstrap/BootstrapLifetimeScope.cs"
                });
            }

            if (!source.Contains("TransitionChanged") ||
                !source.Contains("SceneTransitionPhase.Completed") ||
                !source.Contains("ticket.Fail(ex)"))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "Bootstrap scene transitions должны публиковать typed completion и failure states.",
                    Severity = Severity,
                    TypeName = "Assets/Scripts/Core/Bootstrap/BootstrapLifetimeScope.cs"
                });
            }

            if (Regex.IsMatch(source, @"Transition(?:Started|Completed|Failed)"))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "Legacy split transition events запрещены; публикуйте SceneTransitionStatus через TransitionChanged.",
                    Severity = Severity,
                    TypeName = "Assets/Scripts/Core/Bootstrap/BootstrapLifetimeScope.cs"
                });
            }
        }

        // ISceneNavigator
        var navigatorPath = Path.Combine(projectRoot, "Assets/Scripts/Core/Interfaces/Contracts/Core/ISceneNavigator.cs");
        if (File.Exists(navigatorPath))
        {
            var navigator = File.ReadAllText(navigatorPath);
            if (!navigator.Contains("event Action<SceneTransitionStatus>? TransitionChanged"))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "ISceneNavigator должен предоставлять единственный typed TransitionChanged event.",
                    Severity = Severity,
                    TypeName = "Assets/Scripts/Core/Interfaces/Contracts/Core/ISceneNavigator.cs"
                });
            }
        }

        // SceneTransitionTicket
        var ticketPath = Path.Combine(projectRoot, "Assets/Scripts/Core/Bootstrap/SceneTransitionTicket.cs");
        if (File.Exists(ticketPath))
        {
            var ticket = File.ReadAllText(ticketPath);
            if (!ticket.Contains("SetPhase(SceneTransitionPhase.Failed, exception)") ||
                !ticket.Contains("Phase is SceneTransitionPhase.Failed or SceneTransitionPhase.PresentationReady"))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "SceneTransitionTicket должен публиковать Failed ровно один раз и сохранять PresentationReady terminal.",
                    Severity = Severity,
                    TypeName = "Assets/Scripts/Core/Bootstrap/SceneTransitionTicket.cs"
                });
            }
        }

        // SceneTransitionRuntime
        var runtimePath = Path.Combine(projectRoot, "Assets/Scripts/Core/Bootstrap/SceneTransitionRuntime.cs");
        if (File.Exists(runtimePath))
        {
            var runtime = File.ReadAllText(runtimePath);
            if (!runtime.Contains("GetInvocationList()") ||
                !runtime.Contains("catch (Exception exception)"))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "Transition observers должны вызываться независимо чтобы один subscriber не мог abortить транзакцию.",
                    Severity = Severity,
                    TypeName = "Assets/Scripts/Core/Bootstrap/SceneTransitionRuntime.cs"
                });
            }
        }

        // SceneTransitionStatus
        var statusPath = Path.Combine(projectRoot, "Assets/Scripts/Core/Bootstrap/Contracts/SceneTransitionStatus.cs");
        if (File.Exists(statusPath))
        {
            var status = File.ReadAllText(statusPath);
            if (!status.Contains("CompletedWithWarnings") ||
                !status.Contains("Failed") ||
                !status.Contains("Exception? Failure"))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "SceneTransitionStatus должен представлять successful, degraded и failed terminal outcomes.",
                    Severity = Severity,
                    TypeName = "Assets/Scripts/Core/Bootstrap/Contracts/SceneTransitionStatus.cs"
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
