#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Scene;

/// <summary>
/// MainMenu Play transition must be guarded
/// against duplicate clicks while loading or tearing down.
/// Ported from check-architecture.js checkUiTransitionGuards().
/// </summary>
public sealed class UITransitionGuardRule : IRule
{
    public string Id => "KERN-UI-TRANSITION-GUARD";
    public string Description => "UI transition guard validation";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;

        // MainMenu Play button guard
        var mainMenuPath = Path.Combine(projectRoot, "Assets/Scripts/UI/Menu/Navigation/MainMenu.cs");
        if (!File.Exists(mainMenuPath))
            mainMenuPath = Path.Combine(projectRoot, "Assets/Scripts/UI/Menu/MainMenu.cs");

        var worldEntryPath = Path.Combine(projectRoot, "Assets/Scripts/UI/Menu/WorldEntry/Control/MenuWorldEntryController.cs");

        bool hasPlayGuard = false;
        if (File.Exists(mainMenuPath))
        {
            var content = File.ReadAllText(mainMenuPath);
            if (Regex.IsMatch(content, @"(?:private|internal)\s+void\s+OnPlayButtonClicked\(\)\s*\{\s*if \(_loadingActive \|\| _teardownStarted\)"))
            {
                hasPlayGuard = true;
            }
        }

        if (!hasPlayGuard && File.Exists(worldEntryPath))
        {
            var content = File.ReadAllText(worldEntryPath);
            if (Regex.IsMatch(content, @"(?:private|internal)\s+void\s+OnPlayButtonClicked\(\)\s*\{\s*if \(_loadingActive \|\| _teardownStarted\)"))
            {
                hasPlayGuard = true;
            }
        }

        if (!hasPlayGuard)
        {
            violations.Add(new RuleViolation
            {
                RuleId = Id,
                Message = "MainMenu Play transition должен быть защищён от дублированных кликов во время загрузки или завершения.",
                Severity = Severity,
                TypeName = "Assets/Scripts/UI/Menu/Navigation/MainMenu.cs"
            });
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
