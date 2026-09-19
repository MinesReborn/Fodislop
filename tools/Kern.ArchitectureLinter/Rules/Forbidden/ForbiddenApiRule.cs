#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Forbidden;

/// <summary>
/// Forbidden API usage detection. Combines assembly-based checks (Cecil) for Unity API calls
/// and source-based checks (regex) for patterns that don't survive compilation:
/// ServiceLocator, IObjectResolver in gameplay, legacy coroutines, Input, AudioSource,
/// runtime Texture2D, UI Toolkit stylesheet misuse, PlayerPrefs, etc.
/// Ported from scripts/check-forbidden-patterns.sh via check-architecture.js.
/// </summary>
public sealed class ForbiddenApiRule : IRule
{
    private static readonly (string DeclaringType, string MethodName, string Description)[] ForbiddenApis =
    {
        ("UnityEngine.Camera", "get_main", "Camera.main запрещён — используйте инъекцию IGameplayCamera"),
        ("UnityEngine.Object", "FindAnyObjectByType", "FindAnyObjectByType запрещён — используйте DI или сериализованные ссылки"),
        ("UnityEngine.Object", "FindObjectsByType", "FindObjectsByType запрещён — используйте DI или сериализованные ссылки"),
        ("UnityEngine.GameObject", "Find", "GameObject.Find запрещён — используйте DI или сериализованные ссылки"),
        ("UnityEngine.GameObject", "FindWithTag", "GameObject.FindWithTag запрещён — используйте DI"),
        ("UnityEngine.Texture2D", ".ctor", "new Texture2D запрещён — используйте RuntimeTextureFactory"),
    };

    // Source-based patterns: (regex, message, exemptPathPrefix?)
    private static readonly (Regex Pattern, string Message, string? Exempt)[] SourcePatterns =
    {
        (new Regex(@"\bServiceLocator\b", RegexOptions.Compiled),
            "ServiceLocator запрещён — используйте конструктор / DI инъекцию", null),
        (new Regex(@"\bIObjectResolver\b", RegexOptions.Compiled),
            "IObjectResolver в gameplay/UI логике запрещён — только в composition root и фабриках", null),
        (new Regex(@"\bnew InputAction\(", RegexOptions.Compiled),
            "new InputAction() запрещён — настраивайте в InputSystem_Actions.inputactions", null),
        (new Regex(@"\bStartCoroutine\s*\(", RegexOptions.Compiled),
            "StartCoroutine запрещён — используйте UniTask / CancellationToken",
            "Assets/Scripts/Core/Lifecycle/AsyncOperationSupervisor.cs"),
        (new Regex(@"\bInput\.Get", RegexOptions.Compiled),
            "Legacy Input (Input.Get*) запрещён — используйте UnityEngine.InputSystem", null),
        (new Regex(@"\bAudioSource\b", RegexOptions.Compiled),
            "AudioSource запрещён — используйте FMOD Studio (IAudioSystem / AudioSystem)", null),
        (new Regex(@"\bCamera\.main\b", RegexOptions.Compiled),
            "Camera.main запрещён — используйте инъекцию IGameplayCamera", null),
        (new Regex(@"\bCamera\.allCameras\b", RegexOptions.Compiled),
            "Camera.allCameras запрещён — используйте инъекцию IGameplayCamera", null),
        (new Regex(@"\bApplication\.targetFrameRate\b", RegexOptions.Compiled),
            "Application.targetFrameRate запрещён — DisplayManager единственный владелец", null),
        (new Regex(@"\bScreen\.SetResolution\b", RegexOptions.Compiled),
            "Screen.SetResolution запрещён — DisplayManager единственный владелец", null),
        (new Regex(@"\bQualitySettings\.vSyncCount\b", RegexOptions.Compiled),
            "QualitySettings.vSyncCount запрещён — DisplayManager единственный владелец", null),
        (new Regex(@"\bnew Texture2D\s*\(", RegexOptions.Compiled),
            "new Texture2D() запрещён — используйте RuntimeTextureFactory", null),
        (new Regex(@"\bTexture2D\.LoadImage\b", RegexOptions.Compiled),
            "Texture2D.LoadImage запрещён — используйте RuntimeTextureFactory", null),
        (new Regex(@"\.styleSheets\.Add\s*\(\s*Resources\.Load\b", RegexOptions.Compiled),
            "Загрузка стилей через Resources запрещена — используйте PanelSettings.themeUss (@import)", null),
        (new Regex(@"\bnew GameObject\(", RegexOptions.Compiled),
            "new GameObject() запрещён — используйте ISceneObjectFactory", null),
        (new Regex(@"\bGameObject\.Find\b", RegexOptions.Compiled),
            "GameObject.Find запрещён — глобальный поиск по сцене запрещён", null),
        (new Regex(@"\bGameObject\.FindWithTag\b", RegexOptions.Compiled),
            "GameObject.FindWithTag запрещён — глобальный поиск запрещён", null),
        (new Regex(@"\bSceneManager\.LoadScene\s*\(", RegexOptions.Compiled),
            "SceneManager.LoadScene запрещён — используйте BootstrapLifetimeScope.TransitionAsync", null),
        (new Regex(@"\bFindObjectOfType\b", RegexOptions.Compiled),
            "FindObjectOfType запрещён — используйте FindObjectsByType / FindAnyObjectByType", null),
        (new Regex(@"\bFindObjectsOfType\b", RegexOptions.Compiled),
            "FindObjectsOfType запрещён — используйте FindObjectsByType / FindAnyObjectByType", null),
        (new Regex(@"\bPlayerPrefs\.", RegexOptions.Compiled),
            "PlayerPrefs запрещён — используйте ClientConfigManager (client_config.json)", null),
    };

