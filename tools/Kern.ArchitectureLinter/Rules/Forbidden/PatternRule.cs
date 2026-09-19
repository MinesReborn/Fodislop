using Kern.ArchitectureLinter.Core;
using Mono.Cecil;
using System.Text.RegularExpressions;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Forbidden;

/// <summary>
/// Detects forbidden architectural patterns in C# code.
/// Replaces singleton Instance, ServiceLocator, IObjectResolver in gameplay, etc.
/// </summary>
public sealed class PatternRule : IRule
{
    private static readonly (Regex Pattern, string Name, string? AllowPath, string? AllowContent)[] Rules =
    {
        (new Regex(@"\b(?:StageAsync|CommitStagedAsync|DiscardStagedAsync|RestartCurrentAsync)\b"), "branching/staged scene lifecycle", null, null),
        (new Regex(@"\b(?:ContentSceneRoot|SceneInjectionBridge|LifecycleGraph|LifecycleParticipant|WorldSessionLifecycle)\b"), "removed lifecycle infrastructure", null, null),
        (new Regex(@"Transform\?\s+managerObject|_servicesRoot\.Find\(|transform\.Find\("), "runtime composition-root name lookup (use serialized typed references)", @"^(Assets/Scripts/VContainer/|Assets/Scripts/Tests/|Assets/Scripts/Editor/Validation/ManagerContractMigrator\.cs|Assets/Scripts/(Game|Rendering|UI|World)/)", null),
        (new Regex(@"TryResolve<|TryResolve\s*\("), "DI fallback resolution (use required constructor/explicit dependency)", @"^(Assets/Scripts/Tests/|Assets/Scripts/VContainer/|Assets/Scripts/Core/Bootstrap/\w+LifetimeScope\.cs$)", null),
        (new Regex(@"using\s+Kern\.UI(?:\.|;)|using\s+Kern\.Game\.Managers;"), "networking layer references presentation/game manager namespaces", @"^(?!Assets/Scripts/Networking/)", null),
        (new Regex(@"\b(?:SceneCoordinator|ISceneCoordinator|SceneStartup|ISceneEntryPoint)\b"), "removed scene DI proxy", null, null),
        (new Regex(@"RegisterComponentOnNewGameObject\b"), "runtime fallback manager construction", @"^Assets/Scripts/VContainer/", null),
        (new Regex(@"\b(?:GlobalChatUI|InventoryView|PlayerHUDView|MinimapController|WorldMapController|PauseMenu|FloatingChatManager)\b"), "packet processor depends directly on UI", @"^(?!Assets/Scripts/Networking/Processors/)", null),
        (new Regex(@"FindAnyObjectByType\s*<"), "global runtime object lookup", @"^(Assets/Editor/|Assets/Scripts/Editor/|Assets/Scripts/VContainer/|Assets/Scripts/Tests/)", null),
        (new Regex(@"public\s+static\s+[A-Za-z0-9_<>?.]+\s+Instance\s*([({;=]|=>)"), "static Instance singleton", null, null),
        (new Regex(@"ServiceLocator"), "ServiceLocator access", null, null),
        (new Regex(@"(?:private|protected|public)\s+(?:readonly\s+)?IObjectResolver\s+_?[A-Za-z0-9_]+"), "IObjectResolver injected into runtime logic (use direct dependencies; resolver belongs to composition roots/factories)", @"^(Assets/Scripts/Core/(?:BootstrapLifetimeScope|GameBootstrap|GameLifetimeScope)\.cs|Assets/Scripts/Core/Lifecycle/SceneObjectFactory\.cs)$", null),
        (new Regex(@"new\s+InputAction\("), "ad-hoc InputAction", null, null),
        (new Regex(@"FitFieldDimensionsToAtlasBudget"), "fractional lighting-field fitting", null, null),
        (new Regex(@"Mathf\.Approximately\([^,]*CameraOrthoSize"), "exact camera zoom cache comparison", null, null),
        (new Regex(@"Camera\.main"), "Camera.main outside GameplayCamera", @"^Assets/Scripts/Core/(?:Rendering/)?GameplayCamera\.cs$", null),
        (new Regex(@"Application\.targetFrameRate\s*="), "FPS cap outside DisplayManager", @"^Assets/Scripts/Rendering/(?:Settings/)?DisplayManager\.cs$", null),
        (new Regex(@"QualitySettings\.vSyncCount\s*="), "VSync ownership outside DisplayManager", @"^Assets/Scripts/Rendering/(?:Settings/)?DisplayManager\.cs$", null),
        (new Regex(@"new\s+Texture2D(Array)?\s*\("), "runtime Texture2D construction outside RuntimeTextureFactory", @"^(Assets/(?:Scripts/)?Editor/|Assets/Scripts/AssetPipeline/(?:Loading/)?RuntimeTextureFactory\.cs|Assets/Scripts/Tests/)", null),
        (new Regex(@"\.LoadImage\s*\("), "runtime image decoding outside RuntimeTextureFactory", @"^(Assets/(?:Scripts/)?Editor/|Assets/Scripts/AssetPipeline/(?:Loading/)?RuntimeTextureFactory\.cs|Assets/Scripts/Tests/)", null),
        (new Regex(@"\.styleSheets\.Add\s*\("), "controller-local UI Toolkit stylesheet", null, null),
        (new Regex(@"new\s+Vector2\s*\([^,]+,\s*Screen\.height\s*-"), "manual screen-to-panel Y flip", null, null),
        (new Regex(@"\.style\.(width|height)\s*=[^;]*Screen\.(width|height)"), "UI root sized from Screen dimensions", null, null),
        (new Regex(@"LightingCascadeAtlasLimit\s*<=\s*256\s*\?"), "duplicated radiance-cascade count policy", null, @"return atlasDimension <= 256 \? 3 : 4;"),
        (new Regex(@"(FindAnyObjectByType|FindFirstObjectByType)<Camera>"), "ad-hoc gameplay camera lookup", @"^Assets/Scripts/Core/(?:Rendering/)?GameplayCamera\.cs$", null),
        (new Regex(@"AddComponent<[A-Za-z0-9_]*(Manager|Service)>"), "manual manager/service construction", null, null),
        (new Regex(@"(Config|config)\.GraphicsPreset\s*="), "graphics preset mutation outside client config owners", @"^(Assets/Scripts/Core/Configuration/ClientConfig(?:Defaults|Manager|Loader)\.cs|Assets/Scripts/World/Lighting/(?:(?:Config|Core)/)?Lighting(ConfigHolder|Engine)\.cs)$", null),
        (new Regex(@"(Config|config)\.GraphicsQualitySettings\s*="), "graphics quality snapshot mutation outside client config owners", @"^Assets/Scripts/Core/Configuration/ClientConfig(?:Defaults|Manager|Loader)\.cs$", null),
        (new Regex(@"QualitySettings\.antiAliasing\s*="), "MSAA ownership outside LightingEngine", @"^Assets/Scripts/World/Lighting/(?:Core/)?LightingEngine\.cs$", null),
        (new Regex(@"QualitySettings\.SetQualityLevel\s*\("), "Unity quality-level ownership outside LightingEngine", @"^Assets/Scripts/World/Lighting/(?:Core/)?LightingEngine\.cs$", null),
        (new Regex(@"\.renderScale\s*="), "URP render-scale ownership outside LightingEngine", @"^Assets/Scripts/World/Lighting/(?:Core/)?LightingEngine\.cs$", null),
        (new Regex(@"PlayerPrefs\.(Set|Delete|Save)"), "settings persistence in PlayerPrefs", @"^(Assets/Editor/.*|Assets/Scripts/Networking/Auth/(AuthTokenManager|VKAuthService|VkAuthService)\.cs|Assets/Scripts/UI/(AuthGate\.cs|GatewayController\.cs|Gateway/AuthGate\.cs|Gateway/GatewayController\.cs))$", null),
        (new Regex(@"(slider|toggle|dropdown|quality|preset)\.value\s*="), "notifying UI settings refresh", null, null),
        (new Regex(@"PauseMenuUIFactory\.CreateSlider\s*\("), "unbound settings slider (use PauseMenuUIFactory.CreateBoundSlider)", @"^Assets/Scripts/UI/Settings/PauseMenuUIFactory\.cs$", null),
        (new Regex(@"ServerConfig[^;]*(Master|Sfx|Music|Ambience|Voice|Ui)Volume"), "audio volume in ServerConfig", null, null),
        (new Regex(@"_clientConfig\.Config\.[A-Za-z0-9_]+\s*="), "direct ClientConfig field mutation", null, null),
        (new Regex(@"_clientConfig\.Save\s*\("), "unowned ClientConfig persistence", @"^(Assets/Scripts/Rendering/(?:Settings/)?GraphicsSettingsController\.cs|Assets/Scripts/Rendering/(?:Settings/)?DisplayManager\.cs|Assets/Scripts/World/Lighting/(?:(?:Config|Core)/)?Lighting(ConfigHolder|Engine)\.cs|Assets/Scripts/Core/Configuration/Contracts/ConfigSaveScheduler\.cs)$", null),
        (new Regex(@"(FindAnyObjectByType|FindFirstObjectByType|FindObjectsByType)<Canvas>"), "screen-space uGUI Canvas lookup", null, null),
        (new Regex(@"using\s+UnityEngine\.UI;"), "screen-space uGUI namespace", null, null),
        (new Regex(@"new\s+GameObject\("), "runtime GameObject construction outside SceneObjectFactory", @"^(Assets/Editor/.*|Assets/Scripts/Editor/.*|Assets/Scripts/Tests/.*|Assets/Scripts/Core/Lifecycle/SceneObjectFactory\.cs|Assets/Scripts/Game/.*)$", null),
        (new Regex(@":\s*new\s+GameObject\("), "fallback GameObject construction when DI is missing", @"^(Assets/Editor/.*|Assets/Scripts/Editor/.*|Assets/Scripts/Tests/.*|Assets/Scripts/Core/Lifecycle/SceneObjectFactory\.cs)$", null),
        (new Regex(@"GameObject\.Find(GameObjectWithTag|GameObjectsWithTag)?\("), "global unscoped GameObject lookup", @"^(Assets/Editor/|Assets/Scripts/Editor/|Assets/Scripts/Tests/)", null),
        (new Regex(@"SceneManager\.LoadScene\("), "synchronous scene loading outside BootstrapLifetimeScope", @"^Assets/Scripts/Tests/", null),
        (new Regex(@"FindObjects?OfType</"), "deprecated FindObject(s)OfType call", null, null),
        (new Regex(@"\bInput\.(GetKey|GetKeyDown|GetKeyUp|GetButton|GetButtonDown|GetMouseButton|mousePosition|GetAxis|anyKey)\b"), "legacy Input Manager call (use UnityEngine.InputSystem)", null, null),
        (new Regex(@"\b(StartCoroutine|StopCoroutine)\s*\("), "legacy MonoBehaviour coroutines (use UniTask)", null, null),
        (new Regex(@"\bAudioSource\b"), "Unity AudioSource usage (FMOD Studio is the sole audio engine)", @"^(Assets/Editor/|Assets/Scripts/Editor/|Assets/Scripts/Tests/)", null),
        (new Regex(@"\bDontDestroyOnLoad\s*\("), "DontDestroyOnLoad outside BootstrapLifetimeScope", @"^(Assets/Editor/|Assets/Scripts/Core/Bootstrap/BootstrapLifetimeScope\.cs|Assets/Scripts/Tests/)", null),
        (new Regex(@"\bScreen\.SetResolution\s*\("), "Screen.SetResolution outside DisplayManager", @"^Assets/Scripts/Rendering/(?:Settings/)?DisplayManager\.cs$", null),
        (new Regex(@"\bThread\.Sleep\s*\("), "blocking Thread.Sleep in gameplay/async code", @"^(Assets/Editor/|Assets/Scripts/Tests/)", null),
        (new Regex(@"\.Forget\s*\("), "unsupervised async operation (use AsyncOperationSupervisor)", @"^Assets/Scripts/Core/Lifecycle/AsyncOperationSupervisor\.cs$", null),
        (new Regex(@"\bclass\s+WorldLayer\s*<"), "WorldLayer implementation outside persistence assembly", @"^Assets/Scripts/World/Persistence/WorldLayer\.cs$", null),
        (new Regex(@"\b(?:FileStream|BinaryReader|BinaryWriter)\b"), "file persistence implementation inside Contracts", @"^(?!Assets/Scripts/Core/Interfaces/Contracts/)", null),
        (new Regex(@"\bclass\s+LocalChatPopup\b"), "disconnected legacy local-chat controller (use GlobalChatUI local channel)", null, null),
        (new Regex(@"\.GetChunk\s*\("), "ambiguous world-layer chunk access (use ReadChunk or GetOrcreateChunk)", null, null),
        (new Regex(@"\bDEVELOPMENT_BUILD\b"), "устаревшая директива DEVELOPMENT_BUILD (UAC0009) — используйте UNITY_ENABLE_CHECKS", null, null),
        (new Regex(@"\bGC\.Collect\s*\("), "manual GC.Collect in runtime gameplay", @"^(Assets/Editor/|Assets/Scripts/Tests/)", null),
        (new Regex(@"\bCamera\.(allCameras|current)\b"), "unmanaged camera lookup (use explicit gameplay camera contract)", null, null),
        (new Regex(@"\bTime\.timeScale\s*="), "unowned Time.timeScale mutation", @"^(Assets/Scripts/UI/(PauseMenu\.cs|Settings/PauseMenu\.cs)|Assets/Scripts/Game/Managers/GameManager\.cs|Assets/Scripts/Tests/)", null),
        (new Regex(@"new\s+(WebClient|HttpClient)\s*\("), "ad-hoc HTTP client (use ClientAssetLoader or UnityWebRequest)", @"^(Assets/Editor/|Assets/Scripts/Tests/)", null),
        (new Regex(@"Shader\.WarmupAllShaders"), "Shader.WarmupAllShaders in URP (throws keyword space assert)", null, null),
        (new Regex(@"_starfieldMaterial\.(?:SetFloat|SetVector|SetColor|SetTexture|SetInt|SetMatrix)\s*\("), "mutation of the serialized Starfield material asset (use the HideAndDontSave runtime clone)", null, null),
        (new Regex(@"\.sharedMaterial\.(?:SetFloat|SetVector|SetColor|SetTexture|SetInt|SetMatrix)\s*\("), "mutation through Renderer.sharedMaterial (clone the material or use MaterialPropertyBlock)", null, null),
        (new Regex(@"GameStartupServices"), "deleted GameStartupServices aggregate (inject startup dependencies directly into GameBootstrap)", @"^Assets/Scripts/Tests/", null),
        (new Regex(@"SceneScopeAuthoring|SceneContractMigration"), "scene auto-fixing editor tools are deleted (use the read-only ProductionSceneContractValidator)", null, null),
        (new Regex(@"PlayerMovementController\.(LocalPlayer|OnLocalPlayerSpawned)"), "static local-player access (resolve ILocalPlayerState)", @"^Assets/Scripts/Core/Interfaces/ILocalPlayerState\.cs$", null),
        (new Regex(@"\b(MenuStarfield|MenuSceneryController)\.Current\b"), "static menu-scenery access (use the MainMenuLifetimeScope serialized contract)", null, null),
        (new Regex(@"\b(PauseMenu\.IsMenuOpen|ChatInput\.IsFocused|ProgrammatorGrid\.IsOpen)\b"), "static UI state access outside the UI layer (compose IInputBlocker)", @"^Assets/Scripts/UI/", null),
        (new Regex(@"\bpartial\s+(?:class|struct|record)\b"), "partial type declaration is forbidden (decompose into separate classes/responsibilities instead)", @"^Assets/Scripts/VContainer/", null),
    };

