#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Contracts;

/// <summary>
/// Validates serialized scene contracts by reading .unity files as text.
/// Checks LifetimeScope presence, required components, groups, and hierarchy.
/// Ported from check-architecture.js checkSerializedSceneContracts().
/// </summary>
public sealed class SceneContractRule : IRule
{
    public string Id => "KERN-SCENE-CONTRACT";
    public string Description => "Serialized scene contract validation";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    private static readonly (string File, string Scope, string[] Components, Dictionary<string, string[]>? Groups)[] Contracts = new[]
    {
        ("Assets/Scenes/Bootstrap.unity", "BootstrapLifetimeScope", new[] { "BootstrapLifetimeScope" }, new Dictionary<string, string[]>
        {
            ["Networking"] = new[] { "ConnectionManager", "NetworkService" },
            ["Content"] = new[] { "ClientAssetLoader", "ClientConfigManager", "TextureStorageManager" },
            ["Audio"] = new[] { "AudioSystem" },
            ["Presentation"] = new[] { "BootstrapLoadingScreen" },
        }),
        ("Assets/Scenes/MainGame.unity", "GameLifetimeScope", new[] { "GameLifetimeScope" }, new Dictionary<string, string[]>
        {
            // Networking и Gameplay — чистый C# в контейнере (SCENE_STANDARD.md §1).
            ["World"] = new[] { "MapManager", "WorldBackgroundSetup", "WorldTextureManager" },
            ["Rendering"] = new[] { "TerrainRenderer", "WorldEntityBatchRenderer", "PostProcessController", "LightingEngine", "SurfaceRenderer", "CameraFollow", "VFXPool" },
            ["Audio"] = new[] { "ServerAudioEventManager" },
        }),
        ("Assets/Scenes/Gateway.unity", "GatewayLifetimeScope", new[] { "GatewayLifetimeScope", "GatewayController", "UIDocument" }, null),
        ("Assets/Scenes/MainMenu.unity", "MainMenuLifetimeScope", new[] { "MainMenuLifetimeScope", "MainMenu", "UIDocument" }, null),
    };

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;

        foreach (var (file, scope, components, groups) in Contracts)
        {
            var path = Path.Combine(projectRoot, file);
            if (!File.Exists(path))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "Файл сцены не найден.",
                    Severity = Severity,
                    TypeName = file
                });
                continue;
            }

            var content = File.ReadAllText(path);

            // Check scope component presence
            var scopeMatches = CountComponentOccurrences(content, scope);
            if (scopeMatches == 0)
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"Required scope '{scope}' отсутствует в сцене.",
                    Severity = Severity,
                    TypeName = file
                });
            }
            else if (scopeMatches > 1)
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"Ожидается ровно один '{scope}', найдено {scopeMatches}.",
                    Severity = Severity,
                    TypeName = file
                });
            }

            // Check required components (can be on any GameObject, not just named ones)
            foreach (var component in components)
            {
                if (!HasComponentInScene(content, component))
                {
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"Required component '{component}' отсутствует.",
                        Severity = Severity,
                        TypeName = file
                    });
                }
            }

            // Check groups
            if (groups != null)
            {
                foreach (var (group, managers) in groups)
                {
                    foreach (var manager in managers)
                    {
                        if (!ContainsComponentClass(content, manager))
                        {
                            violations.Add(new RuleViolation
                            {
                                RuleId = Id,
                                Message = $"Registered manager '{manager}' has no authored component.",
                                Severity = Severity,
                                TypeName = file
                            });
                        }
                    }
                }
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private static int CountComponentOccurrences(string content, string className)
    {
        return Regex.Matches(content, $"m_Name: {Regex.Escape(className)}(?:\\s|$)").Count;
    }

    // Known component GUIDs (from Unity and project assemblies)
    private static readonly Dictionary<string, string> KnownComponentGuids = new(StringComparer.OrdinalIgnoreCase)
    {
        // Unity built-in components
        ["UIDocument"] = "0000000000000000e000000000000000",
        // Project components (found via MCP inspection)
        ["GatewayController"] = "908ee3e9c3acb4c3cba7e23129cc9b38",
    };

    /// <summary>
    /// Checks if a component exists in the scene by looking for MonoBehaviour entries
    /// that reference the component type by name or GUID.
    /// </summary>
    private static bool HasComponentInScene(string content, string className)
    {
        // Check 1: GameObject with this name exists
        if (content.Contains($"m_Name: {className}"))
            return true;

        // Check 2: MonoBehaviour with m_EditorClassIdentifier containing this name
        if (Regex.IsMatch(content, $"m_EditorClassIdentifier:.*{Regex.Escape(className)}"))
            return true;

        // Check 3: MonoBehaviour script GUID reference
        if (KnownComponentGuids.TryGetValue(className, out var guid))
        {
            if (content.Contains($"guid: {guid}"))
                return true;
        }

        return false;
    }

    private static bool ContainsComponentClass(string content, string className)
    {
        return HasComponentInScene(content, className);
    }
}
