#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Contracts;

/// <summary>
/// Validates editor scene authoring contract:
/// - Scene auto-fixing editor tools are deleted (only read-only validator may exist)
/// - ProductionSceneContractValidator must exist and guard scene contracts
/// - GameLifetimeScope must include WorldTextureManager
/// - RegisterManager must require typed ManagerBindings without hierarchy-search fallback
/// Ported from check-architecture.js checkEditorSceneAuthoringContract().
/// </summary>
public sealed class EditorSceneAuthoringContractRule : IRule
{
    public string Id => "KERN-EDITOR-SCENE-AUTHORING";
    public string Description => "Editor scene authoring contract validation";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;

        // Scene auto-fixing tools must not exist
        var authoringPath = Path.Combine(projectRoot, "Assets/Scripts/Editor/SceneScopeAuthoring.cs");
        var migrationPath = Path.Combine(projectRoot, "Assets/Scripts/Editor/SceneContractMigration.cs");

        if (File.Exists(authoringPath) || File.Exists(migrationPath))
        {
            violations.Add(new RuleViolation
            {
                RuleId = Id,
                Message = "Scene auto-fixing editor tools удалены; только read-only ProductionSceneContractValidator может существовать.",
                Severity = Severity,
                TypeName = "Assets/Scripts/Editor/SceneScopeAuthoring.cs"
            });
        }

        // ProductionSceneContractValidator must exist
        var validatorPath = Path.Combine(projectRoot, "Assets/Scripts/Editor/Validation/ProductionSceneContractValidator.cs");
        if (!File.Exists(validatorPath))
        {
            violations.Add(new RuleViolation
            {
                RuleId = Id,
                Message = "Read-only ProductionSceneContractValidator должен существовать и guardить scene contracts.",
                Severity = Severity,
                TypeName = "Assets/Scripts/Editor/Validation/ProductionSceneContractValidator.cs"
            });
        }
        else
        {
            var validator = File.ReadAllText(validatorPath);
            if (!validator.Contains("bindingCount == 0") ||
                !validator.Contains("boundTypes") ||
                !validator.Contains("target.transform.IsChildOf(groupRoot)"))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "Production validator должен отклонять empty, duplicate, stale и wrongly-grouped ManagerBindings.",
                    Severity = Severity,
                    TypeName = "Assets/Scripts/Editor/Validation/ProductionSceneContractValidator.cs"
                });
            }
        }

        // GameLifetimeScope
        var scopePath = Path.Combine(projectRoot, "Assets/Scripts/Core/Bootstrap/GameLifetimeScope.cs");
        if (File.Exists(scopePath))
        {
            var scope = File.ReadAllText(scopePath);

            if (!scope.Contains("RegisterManager<WorldTextureManager>(builder, \"World\")"))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "MainGame World manager contract должен включать WorldTextureManager.",
                    Severity = Severity,
                    TypeName = "Assets/Scripts/Core/Bootstrap/GameLifetimeScope.cs"
                });
            }

            if (!scope.Contains("ResolveTypedBinding<T>(group)") ||
                Regex.IsMatch(scope, @"FindManagerInOwnScene|GetComponentsInChildren<T>\(true\).*RegisterManager"))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "RegisterManager должен требовать typed ManagerBindings без hierarchy-search fallback.",
                    Severity = Severity,
                    TypeName = "Assets/Scripts/Core/Bootstrap/GameLifetimeScope.cs"
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