    public string Id => "KERN-PATTERN";
    public string Description => "Forbidden architectural pattern detection";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;
        var scriptsRoot = Path.Combine(projectRoot, "Assets", "Scripts");
        var editorRoot = Path.Combine(projectRoot, "Assets", "Editor");

        if (!Directory.Exists(scriptsRoot))
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);

        var files = new List<string>();
        foreach (var root in new[] { scriptsRoot, editorRoot })
        {
            if (Directory.Exists(root))
                files.AddRange(SourceScanner.EnumerateCsFiles(root, "Tests", "Plugins", "VContainer", "Editor"));
        }

        foreach (var rule in Rules)
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(projectRoot, file).Replace(Path.DirectorySeparatorChar, '/');
                if (!string.IsNullOrEmpty(rule.AllowPath) &&
                    Regex.IsMatch(relative, rule.AllowPath, RegexOptions.CultureInvariant))
                {
                    continue;
                }

                var content = SourceScanner.StripComments(File.ReadAllText(file));
                var lines = content.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].TrimEnd('\r');
                    if (!rule.Pattern.IsMatch(line))
                        continue;
                    if (!string.IsNullOrEmpty(rule.AllowContent) && Regex.IsMatch(line, rule.AllowContent))
                        continue;

                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = rule.Name,
                        Severity = Severity,
                        AssemblyName = relative,
                        TypeName = relative,
                        Line = i + 1
                    });
                }
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