    // Files that are legitimate owners of specific APIs (file -> patterns they can use)
    private static readonly Dictionary<string, string[]> LegitimateOwners = new(StringComparer.Ordinal)
    {
        // DisplayManager owns display settings
        ["Assets/Scripts/Rendering/Settings/DisplayManager.cs"] = new[] {
            "Application.targetFrameRate", "Screen.SetResolution", "QualitySettings.vSyncCount"
        },
        // DisplaySettings defines the values (doesn't use them)
        ["Assets/Scripts/Core/Interfaces/Contracts/Settings/DisplaySettings.cs"] = new[] {
            "Application.targetFrameRate", "Screen.SetResolution", "QualitySettings.vSyncCount"
        },
        // GameplayCamera wraps Camera.main
        ["Assets/Scripts/Core/Rendering/GameplayCamera.cs"] = new[] { "Camera.main" },
        // SceneObjectFactory creates GameObjects
        ["Assets/Scripts/Core/Lifecycle/SceneObjectFactory.cs"] = new[] {
            "new GameObject", "IObjectResolver"
        },
        // RuntimeTextureFactory creates textures
        ["Assets/Scripts/AssetPipeline/Loading/RuntimeTextureFactory.cs"] = new[] { "new Texture2D" },
        // Токены auth/VK живут в PlayerPrefs: перенос в ClientConfig требует
        // PlayMode-проверки login/logout, без неё миграцию не делать.
        ["Assets/Scripts/Networking/Auth/AuthTokenManager.cs"] = new[] { "PlayerPrefs" },
        ["Assets/Scripts/Networking/Auth/VKAuthService.cs"] = new[] { "PlayerPrefs" },
        // Tests need to create objects
        ["Assets/Scripts/Tests/Editor/Core/LocalPlayerStateTests.cs"] = new[] { "new GameObject" },
        ["Assets/Scripts/Tests/Editor/Core/ProductionSceneContractValidatorTests.cs"] = new[] { "new GameObject" },
        ["Assets/Scripts/Tests/Editor/Core/DisplayOutputProfileTests.cs"] = new[] { "new GameObject" },
        ["Assets/Scripts/Tests/Editor/Core/LocalPlayerStateFuzzTests.cs"] = new[] { "new GameObject" },
    };

    public string Id => "KERN-FORBIDDEN-API";
    public string Description => "Forbidden API usage detection";
    public RuleSeverity Severity => RuleSeverity.Error;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();

        // Assembly-based checks ( Cecil )
        foreach (var assembly in assemblies)
        {
            if (context.ShouldExclude(assembly.Name.Name))
                continue;

            if (IsEditorAssembly(assembly.Name.Name) || IsTestAssembly(assembly.Name.Name))
                continue;

            foreach (var type in assembly.MainModule.Types)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ScanType(type, violations);
            }
        }

        // Source-based checks (regex on .cs files)
        var scriptsRoot = Path.Combine(context.ProjectRoot, "Assets", "Scripts");
        var editorRoot = Path.Combine(context.ProjectRoot, "Assets", "Editor");

        foreach (var file in SourceScanner.EnumerateAllCsFiles(scriptsRoot, editorRoot))
        {
            var relative = SourceScanner.GetProjectRelativePath(context.ProjectRoot, file);
            if (IsSourceExcluded(relative)) continue;

            var source = File.ReadAllText(file);
            var stripped = SourceScanner.StripComments(source);
            var ownerExemptions = LegitimateOwners.TryGetValue(relative, out var ex) ? ex : null;

            foreach (var (pattern, message, exempt) in SourcePatterns)
            {
                if (exempt != null && relative == exempt)
                    continue;

                // Skip if file is a legitimate owner of this API
                if (ownerExemptions != null && IsLegitimateOwner(pattern, ownerExemptions))
                    continue;

                foreach (Match match in pattern.Matches(stripped))
                {
                    var line = source.Substring(0, match.Index).Count(c => c == '\n') + 1;
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = message,
                        Severity = Severity,
                        AssemblyName = null,
                        TypeName = $"{relative}:{line}",
                        MemberName = null
                    });
                }
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private static bool IsEditorAssembly(string assemblyName)
    {
        return assemblyName.EndsWith(".Editor", StringComparison.OrdinalIgnoreCase) ||
               assemblyName.EndsWith("-Editor", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTestAssembly(string assemblyName)
    {
        return assemblyName.StartsWith("Kern.Tests.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSourceExcluded(string relative)
    {
        return relative.StartsWith("Assets/Scripts/VContainer/") ||
               relative.StartsWith("Assets/Plugins/") ||
               relative.StartsWith("Packages/");
    }

    private static bool IsLegitimateOwner(Regex pattern, string[] exemptions)
    {
        var patternStr = pattern.ToString().Replace(@"\", "").Replace("\\", "");
        foreach (var exemption in exemptions)
        {
            if (patternStr.Contains(exemption))
                return true;
        }
        return false;
    }

    private void ScanType(TypeDefinition type, List<RuleViolation> violations)
    {
        foreach (var method in type.Methods)
        {
            if (!method.HasBody)
                continue;

            foreach (var forbidden in ForbiddenApis)
            {
                if (IsAllowedOwner(type, forbidden.DeclaringType, forbidden.MethodName))
                {
                    continue;
                }

                if (CecilAssemblyScanner.CallsMethod(method, forbidden.DeclaringType, forbidden.MethodName))
                {
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = forbidden.Description,
                        Severity = Severity,
                        AssemblyName = type.Module.Assembly.Name.Name,
                        TypeName = type.FullName,
                        MemberName = method.Name
                    });
                }
            }
        }

        foreach (var nested in type.NestedTypes)
            ScanType(nested, violations);
    }

    private static bool IsAllowedOwner(
        TypeDefinition type,
        string forbiddenDeclaringType,
        string forbiddenMethodName)
    {
        if (forbiddenDeclaringType == "UnityEngine.Camera" &&
            forbiddenMethodName == "get_main" &&
            type.FullName == "Kern.Core.GameplayCamera")
        {
            return true;
        }

        return forbiddenDeclaringType == "UnityEngine.Texture2D" &&
               forbiddenMethodName == ".ctor" &&
               type.FullName == "Kern.RuntimeTextureFactory";
    }
}
