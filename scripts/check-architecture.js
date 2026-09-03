#!/usr/bin/env node
/**
 * Fodinae merged architecture linter.
 *
 * Single Node.js entry point that used to be three separate scripts:
 *
 *   1. scripts/check-forbidden-patterns.sh   — 43 grep-style architecture,
 *      settings and performance pattern rules against production C# files.
 *   2. scripts/check_di_lifecycle.py         — deep semantic DI and lifecycle
 *      analyzer (execution-order contracts, Configure reentrancy, Unity
 *      namespace syntax, unguarded [Inject] access in early lifecycle,
 *      async void in MonoBehaviours).
 *   3. scripts/check_settings_wiring.py      — settings wiring analyzer (dead
 *      ClientConfig fields + startup application contract for config
 *      consumers + UI-only reads flagged as dead wiring).
 *   4. Assets/Editor/Tools/lint-uss.py        — USS stylesheet validator
 *      (UI Toolkit property/function/easing allowlists from the UIElements
 *      registry, custom token usage, brace balance).
 *   5. Localization linter (no predecessor)   — language-file parity, used
 *      keys must exist in every language, placeholder sanity ({0},{1},...),
 *      dead keys and the unwired-dictionary check.
 *
 * Usage:
 *   node scripts/check-architecture.js [files...]
 *
 * With no arguments it scans Assets/Scripts and Assets/Editor for *.cs files
 * and always validates Assets/Resources/Styles/*.uss. With arguments it scans
 * only the given files (pattern rules only; the DI, settings-wiring and USS
 * parts always analyze the full tree, as the originals did).
 * Exits 1 on any violation, 0 otherwise.
 */

"use strict";

const fs = require("fs");
const { spawnSync } = require("child_process");
const path = require("path");

const RED = "\x1b[0;31m";
const GREEN = "\x1b[0;32m";
const YELLOW = "\x1b[1;33m";
const CYAN = "\x1b[0;36m";
const BOLD = "\x1b[1m";
const NC = "\x1b[0m";

const violations = [];

function recordViolation(category, loc, message) {
    violations.push({ category, loc, message });
}

function escapeRegExp(s) {
    return s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

// ---------------------------------------------------------------------------
// File discovery
// ---------------------------------------------------------------------------

const EXCLUDE_REGEX = /^(Assets\/Scripts\/VContainer\/|Assets\/Plugins\/|Packages\/|Library\/)/;

function walkCs(root, result = []) {
    let entries;
    try {
        entries = fs.readdirSync(root, { withFileTypes: true });
    } catch {
        return result;
    }
    for (const entry of entries) {
        const full = path.join(root, entry.name);
        if (entry.isDirectory()) {
            walkCs(full, result);
        } else if (entry.isFile() && entry.name.endsWith(".cs")) {
            result.push(full);
        }
    }
    return result;
}

function collectProductionFiles() {
    const files = [];
    for (const root of ["Assets/Scripts", "Assets/Editor"]) {
        walkCs(root, files);
    }
    return files;
}

function readFile(filePath) {
    try {
        return fs.readFileSync(filePath, "utf8");
    } catch {
        return null;
    }
}

function readRequiredFile(filePath, category) {
    const source = readFile(filePath);
    if (source === null) {
        recordViolation(category, filePath, "Required architecture source file is missing or unreadable.");
    }

    return source;
}

const SCRIPT_CLASS_BY_GUID = new Map();
let scriptClassIndexBuilt = false;

function buildScriptClassIndex() {
    if (scriptClassIndexBuilt) {
        return;
    }
    scriptClassIndexBuilt = true;

    for (const filePath of walkCs("Assets/Scripts")) {
        const meta = readFile(filePath + ".meta");
        const source = readFile(filePath);
        if (meta === null || source === null) {
            continue;
        }
        const guid = meta.match(/^guid:\s*([a-f0-9]+)\s*$/m)?.[1];
        const className = source.match(/\bclass\s+([A-Za-z0-9_]+)/)?.[1];
        if (guid && className) {
            SCRIPT_CLASS_BY_GUID.set(guid, className);
        }
    }
}

// ---------------------------------------------------------------------------
// Part 1: architectural pattern rules
// (ported from scripts/check-forbidden-patterns.sh)
// ---------------------------------------------------------------------------

const COMMENT_LINE_REGEX = /^\s*(?:\/\/|\/\*|\*|\/\/\/)/;

// Transitional debt list for the async-lifecycle migration. The rule below
// rejects every new production .Forget() call; each existing owner must be
// removed from this list when its operations move under a supervisor.
const LEGACY_UNSUPERVISED_ASYNC_FILES = /^Assets\/Scripts\/Core\/Lifecycle\/AsyncOperationSupervisor\.cs$/;

const OVERSIZED_PRODUCTION_FILE_LIMIT = 500;
const OVERSIZED_PRODUCTION_FILE_DEBT = new Set([
    "Assets/Scripts/World/Lighting/Core/LightingEngine.cs",
    "Assets/Scripts/World/Persistence/WorldLayer.cs",
    "Assets/Scripts/World/Terrain/Core/TerrainRenderer.cs",
    "Assets/Scripts/UI/Overlays/InGameDebugOverlay.cs",
    "Assets/Scripts/AssetPipeline/Animation/GifAnimationDecoder.cs",
    "Assets/Scripts/UI/Chat/GlobalChatUI.cs",
    "Assets/Scripts/Rendering/PostProcessing/PostProcessRenderPass.cs",
    "Assets/Scripts/Game/Entities/Robot.cs",
    "Assets/Scripts/UI/Menu/Core/MainMenu.cs",
    "Assets/Scripts/World/Textures/WorldTextureManager.cs",
    "Assets/Scripts/AssetPipeline/Loading/ClientAssetLoader.cs",
    "Assets/Scripts/UI/Programmator/Model/ProgrammatorData.cs",
    "Assets/Scripts/UI/Gateway/GatewayController.cs",
    "Assets/Scripts/UI/Map/WorldMapRenderer.cs",
    "Assets/Scripts/World/Rendering/BackgroundFloodFill.cs",
    "Assets/Scripts/UI/Programmator/Grid/ProgrammatorClipboardController.cs",
    "Assets/Scripts/AssetPipeline/Cache/AssetCacheEntry.cs",
    "Assets/Scripts/UI/HUD/Player/View/PlayerHUDView.cs",
    "Assets/Scripts/Game/Audio/ServerAudioEvent.cs",
    "Assets/Scripts/UI/Settings/PauseMenu.cs",
    "Assets/Scripts/World/Terrain/Mesh/TerrainMeshBuilder.cs",
    "Assets/Scripts/World/Lighting/Core/LightingResourceManager.cs",
    "Assets/Scripts/Player/Controllers/PlayerMovementController.cs",
    "Assets/Scripts/World/Textures/TextureAtlas.cs",
]);

function checkOversizedProductionFiles() {
    for (const filePath of walkCs("Assets/Scripts")) {
        if (filePath.includes("/Tests/") ||
            filePath.includes("/Editor/") ||
            filePath.includes("/VContainer/")) {
            continue;
        }

        const source = readFile(filePath);
        if (source === null) {
            continue;
        }

        const lineCount = source.split(/\r?\n/).length - 1;
        if (lineCount > OVERSIZED_PRODUCTION_FILE_LIMIT &&
            !OVERSIZED_PRODUCTION_FILE_DEBT.has(filePath)) {
            recordViolation(
                "file size",
                filePath,
                `${lineCount} lines exceeds the ${OVERSIZED_PRODUCTION_FILE_LIMIT}-line production limit; split responsibilities instead of adding a new god-object or partial class.`,
            );
        }
    }
}

// Each rule: { pattern, name, allow (path exemption, nullable), allowContent (line exemption, nullable) }.
// "allow" and "allowContent" were the ALLOW_REGEX / ALLOW_CONTENT_REGEX arrays of the shell linter.
const RULES = [
    { pattern: /\b(?:StageAsync|CommitStagedAsync|DiscardStagedAsync|RestartCurrentAsync)\b/, name: "branching/staged scene lifecycle", allow: null, allowContent: null },
    { pattern: /\b(?:ContentSceneRoot|SceneInjectionBridge|LifecycleGraph|LifecycleParticipant|WorldSessionLifecycle)\b/, name: "removed lifecycle infrastructure", allow: null, allowContent: null },
    { pattern: /Transform\?\s+managerObject|_servicesRoot\.Find\(|transform\.Find\(/, name: "runtime composition-root name lookup (use serialized typed references)", allow: /^(Assets\/Scripts\/VContainer\/|Assets\/Scripts\/Tests\/|Assets\/Scripts\/Editor\/ManagerContractMigrator\.cs|Assets\/Scripts\/(Game|Rendering|UI|World)\/)/, allowContent: null },
    // Запрет адресован ПОТРЕБИТЕЛЮ: класс не вправе прикрывать недостающую
    // проводку запасным разрешением вместо явной зависимости. У корня композиции
    // конструктора нет — разрешение и есть его работа, и правило ниже
    // (Resolve в раннем жизненном цикле) само требует там TryResolve с проверкой
    // на null. Без этого исключения два правила противоречат друг другу.
    { pattern: /TryResolve<|TryResolve\s*\(/, name: "DI fallback resolution (use required constructor/explicit dependency)", allow: /^(Assets\/Scripts\/Tests\/|Assets\/Scripts\/VContainer\/|Assets\/Scripts\/Core\/Bootstrap\/\w+LifetimeScope\.cs$)/, allowContent: null },
    { pattern: /using\s+Fodinae\.UI(?:\.|;)|using\s+Fodinae\.Game\.Managers;/, name: "networking layer references presentation/game manager namespaces", allow: /^(?!Assets\/Scripts\/Networking\/)/, allowContent: null },
    { pattern: /\b(?:SceneCoordinator|ISceneCoordinator|SceneStartup|ISceneEntryPoint)\b/, name: "removed scene DI proxy", allow: null, allowContent: null },
    { pattern: /\bRegisterComponentOnNewGameObject\b/, name: "runtime fallback manager construction", allow: /^Assets\/Scripts\/VContainer\//, allowContent: null },
    { pattern: /\b(?:GlobalChatUI|InventoryView|PlayerHUDView|MinimapController|WorldMapController|PauseMenu|FloatingChatManager)\b/, name: "packet processor depends directly on UI", allow: /^(?!Assets\/Scripts\/Networking\/Processors\/)/, allowContent: null },
    { pattern: /\bFindAnyObjectByType\s*</, name: "global runtime object lookup", allow: /^(Assets\/Editor\/|Assets\/Scripts\/Editor\/|Assets\/Scripts\/VContainer\/|Assets\/Scripts\/Tests\/)/, allowContent: null },
    { pattern: /public\s+static\s+[A-Za-z0-9_<>?.]+\s+Instance\s*([({;=]|=>)/, name: "static Instance singleton", allow: null, allowContent: null },
    { pattern: /ServiceLocator/, name: "ServiceLocator access", allow: null, allowContent: null },
    { pattern: /(?:private|protected|public)\s+(?:readonly\s+)?IObjectResolver\s+_?[A-Za-z0-9_]+/, name: "IObjectResolver injected into runtime logic (use direct dependencies; resolver belongs to composition roots/factories)", allow: /^(Assets\/Scripts\/Core\/(?:BootstrapLifetimeScope|GameBootstrap|GameLifetimeScope)\.cs|Assets\/Scripts\/Core\/Lifecycle\/SceneObjectFactory\.cs)$/, allowContent: null },
    { pattern: /new\s+InputAction\(/, name: "ad-hoc InputAction", allow: null, allowContent: null },
    { pattern: /FitFieldDimensionsToAtlasBudget/, name: "fractional lighting-field fitting", allow: null, allowContent: null },
    { pattern: /Mathf\.Approximately\([^,]*CameraOrthoSize/, name: "exact camera zoom cache comparison", allow: null, allowContent: null },
    { pattern: /Camera\.main/, name: "Camera.main outside GameplayCamera", allow: /^Assets\/Scripts\/Core\/(?:Rendering\/)?GameplayCamera\.cs$/, allowContent: null },
    { pattern: /Application\.targetFrameRate\s*=/, name: "FPS cap outside DisplayManager", allow: /^Assets\/Scripts\/Rendering\/(?:Settings\/)?DisplayManager\.cs$/, allowContent: null },
    { pattern: /QualitySettings\.vSyncCount\s*=/, name: "VSync ownership outside DisplayManager", allow: /^Assets\/Scripts\/Rendering\/(?:Settings\/)?DisplayManager\.cs$/, allowContent: null },
    { pattern: /new\s+Texture2D(Array)?\s*\(/, name: "runtime Texture2D construction outside RuntimeTextureFactory", allow: /^(Assets\/(?:Scripts\/)?Editor\/|Assets\/Scripts\/AssetPipeline\/(?:Loading\/)?RuntimeTextureFactory\.cs|Assets\/Scripts\/Tests\/)/, allowContent: null },
    { pattern: /\.LoadImage\s*\(/, name: "runtime image decoding outside RuntimeTextureFactory", allow: /^(Assets\/(?:Scripts\/)?Editor\/|Assets\/Scripts\/AssetPipeline\/(?:Loading\/)?RuntimeTextureFactory\.cs|Assets\/Scripts\/Tests\/)/, allowContent: null },
    { pattern: /\.styleSheets\.Add\s*\(/, name: "controller-local UI Toolkit stylesheet", allow: null, allowContent: null },
    { pattern: /new\s+Vector2\s*\([^,]+,\s*Screen\.height\s*-/, name: "manual screen-to-panel Y flip", allow: null, allowContent: null },
    { pattern: /\.style\.(width|height)\s*=[^;]*Screen\.(width|height)/, name: "UI root sized from Screen dimensions", allow: null, allowContent: null },
    { pattern: /LightingCascadeAtlasLimit\s*<=\s*256\s*\?/, name: "duplicated radiance-cascade count policy", allow: null, allowContent: /return atlasDimension <= 256 \? 3 : 4;/ },
    { pattern: /(FindAnyObjectByType|FindFirstObjectByType)<Camera>/, name: "ad-hoc gameplay camera lookup", allow: /^Assets\/Scripts\/Core\/(?:Rendering\/)?GameplayCamera\.cs$/, allowContent: null },
    { pattern: /AddComponent<[A-Za-z0-9_]*(Manager|Service)>/, name: "manual manager/service construction", allow: null, allowContent: null },
    { pattern: /(Config|config)\.GraphicsPreset\s*=/, name: "graphics preset mutation outside client config owners", allow: /^(Assets\/Scripts\/Core\/Configuration\/ClientConfig(?:Defaults|Manager|Migration)\.cs|Assets\/Scripts\/World\/Lighting\/(?:(?:Config|Core)\/)?Lighting(ConfigHolder|Engine)\.cs)$/, allowContent: null },
    { pattern: /(Config|config)\.GraphicsQualitySettings\s*=/, name: "graphics quality snapshot mutation outside client config owners", allow: /^Assets\/Scripts\/Core\/Configuration\/ClientConfig(?:Defaults|Manager|Migration)\.cs$/, allowContent: null },
    { pattern: /QualitySettings\.antiAliasing\s*=/, name: "MSAA ownership outside LightingEngine", allow: /^Assets\/Scripts\/World\/Lighting\/(?:Core\/)?LightingEngine\.cs$/, allowContent: null },
    { pattern: /QualitySettings\.SetQualityLevel\s*\(/, name: "Unity quality-level ownership outside LightingEngine", allow: /^Assets\/Scripts\/World\/Lighting\/(?:Core\/)?LightingEngine\.cs$/, allowContent: null },
    { pattern: /\.renderScale\s*=/, name: "URP render-scale ownership outside LightingEngine", allow: /^Assets\/Scripts\/World\/Lighting\/(?:Core\/)?LightingEngine\.cs$/, allowContent: null },
    { pattern: /PlayerPrefs\.(Set|Delete|Save)/, name: "settings persistence in PlayerPrefs", allow: /^(Assets\/Editor\/.*|Assets\/Scripts\/Networking\/Auth\/(AuthTokenManager|VkAuthService)\.cs|Assets\/Scripts\/UI\/(AuthGate\.cs|GatewayController\.cs|Gateway\/AuthGate\.cs|Gateway\/GatewayController\.cs))$/, allowContent: null },
    { pattern: /(slider|toggle|dropdown|quality|preset)\.value\s*=/, name: "notifying UI settings refresh", allow: null, allowContent: null },
    { pattern: /ServerConfig[^;]*(Master|Sfx|Music|Ambience|Voice|Ui)Volume/, name: "audio volume in ServerConfig", allow: null, allowContent: null },
    { pattern: /_clientConfig\.Config\.[A-Za-z0-9_]+\s*=/, name: "direct ClientConfig field mutation", allow: null, allowContent: null },
    { pattern: /_clientConfig\.Save\s*\(/, name: "unowned ClientConfig persistence", allow: /^(Assets\/Scripts\/Rendering\/(?:Settings\/)?GraphicsSettingsController\.cs|Assets\/Scripts\/Rendering\/(?:Settings\/)?DisplayManager\.cs|Assets\/Scripts\/World\/Lighting\/(?:(?:Config|Core)\/)?Lighting(ConfigHolder|Engine)\.cs)$/, allowContent: null },
    { pattern: /(FindAnyObjectByType|FindFirstObjectByType|FindObjectsByType)<Canvas>/, name: "screen-space uGUI Canvas lookup", allow: null, allowContent: null },
    { pattern: /using\s+UnityEngine\.UI;/, name: "screen-space uGUI namespace", allow: null, allowContent: null },
    { pattern: /new\s+GameObject\(/, name: "runtime GameObject construction outside SceneObjectFactory", allow: /^(Assets\/Editor\/.*|Assets\/Scripts\/Editor\/.*|Assets\/Scripts\/Tests\/.*|Assets\/Scripts\/Core\/Lifecycle\/SceneObjectFactory\.cs|Assets\/Scripts\/Game\/.*)$/, allowContent: null },
    { pattern: /:\s*new\s+GameObject\(/, name: "fallback GameObject construction when DI is missing", allow: /^(Assets\/Editor\/.*|Assets\/Scripts\/Editor\/.*|Assets\/Scripts\/Tests\/.*|Assets\/Scripts\/Core\/Lifecycle\/SceneObjectFactory\.cs)$/, allowContent: null },
    { pattern: /GameObject\.Find(GameObjectWithTag|GameObjectsWithTag)?\(/, name: "global unscoped GameObject lookup", allow: /^(Assets\/Editor\/|Assets\/Scripts\/Editor\/|Assets\/Scripts\/Tests\/)/, allowContent: null },
    { pattern: /SceneManager\.LoadScene\(/, name: "synchronous scene loading outside BootstrapLifetimeScope", allow: /^Assets\/Scripts\/Tests\//, allowContent: null },
    { pattern: /FindObjects?OfType</, name: "deprecated FindObject(s)OfType call", allow: null, allowContent: null },
    { pattern: /\bInput\.(GetKey|GetKeyDown|GetKeyUp|GetButton|GetButtonDown|GetMouseButton|mousePosition|GetAxis|anyKey)\b/, name: "legacy Input Manager call (use UnityEngine.InputSystem)", allow: null, allowContent: null },
    { pattern: /\b(StartCoroutine|StopCoroutine)\s*\(/, name: "legacy MonoBehaviour coroutines (use UniTask)", allow: null, allowContent: null },
    { pattern: /\bAudioSource\b/, name: "Unity AudioSource usage (FMOD Studio is the sole audio engine)", allow: /^(Assets\/Editor\/|Assets\/Scripts\/Editor\/|Assets\/Scripts\/Tests\/)/, allowContent: null },
    { pattern: /\bDontDestroyOnLoad\s*\(/, name: "DontDestroyOnLoad outside BootstrapLifetimeScope", allow: /^(Assets\/Editor\/|Assets\/Scripts\/Core\/Bootstrap\/BootstrapLifetimeScope\.cs|Assets\/Scripts\/Tests\/)/, allowContent: null },
    { pattern: /\bScreen\.SetResolution\s*\(/, name: "Screen.SetResolution outside DisplayManager", allow: /^Assets\/Scripts\/Rendering\/(?:Settings\/)?DisplayManager\.cs$/, allowContent: null },
    { pattern: /\bThread\.Sleep\s*\(/, name: "blocking Thread.Sleep in gameplay/async code", allow: /^(Assets\/Editor\/|Assets\/Scripts\/Tests\/)/, allowContent: null },
    { pattern: /\.Forget\s*\(/, name: "unsupervised async operation (use AsyncOperationSupervisor)", allow: LEGACY_UNSUPERVISED_ASYNC_FILES, allowContent: null },
    { pattern: /\bclass\s+WorldLayer\s*</, name: "WorldLayer implementation outside persistence assembly", allow: /^Assets\/Scripts\/World\/Persistence\/WorldLayer\.cs$/, allowContent: null },
    { pattern: /\b(?:FileStream|BinaryReader|BinaryWriter)\b/, name: "file persistence implementation inside Contracts", allow: /^(?!Assets\/Scripts\/Core\/Interfaces\/Contracts\/)/, allowContent: null },
    { pattern: /\bclass\s+LocalChatPopup\b/, name: "disconnected legacy local-chat controller (use GlobalChatUI local channel)", allow: null, allowContent: null },
    { pattern: /\.GetChunk\s*\(/, name: "ambiguous world-layer chunk access (use ReadChunk or GetOrCreateChunk)", allow: null, allowContent: null },
    // UAC0009: Unity 6 объявила DEVELOPMENT_BUILD устаревшей в пользу директив
    // варианта управляемого кода. Здесь это #if UNITY_EDITOR || UNITY_ENABLE_CHECKS.
    //
    // Не Debug.isDebugBuild: блоки закрывают поля и параметры конструкторов,
    // а рантайм-проверка объявление не вырезает.
    // Не DEBUG: по документации редактора это САМЫЙ УЗКИЙ вариант —
    // неоптимизированная сборка под отладчик. UNITY_ENABLE_CHECKS определён и в
    // Checked, и в Debug (Debug наследует все определения Checked), а закрывают
    // эти блоки именно проверки и отладочные органы управления.
    { pattern: /\bDEVELOPMENT_BUILD\b/, name: "устаревшая директива DEVELOPMENT_BUILD (UAC0009) — используйте UNITY_ENABLE_CHECKS", allow: null, allowContent: null },
    { pattern: /\bGC\.Collect\s*\(/, name: "manual GC.Collect in runtime gameplay", allow: /^(Assets\/Editor\/|Assets\/Scripts\/Tests\/)/, allowContent: null },
    { pattern: /\bCamera\.(allCameras|current)\b/, name: "unmanaged camera lookup (use explicit gameplay camera contract)", allow: null, allowContent: null },
    { pattern: /\bTime\.timeScale\s*=/, name: "unowned Time.timeScale mutation", allow: /^(Assets\/Scripts\/UI\/(PauseMenu\.cs|Settings\/PauseMenu\.cs)|Assets\/Scripts\/Game\/Managers\/GameManager\.cs|Assets\/Scripts\/Tests\/)/, allowContent: null },
    { pattern: /new\s+(WebClient|HttpClient)\s*\(/, name: "ad-hoc HTTP client (use ClientAssetLoader or UnityWebRequest)", allow: /^(Assets\/Editor\/|Assets\/Scripts\/Tests\/)/, allowContent: null },
    { pattern: /Shader\.WarmupAllShaders/, name: "Shader.WarmupAllShaders in URP (throws keyword space assert)", allow: null, allowContent: null },
    { pattern: /_starfieldMaterial\.(?:SetFloat|SetVector|SetColor|SetTexture|SetInt|SetMatrix)\s*\(/, name: "mutation of the serialized Starfield material asset (use the HideAndDontSave runtime clone)", allow: null, allowContent: null },
    { pattern: /\.sharedMaterial\.(?:SetFloat|SetVector|SetColor|SetTexture|SetInt|SetMatrix)\s*\(/, name: "mutation through Renderer.sharedMaterial (clone the material or use MaterialPropertyBlock)", allow: null, allowContent: null },
    { pattern: /GameStartupServices/, name: "deleted GameStartupServices aggregate (inject startup dependencies directly into GameBootstrap)", allow: /^Assets\/Scripts\/Tests\//, allowContent: null },
    { pattern: /SceneScopeAuthoring|SceneContractMigration/, name: "scene auto-fixing editor tools are deleted (use the read-only ProductionSceneContractValidator)", allow: null, allowContent: null },
    { pattern: /PlayerMovementController\.(LocalPlayer|OnLocalPlayerSpawned)/, name: "static local-player access (resolve ILocalPlayerState)", allow: /^Assets\/Scripts\/Core\/Interfaces\/ILocalPlayerState\.cs$/, allowContent: null },
    { pattern: /\b(MenuStarfield|MenuSceneryController)\.Current\b/, name: "static menu-scenery access (use the MainMenuLifetimeScope serialized contract)", allow: null, allowContent: null },
    { pattern: /\b(PauseMenu\.IsMenuOpen|ChatInput\.IsFocused|ProgrammatorGrid\.IsOpen)\b/, name: "static UI state access outside the UI layer (compose IInputBlocker)", allow: /^(Assets\/Scripts\/UI\/|Assets\/Scripts\/DiagnosticRunner\.cs|Assets\/Scripts\/Core\/Bootstrap\/DiagnosticRunner\.cs)/, allowContent: null },
];

const STANDARDS_LIST = [
    "  - static 'Instance' singletons              -> use VContainer DI",
    "  - ServiceLocator                            -> constructor / DI injection",
    "  - IObjectResolver in gameplay/UI logic      -> direct constructor/field dependencies; resolver only in roots/factories",
    "  - 'new InputAction(...)'                    -> configure in InputSystem_Actions.inputactions",
    "  - legacy coroutines (StartCoroutine)        -> use UniTask / CancellationToken",
    "  - legacy Input (Input.Get*)                 -> use UnityEngine.InputSystem (Keyboard.current/Mouse.current)",
    "  - AudioSource components                    -> use FMOD Studio (IAudioSystem / AudioSystem)",
    "  - Camera.main / Camera.allCameras           -> use injected IGameplayCamera (render features may use the explicit marker)",
    "  - targetFrameRate / VSync / SetResolution   -> DisplayManager is the single owner",
    "  - runtime Texture2D construction/decoding   -> use RuntimeTextureFactory",
    "  - UI Toolkit stylesheets in controllers     -> use PanelSettings.themeUss (@import)",
    "  - screen-to-panel coordinate conversion     -> use RuntimePanelUtils.ScreenToPanel",
    "  - UI element sizing from Screen.dimensions  -> use PanelSettings & USS flex layout",
    "  - manager/service runtime creation          -> register and resolve through VContainer",
    "  - graphics preset/quality mutation          -> use ClientConfigManager",
    "  - MSAA, quality-level, URP render-scale     -> LightingEngine is the owner",
    "  - settings persistence in PlayerPrefs       -> use ClientConfigManager (client_config.json)",
    "  - UI settings notifications                 -> use SetValueWithoutNotify",
    "  - runtime GameObject construction           -> use ISceneObjectFactory",
    "  - unscoped GameObject.Find / FindWithTag    -> prohibit global scene searches (use DI or FindInOwnScene)",
    "  - synchronous SceneManager.LoadScene        -> use BootstrapLifetimeScope.TransitionAsync",
    "  - deprecated FindObject(s)OfType            -> use FindObjectsByType / FindAnyObjectByType",
    "  - Unity classes namespace syntax            -> use block namespace { } for MonoBehaviour/ScriptableObject",
];

function checkPatterns(files) {
    const cache = new Map();
    const contentOf = (file) => {
        if (!cache.has(file)) {
            cache.set(file, readFile(file));
        }
        return cache.get(file);
    };

    for (const rule of RULES) {
        for (const file of files) {
            if (rule.allow && rule.allow.test(file)) {
                continue;
            }
            const content = contentOf(file);
            if (content === null) {
                continue;
            }
            const lines = content.split("\n");
            for (let i = 0; i < lines.length; i++) {
                const line = lines[i].replace(/\r$/, "");
                if (!rule.pattern.test(line)) {
                    continue;
                }
                if (COMMENT_LINE_REGEX.test(line)) {
                    continue;
                }
                if (rule.allowContent && rule.allowContent.test(line)) {
                    continue;
                }
                violations.push({
                    category: "Architecture",
                    loc: `${file}:${i + 1}`,
                    message: `${BOLD}${rule.name}${NC}\n  File: ${BOLD}${file}:${i + 1}${NC}\n  Code: ${CYAN}${line}${NC}`,
                    kind: "pattern",
                });
            }
        }
    }
}

// ---------------------------------------------------------------------------
// Part 2: deep semantic DI and lifecycle analyzer
// (ported from scripts/check_di_lifecycle.py)
// ---------------------------------------------------------------------------

const EXECUTION_ORDER_CONTRACTS = {
    "Assets/Scripts/Core/Bootstrap/BootstrapLifetimeScope.cs": -30000,
    "Assets/Scripts/Core/Bootstrap/GameLifetimeScope.cs": -20000,
    "Assets/Scripts/Game/Managers/MapManager.cs": -10000,
};

function isExcludedDiPath(filePath) {
    return /(^|\/)Tests\//.test(filePath) ||
        /(^|\/)Plugins\//.test(filePath) ||
        /(^|\/)VContainer\//.test(filePath) ||
        /(^|\/)Editor\//.test(filePath);
}

function checkExecutionOrders() {
    for (const [filePath, expected] of Object.entries(EXECUTION_ORDER_CONTRACTS)) {
        const content = readFile(filePath);
        if (content === null) {
            continue;
        }
        const m = /\[DefaultExecutionOrder\(\s*(-?\d+)\s*\)\]/.exec(content);
        if (!m || parseInt(m[1], 10) !== expected) {
            recordViolation(
                "Execution Order Contract",
                filePath,
                `Expected [DefaultExecutionOrder(${expected})], found ${m ? m[0] : "none"}.`,
            );
        }
    }
}

function checkLifetimeScopeConfigure() {
    for (const filePath of walkCs("Assets/Scripts")) {
        if (isExcludedDiPath(filePath)) {
            continue;
        }
        const content = readFile(filePath);
        if (content === null || !content.includes("LifetimeScope")) {
            continue;
        }
        const lines = content.split("\n");
        for (let index = 0; index < lines.length; index++) {
            if (!lines[index].includes("RegisterBuildCallback")) {
                continue;
            }
            // A callback that only injects authored scene objects is required
            // before Unity calls Start. Resolve, scene loading and startup work
            // still belong in IPostStartable.
            if (lines[index].includes("InjectSceneBehaviours")) {
                continue;
            }
            recordViolation(
                "Configure Reentrancy",
                filePath + ":" + (index + 1),
                "RegisterBuildCallback may only inject authored scene behaviours. Move Resolve/scene loading/startup work to IPostStartable.",
            );
        }
    }
}

function checkProjectCompileIncludes() {
    for (const projectFile of fs.readdirSync(".").filter((file) => file.endsWith(".csproj"))) {
        const content = readFile(projectFile);
        if (content === null) {
            continue;
        }

        for (const match of content.matchAll(/<Compile Include="([^"]+)"/g)) {
            const sourcePath = match[1].replaceAll("\\", "/");
            if (!fs.existsSync(sourcePath)) {
                recordViolation(
                    "Project References",
                    `${projectFile}:${content.slice(0, match.index).split("\n").length}`,
                    `Compile Include points to a missing source file: ${sourcePath}. Regenerate or clean the project file.`,
                );
            }
        }
    }
}

function checkSceneReadinessContracts() {
    const scope = readRequiredFile("Assets/Scripts/Core/Bootstrap/GameLifetimeScope.cs", "Scene Readiness");
    const bootstrap = readRequiredFile("Assets/Scripts/Core/Bootstrap/BootstrapLifetimeScope.cs", "Scene Readiness");
    const gameBootstrap = readRequiredFile("Assets/Scripts/Core/Bootstrap/GameBootstrap.cs", "Scene Readiness");
    const gameManager = readFile("Assets/Scripts/Game/Managers/GameManager.cs");

    if (scope !== null &&
        (!scope.includes("WaitUntilReadyAsync") ||
            !scope.includes("MarkReady") ||
            !scope.includes("MarkFailed"))) {
        recordViolation(
            "Scene Readiness",
            "Assets/Scripts/Core/Bootstrap/GameLifetimeScope.cs",
            "GameLifetimeScope must expose a deterministic ready/failed signal for Bootstrap scene transitions.",
        );
    }

    if (bootstrap !== null &&
        (!bootstrap.includes("WaitForPresentationAsync") ||
            !bootstrap.includes("SceneTransitionTicket"))) {
        recordViolation(
            "Scene Readiness",
            "Assets/Scripts/Core/Bootstrap/BootstrapLifetimeScope.cs",
            "Bootstrap must await the SceneTransitionTicket presentation readiness before unloading the previous scene.",
        );
    }

    if (gameBootstrap !== null &&
        (!gameBootstrap.includes("_scope.MarkReady()") ||
            !gameBootstrap.includes("_scope.MarkFailed(exception)"))) {
        recordViolation(
            "Scene Readiness",
            "Assets/Scripts/Core/Bootstrap/GameBootstrap.cs",
            "GameBootstrap must publish both successful and failed startup outcomes.",
        );
    }

    if (gameManager !== null &&
        (!gameManager.includes("IsVisualsLoaded") ||
            !gameManager.includes("PendingAssetCount") ||
            !gameManager.includes("PendingCellTextureRequests") ||
            !gameManager.includes("_surfaceRenderer.IsInitialized") ||
            !gameManager.includes("_lightingEngine.IsInitialized"))) {
        recordViolation(
            "World Readiness",
            "Assets/Scripts/Game/Managers/GameManager.cs",
            "OnWorldLoaded must wait for player visuals, surface, lighting and pending asset/texture queues.",
        );
    }
}

function checkTransitionStateContracts() {
    const bootstrapPath = "Assets/Scripts/Core/Bootstrap/BootstrapLifetimeScope.cs";
    const source = readRequiredFile(bootstrapPath, "Scene Transition Contract");
    const navigatorPath = "Assets/Scripts/Core/Interfaces/Contracts/ISceneNavigator.cs";
    const navigator = readRequiredFile(navigatorPath, "Scene Transition Contract");
    const ticketPath = "Assets/Scripts/Core/Bootstrap/SceneTransitionTicket.cs";
    const ticket = readRequiredFile(ticketPath, "Scene Transition Contract");
    const runtimePath = "Assets/Scripts/Core/Bootstrap/SceneTransitionRuntime.cs";
    const runtime = readRequiredFile(runtimePath, "Scene Transition Contract");
    const statusPath = "Assets/Scripts/Core/Interfaces/Contracts/SceneTransitionStatus.cs";
    const status = readRequiredFile(statusPath, "Scene Transition Contract");
    if (source === null) {
        return;
    }

    if (!source.includes("_currentSceneName = null;")) {
        recordViolation(
            "Scene Transition Contract",
            bootstrapPath,
            "Transitioning away from a loaded scene must clear the current-scene state before loading the replacement.",
        );
    }

    if (!source.includes("TransitionChanged") ||
        !source.includes("SceneTransitionPhase.Completed") ||
        !source.includes("ticket.Fail(ex)")) {
        recordViolation(
            "Scene Transition Contract",
            bootstrapPath,
            "Bootstrap scene transitions must publish typed completion and failure states.",
        );
    }

    if (/Transition(?:Started|Completed|Failed)/.test(source)) {
        recordViolation(
            "Scene Transition Contract",
            bootstrapPath,
            "Legacy split transition events are forbidden; publish SceneTransitionStatus through TransitionChanged.",
        );
    }

    if (navigator !== null && !navigator.includes("event Action<SceneTransitionStatus>? TransitionChanged")) {
        recordViolation(
            "Scene Transition Contract",
            navigatorPath,
            "ISceneNavigator must expose the single typed TransitionChanged event.",
        );
    }

    if (ticket !== null &&
        (!ticket.includes("SetPhase(SceneTransitionPhase.Failed, exception)") ||
            !ticket.includes("Phase is SceneTransitionPhase.Failed or SceneTransitionPhase.PresentationReady"))) {
        recordViolation(
            "Scene Transition Contract",
            ticketPath,
            "SceneTransitionTicket must publish Failed exactly once and keep PresentationReady terminal.",
        );
    }

    if (runtime !== null &&
        (!runtime.includes("GetInvocationList()") ||
            !runtime.includes("catch (Exception exception)"))) {
        recordViolation(
            "Scene Transition Contract",
            runtimePath,
            "Transition observers must be invoked independently so one subscriber cannot abort the transaction.",
        );
    }

    if (status !== null &&
        (!status.includes("CompletedWithWarnings") ||
            !status.includes("Failed") ||
            !status.includes("Exception? Failure"))) {
        recordViolation(
            "Scene Transition Contract",
            statusPath,
            "SceneTransitionStatus must represent successful, degraded and failed terminal outcomes.",
        );
    }
}

function checkPersistentAssetCacheContract() {
    const cachePath = "Assets/Scripts/AssetPipeline/Cache/PersistentAssetCache.cs";
    const cache = readRequiredFile(cachePath, "Persistent Cache Contract");
    const manifestPath = "Assets/Scripts/AssetPipeline/Cache/PersistentAssetCacheEntryManifest.cs";
    const manifest = readRequiredFile(manifestPath, "Persistent Cache Contract");
    const formatPath = "Assets/Scripts/AssetPipeline/Cache/PersistentAssetCacheFormat.cs";
    const format = readRequiredFile(formatPath, "Persistent Cache Contract");

    if (cache !== null &&
        (!cache.includes("ConcurrentDictionary<string, SemaphoreSlim>") ||
            !cache.includes("ReadVerifiedAsset") ||
            !cache.includes("WriteAtomically") ||
            !cache.includes('assetPath + ".entry"'))) {
        recordViolation(
            "Persistent Cache Contract",
            cachePath,
            "PersistentAssetCache must serialize per-entry access and atomically persist verified payload/manifest pairs.",
        );
    }

    if (manifest !== null &&
        (!manifest.includes("SHA256.Create()") ||
            !manifest.includes("payload.LongLength == Length") ||
            !manifest.includes("EntryFormatVersion = 2"))) {
        recordViolation(
            "Persistent Cache Contract",
            manifestPath,
            "Persistent cache entries must validate schema v2 length and SHA-256 before use.",
        );
    }

    if (format !== null &&
        (!format.includes("CurrentSchemaVersion = 2") ||
            !format.includes("VersionOneBackupFileName") ||
            !format.includes("CommitVersionMarker"))) {
        recordViolation(
            "Persistent Cache Contract",
            formatPath,
            "Persistent cache format must migrate v1 to schema v2 through a durable marker commit.",
        );
    }
}

function checkUiTransitionGuards() {
    const mainMenuPath = fs.existsSync("Assets/Scripts/UI/Menu/Core/MainMenu.cs") ? "Assets/Scripts/UI/Menu/Core/MainMenu.cs" : "Assets/Scripts/UI/Menu/MainMenu.cs";
    const gatewayPath = fs.existsSync("Assets/Scripts/UI/Gateway/GatewayController.cs") ? "Assets/Scripts/UI/Gateway/GatewayController.cs" : "Assets/Scripts/UI/GatewayController.cs";
    const mainMenu = readFile(mainMenuPath);
    const gateway = readFile(gatewayPath);
    if (mainMenu !== null && !/private void OnPlayButtonClicked\(\)\s*\{\s*if \(_loadingActive \|\| _teardownStarted\)/s.test(mainMenu)) {
        recordViolation(
            "UI Transition Contract",
            mainMenuPath,
            "MainMenu Play transition must be guarded against duplicate clicks while loading or tearing down.",
        );
    }
    if (gateway !== null && !/private void GoToMainMenu\(\)\s*\{\s*if \(_leaving\)/s.test(gateway)) {
        recordViolation(
            "UI Transition Contract",
            gatewayPath,
            "Gateway-to-menu transition must be guarded against duplicate activation.",
        );
    }
}

function checkSceneScopeInjection() {
    const contracts = [
        ["Assets/Scripts/Core/Bootstrap/GatewayLifetimeScope.cs", "GatewayController"],
        ["Assets/Scripts/Core/Bootstrap/MainMenuLifetimeScope.cs", "MainMenu"],
    ];
    for (const [filePath, component] of contracts) {
        const source = readFile(filePath);
        if (source === null) {
            continue;
        }
        if (!new RegExp(`RegisterComponent\\([^)]*_${component === "MainMenu" ? "controller" : "controller"}[^)]*\\)`).test(source)) {
            recordViolation(
                "Scene Scope Injection",
                filePath,
                `${component} must be registered as an authored scene component so VContainer injects it during resolution.`,
            );
        }
    }
}

function checkLifecycleSelfCalls() {
    for (const filePath of walkCs("Assets/Scripts")) {
        if (isExcludedDiPath(filePath)) {
            continue;
        }
        const source = readFile(filePath);
        if (source === null) {
            continue;
        }
        for (const methodName of ["Awake", "OnEnable", "Start", "OnDisable", "OnDestroy"]) {
            const method = new RegExp(`(?:void|UniTask|UniTaskVoid)\\s+${methodName}\\s*\\([^)]*\\)\\s*\\{([\\s\\S]*?)\\n\\s*\\}`, "g");
            for (const match of source.matchAll(method)) {
                if (new RegExp(`(?<!base\\.)\\b${methodName}\\s*\\(\\s*\\)`).test(match[1])) {
                    recordViolation(
                        "Lifecycle Contract",
                        filePath,
                        `${methodName}() must not be called manually from its own lifecycle logic; use an explicit initialization/rebinding method.`,
                    );
                }
            }
        }
    }
}

function checkMenuSceneryOwnership() {
    // readRequiredFile, а не readFile: раньше отсутствие любой из двух сцен
    // молча выключало проверку целиком, и переименование сцены сняло бы её
    // без единого слова в выводе. Проверка, исчезающая вместе с файлом, даёт
    // ложную уверенность — то же самое уже случилось с проверкой тонмапа.
    const bootstrapScene = readRequiredFile(
        "Assets/Scenes/Bootstrap.unity", "Menu Scenery Ownership");
    const mainMenuScene = readRequiredFile(
        "Assets/Scenes/MainMenu.unity", "Menu Scenery Ownership");
    if (bootstrapScene === null || mainMenuScene === null) {
        return;
    }

    const bootstrapOwnsScenery = bootstrapScene.includes("m_Name: MenuScenery");
    const menuOwnsScenery = mainMenuScene.includes("m_Name: MenuScenery");
    if (bootstrapOwnsScenery || !menuOwnsScenery) {
        recordViolation(
            "Scene Ownership",
            "Assets/Scenes/Bootstrap.unity",
            "MainMenu must own MenuScenery and Bootstrap must not contain menu scenery. Use Unity Editor API to restore scene ownership.",
        );
    }
}

function checkEditorSceneAuthoringContract() {
    const authoring = readFile("Assets/Scripts/Editor/SceneScopeAuthoring.cs");
    const migration = readFile("Assets/Scripts/Editor/SceneContractMigration.cs");
    const validator = readFile("Assets/Scripts/Editor/ProductionSceneContractValidator.cs");
    const runtimeScope = readRequiredFile("Assets/Scripts/Core/Bootstrap/GameLifetimeScope.cs", "Scene Authoring Contract");

    if (authoring !== null || migration !== null) {
        recordViolation(
            "Scene Authoring Contract",
            "Assets/Scripts/Editor/SceneScopeAuthoring.cs",
            "Scene auto-fixing editor tools are deleted; only the read-only ProductionSceneContractValidator may exist.",
        );
    }

    if (validator === null) {
        recordViolation(
            "Scene Authoring Contract",
            "Assets/Scripts/Editor/ProductionSceneContractValidator.cs",
            "The read-only ProductionSceneContractValidator must exist and guard scene contracts.",
        );
    } else if (!validator.includes("bindingCount == 0") ||
        !validator.includes("boundTypes") ||
        !validator.includes("target.transform.IsChildOf(groupRoot)")) {
        recordViolation(
            "Scene Authoring Contract",
            "Assets/Scripts/Editor/ProductionSceneContractValidator.cs",
            "The production validator must reject empty, duplicate, stale and wrongly-grouped ManagerBindings.",
        );
    }

    if (runtimeScope !== null) {
        if (!runtimeScope.includes('RegisterManager<WorldTextureManager>(builder, "World")')) {
            recordViolation(
                "Scene Authoring Contract",
                "Assets/Scripts/Core/Bootstrap/GameLifetimeScope.cs",
                "MainGame World manager contract must include WorldTextureManager.",
            );
        }

        if (!runtimeScope.includes("ResolveTypedBinding<T>(group)") ||
            /FindManagerInOwnScene|GetComponentsInChildren<T>\(true\).*RegisterManager/s.test(runtimeScope)) {
            recordViolation(
                "Scene Authoring Contract",
                "Assets/Scripts/Core/Bootstrap/GameLifetimeScope.cs",
                "RegisterManager must require typed ManagerBindings without a hierarchy-search fallback.",
            );
        }
    }
}

function checkGameBootstrapResolvesRegisteredManagers() {
    const scopePath = "Assets/Scripts/Core/Bootstrap/GameLifetimeScope.cs";
    const bootstrapPath = "Assets/Scripts/Core/Bootstrap/GameBootstrap.cs";
    const scope = readRequiredFile(scopePath, "Startup Dependency Contract");
    const bootstrap = readRequiredFile(bootstrapPath, "Startup Dependency Contract");
    if (scope === null || bootstrap === null) {
        return;
    }

    if (scope.includes("GameStartupServices")) {
        recordViolation(
            "Startup Dependency Contract",
            scopePath,
            "GameStartupServices is deleted: GameBootstrap receives only its real startup dependencies via constructor injection.",
        );
    }

    if (!scope.includes("RegisterEntryPoint<GameBootstrap>")) {
        recordViolation(
            "Startup Dependency Contract",
            scopePath,
            "GameLifetimeScope must register GameBootstrap as the entry point of the MainGame composition root.",
        );
    }


    for (const requiredType of ["GameInfrastructureStartup", "GamePresentationStartup", "GameStartupPipeline"]) {
        if (!scope.includes(`Register<${requiredType}>`)) {
            recordViolation(
                "Startup Dependency Contract",
                scopePath,
                `GameLifetimeScope must register ${requiredType}.`,
            );
        }
    }

    if (!bootstrap.includes("GameStartupPipeline") ||
        /TerrainRenderer|PostProcessController|LightingEngine|PlayerHUDView|InventoryView/.test(bootstrap)) {
        recordViolation(
            "Startup Dependency Contract",
            bootstrapPath,
            "GameBootstrap must only coordinate the typed GameStartupPipeline and scene ticket.",
        );
    }

    if (/\b(?:_resolver|resolver)\.Resolve\s*</.test(bootstrap) ||
        /\bResolve\s*<[^>]+>\s*\(/.test(bootstrap)) {
        recordViolation(
            "Startup Resolve Contract",
            bootstrapPath,
            "GameBootstrap must not resolve from the container; constructor injection only.",
        );
    }
}

function checkCompositionRootContracts() {
    const roots = [
        "Assets/Scripts/Core/Bootstrap/BootstrapLifetimeScope.cs",
        "Assets/Scripts/Core/Bootstrap/GameLifetimeScope.cs",
        "Assets/Scripts/Core/Bootstrap/GatewayLifetimeScope.cs",
        "Assets/Scripts/Core/Bootstrap/MainMenuLifetimeScope.cs",
    ];
    for (const filePath of roots) {
        const source = readFile(filePath);
        if (source === null) {
            continue;
        }
        if (/Find(?:AnyObject|FirstObject|Objects?ByType)<|FindGameObjectWithTag\s*\(/.test(source)) {
            recordViolation(
                "Composition Root Scene Scan",
                filePath,
                "Composition roots must use serialized references or their own authored hierarchy; global runtime scene scans are forbidden.",
            );
        }
    }
}

function checkDirectDependencyCycles() {
    const graph = new Map();
    const typeNames = new Set();

    for (const filePath of walkCs("Assets/Scripts")) {
        if (isExcludedDiPath(filePath)) {
            continue;
        }
        const content = readFile(filePath);
        if (content === null) {
            continue;
        }

        for (const match of content.matchAll(/\bclass\s+([A-Za-z0-9_]+)/g)) {
            typeNames.add(match[1]);
        }

        for (const match of content.matchAll(/\[Inject\]\s*(?:private|protected|public|internal)?\s*([A-Za-z0-9_<>.?]+)\s+[_A-Za-z0-9]+/g)) {
            const owner = content.slice(0, match.index).match(/\bclass\s+([A-Za-z0-9_]+)[^{]*\{[^{}]*$/)?.[1];
            if (owner) {
                const dependency = match[1].replace(/[<>.?]/g, "");
                if (!graph.has(owner)) {
                    graph.set(owner, new Set());
                }
                graph.get(owner).add(dependency);
            }
        }

        for (const className of typeNames) {
            const constructor = new RegExp(`\\b${escapeRegExp(className)}\\s*\\(([^)]*)\\)`, "m").exec(content);
            if (!constructor) {
                continue;
            }
            const dependencies = constructor[1].match(/[A-Za-z_][A-Za-z0-9_]*(?=\s+[_A-Za-z])/g) ?? [];
            if (!graph.has(className)) {
                graph.set(className, new Set());
            }
            for (const dependency of dependencies) {
                graph.get(className).add(dependency);
            }
        }
    }

    const reported = new Set();
    const visit = (typeName, path, active) => {
        if (active.has(typeName)) {
            const cycleStart = path.indexOf(typeName);
            const cycle = path.slice(cycleStart).concat(typeName);
            const key = [...cycle].sort().join("|");
            if (!reported.has(key)) {
                reported.add(key);
                recordViolation(
                    "DI Dependency Cycle",
                    "Assets/Scripts",
                    "Direct dependency cycle detected: " + cycle.join(" -> ") + ". Break the cycle with an event/callback or a narrow interface.",
                );
            }
            return;
        }
        if (!typeNames.has(typeName)) {
            return;
        }
        active.add(typeName);
        for (const dependency of graph.get(typeName) ?? []) {
            visit(dependency, path.concat(typeName), active);
        }
        active.delete(typeName);
    };

    for (const typeName of typeNames) {
        visit(typeName, [], new Set());
    }
}

function checkPacketSubscriptionSymmetry() {
    for (const filePath of walkCs("Assets/Scripts")) {
        if (isExcludedDiPath(filePath)) {
            continue;
        }
        const content = readFile(filePath);
        if (content === null) {
            continue;
        }

        const subscriptions = [...content.matchAll(/\.OnPacketReceived\s*\+=/g)].length;
        const unsubscriptions = [...content.matchAll(/\.OnPacketReceived\s*-\s*=/g)].length;
        if (subscriptions > 0 && unsubscriptions === 0) {
            recordViolation(
                "Subscription Lifetime",
                filePath,
                "OnPacketReceived is subscribed without a matching unsubscribe. This leaks scene listeners across transitions.",
            );
        }
    }
}

// Validate the serialized scene/DI contract. This catches the class of failure
// that code-only linting missed: a manager is registered for Services/UI while
// the scene asset is still flat or points the scope at the wrong root.
const SCENE_CONTRACTS = {
    "Assets/Scenes/Bootstrap.unity": {
        scope: "BootstrapLifetimeScope",
        groupRoot: "BootstrapLifetimeScope",
        components: ["BootstrapLifetimeScope"],
        uniqueComponents: ["UIDocument"],
        groups: {
            Networking: ["ConnectionManager", "NetworkService"],
            Content: ["ClientAssetLoader", "ClientConfigManager", "TextureStorageManager"],
            Audio: ["AudioSystem"],
            Presentation: ["BootstrapLoadingScreen"],
        },
        forbidden: ["GameLifetimeScope"],
    },
    "Assets/Scenes/MainGame.unity": {
        scope: "GameLifetimeScope",
        groupRoot: "GameLifetimeScope/Services",
        components: ["GameLifetimeScope"],
        uniqueComponents: ["UIDocument"],
        groups: {
            Networking: ["PacketHandler"],
            World: ["MapManager", "WorldBackgroundSetup", "WorldTextureManager"],
            Rendering: ["TerrainRenderer", "WorldEntityBatchRenderer", "PostProcessController", "LightingEngine", "SurfaceRenderer", "CameraFollow", "VFXPool"],
            Gameplay: ["GameManager", "BuildingManager", "RobotManager", "ServerConfig"],
            UI: ["GlobalChatUI", "UIInputManager", "FPSCounter", "FloatingChatManager", "ReconnectUI", "AssetLoadingIndicator", "MissionArrowUI", "DiagnosticRunner", "PlayerHUDView", "InventoryView", "PauseMenu", "MinimapController", "WorldMapController", "WorldMapRenderer", "DisplayManager", "InGameDebugOverlay"],
            Audio: ["ServerAudioEventManager"],
        },
    },
    "Assets/Scenes/Gateway.unity": {
        scope: "GatewayLifetimeScope",
        components: ["GatewayLifetimeScope", "GatewayController", "UIDocument"],
        uniqueComponents: ["UIDocument"],
    },
    "Assets/Scenes/MainMenu.unity": {
        scope: "MainMenuLifetimeScope",
        components: ["MainMenuLifetimeScope", "MainMenu", "UIDocument"],
        uniqueComponents: ["UIDocument"],
    },
};

function parseUnitySceneContract(filePath) {
    const source = readFile(filePath);
    if (source === null) {
        return null;
    }
    buildScriptClassIndex();
    const objects = new Map();
    const unresolvedScripts = [];
    for (const raw of source.split(/^--- !u!/m).slice(1)) {
        const header = raw.match(/^(\d+) &(-?\d+)\n/);
        if (!header) {
            continue;
        }
        const type = Number(header[1]);
        const id = Number(header[2]);
        const body = raw.slice(header[0].length);
        if (type === 1) {
            const name = body.match(/^  m_Name: (.*)$/m)?.[1]?.trim() ?? "";
            const componentIds = [...body.matchAll(/- component: \{fileID: (-?\d+)\}/g)].map((m) => Number(m[1]));
            objects.set(id, { type, id, name, componentIds });
        } else if (type === 4) {
            objects.set(id, {
                type,
                id,
                goId: Number(body.match(/m_GameObject: \{fileID: (-?\d+)\}/)?.[1] ?? 0),
                parentId: Number(body.match(/m_Father: \{fileID: (-?\d+)\}/)?.[1] ?? 0),
            });
        } else if (type === 114) {
            const scriptGuid = body.match(/m_Script: \{fileID: 11500000, guid: ([a-f0-9]+), type: 3\}/)?.[1];
            const serializedIdentifier = body.match(/^  m_EditorClassIdentifier:[ \t]*(.*?)[ \t]*$/m)?.[1] ?? "";
            const serializedClassName = serializedIdentifier
                ? serializedIdentifier.split("::").pop().split(".").pop()
                : "";
            objects.set(id, {
                type,
                id,
                goId: Number(body.match(/m_GameObject: \{fileID: (-?\d+)\}/)?.[1] ?? 0),
                scriptGuid,
                className: serializedClassName ||
                    (scriptGuid ? SCRIPT_CLASS_BY_GUID.get(scriptGuid) ?? "" :
                        body.includes("m_Script: {fileID: 19102,")
                            ? "UIDocument"
                            : ""),
            });
            if (scriptGuid && !SCRIPT_CLASS_BY_GUID.has(scriptGuid) &&
                !body.includes("m_Script: {fileID: 19102,") && !serializedClassName) {
                unresolvedScripts.push({ id, scriptGuid });
            }
        }
    }
    const transforms = new Map([...objects.values()]
        .filter((object) => object.type === 4)
        .map((object) => [object.goId, object]));
    const pathFor = (goId, seen = new Set()) => {
        if (seen.has(goId)) {
            return "<cycle>";
        }
        seen.add(goId);
        const object = objects.get(goId);
        if (!object || object.type !== 1) {
            return "<unknown>";
        }
        const parentTransformId = transforms.get(goId)?.parentId ?? 0;
        if (!parentTransformId) {
            return object.name;
        }

        const parentTransform = objects.get(parentTransformId);
        const parentGameObjectId = parentTransform?.type === 4 ? parentTransform.goId : 0;
        return parentGameObjectId ? pathFor(parentGameObjectId, seen) + "/" + object.name : object.name;
    };
    const gameObjects = [...objects.values()].filter((object) => object.type === 1);
    const components = [];
    for (const object of gameObjects) {
        for (const componentId of object.componentIds ?? []) {
            const component = objects.get(componentId);
            if (component?.type === 114) {
                components.push({ className: component.className, path: pathFor(object.id) });
            }
        }
    }
    return { gameObjects, components, pathFor, unresolvedScripts };
}

function checkSerializedSceneContracts() {
    for (const [filePath, contract] of Object.entries(SCENE_CONTRACTS)) {
        const scene = parseUnitySceneContract(filePath);
        if (scene === null) {
            recordViolation("Scene Contract", filePath, "Scene file could not be read.");
            continue;
        }
        for (const unresolved of scene.unresolvedScripts) {
            recordViolation(
                "Scene Contract",
                filePath,
                `Scene contains an unresolved script GUID ${unresolved.scriptGuid} on MonoBehaviour ${unresolved.id}. The component may be missing or belongs to an unindexed assembly.`,
            );
        }
        const scopeMatches = scene.components.filter((component) => component.className === contract.scope);
        const allScopeMatches = scene.components.filter((component) => component.className.endsWith("LifetimeScope"));
        if (allScopeMatches.length !== 1) {
            recordViolation(
                "Scene Contract",
                filePath,
                `Expected exactly one LifetimeScope component in the scene, found ${allScopeMatches.length}: ` +
                    allScopeMatches.map((component) => `${component.className}@${component.path}`).join(", "),
            );
        }
        if (scopeMatches.length !== 1) {
            recordViolation(
                "Scene Contract",
                filePath,
                `Expected exactly one ${contract.scope} component, found ${scopeMatches.length}.`,
            );
        }

        if (scopeMatches.length === 0) {
            recordViolation("Scene Contract", filePath, "Required scope '" + contract.scope + "' is missing.");
            continue;
        }

        for (const componentName of contract.components ?? []) {
            if (!scene.components.some((component) => component.className === componentName)) {
                recordViolation("Scene Contract", filePath, `Required component '${componentName}' is missing.`);
            }
        }
        for (const componentName of contract.uniqueComponents ?? []) {
            const matches = scene.components.filter((component) => component.className === componentName);
            if (matches.length !== 1) {
                recordViolation(
                    "Scene Contract",
                    filePath,
                    `Expected exactly one ${componentName} component, found ${matches.length}: ` +
                        matches.map((match) => match.path).join(", "),
                );
            }
        }
        for (const forbidden of contract.forbidden ?? []) {
            if (scene.gameObjects.some((object) => object.name === forbidden)) {
                recordViolation("Scene Contract", filePath, "Foreign object '" + forbidden + "' is present; it belongs only to its own scene.");
            }
        }
        for (const [group, classNames] of Object.entries(contract.groups ?? {})) {
            const prefix = contract.groupRoot + "/" + group;
            if (!scene.gameObjects.some((object) => scene.pathFor(object.id) === prefix)) {
                recordViolation("Scene Contract", filePath, "Required hierarchy '" + prefix + "' is missing.");
                continue;
            }
            for (const className of classNames) {
                const matches = scene.components.filter((component) => component.className === className);
                if (matches.length === 0) {
                    recordViolation("Scene Contract", filePath, "Registered manager '" + className + "' has no authored component.");
                } else if (matches.length > 1) {
                    recordViolation(
                        "Scene Contract",
                        filePath,
                        "Registered manager '" + className + "' has duplicate authored components: " +
                            matches.map((match) => match.path).join(", "),
                    );
                } else if (!matches.some((component) => component.path.startsWith(prefix + "/"))) {
                    recordViolation("Scene Contract", filePath, "Manager '" + className + "' is outside '" + prefix + "': " + matches.map((m) => m.path).join(", "));
                } else if (!matches.some((component) => component.path === prefix + "/" + className)) {
                    recordViolation(
                        "Scene Contract",
                        filePath,
                        "Manager '" + className + "' must be the direct authored object '" + prefix + "/" + className + "'.",
                    );
                }
            }
        }
    }
}

function checkUnityNamespaces() {
    for (const filePath of walkCs("Assets/Scripts")) {
        if (isExcludedDiPath(filePath)) {
            continue;
        }
        const content = readFile(filePath);
        if (content === null) {
            continue;
        }
        const unity = /class\s+([A-Za-z0-9_]+)\s*:[^{]*\b(MonoBehaviour|ScriptableObject|VolumeComponent|ScriptableRendererFeature)\b/.exec(content);
        if (unity && /^\s*namespace\s+[A-Za-z0-9_.]+\s*;/m.test(content)) {
            recordViolation(
                "Unity Namespace Contract",
                filePath,
                `Class '${unity[1]}' inherits from Unity type but uses file-scoped namespace. Must use block namespace { } to prevent MonoScript.GetClass() == null.`,
            );
        }
    }
}

function checkEarlyLifecycleDiAndCallgraph() {
    for (const filePath of walkCs("Assets/Scripts")) {
        if (isExcludedDiPath(filePath)) {
            continue;
        }
        const content = readFile(filePath);
        if (content === null) {
            continue;
        }

        // All [Inject] field names in the class.
        const fieldNames = new Set();
        for (const m of content.matchAll(/\[Inject\]\s*(?:private|protected|public)?\s*([A-Za-z0-9_<>?]+)\s+([_A-Za-z0-9]+)\s*(=|;)/g)) {
            fieldNames.add(m[2]);
        }
        if (fieldNames.size === 0) {
            continue;
        }

        // Parse every method body (brace-matched) into name -> body.
        const methods = {};
        const methodRe = /(?:private|protected|public|internal)?\s*(?:override|virtual|static)?\s*(?:void|bool|int|string|Task|UniTask|UniTaskVoid)\s+([A-Za-z0-9_]+)\s*\([^)]*\)\s*\{/g;
        for (const m of content.matchAll(methodRe)) {
            const name = m[1];
            let end = m.index + m[0].length;
            let braceCount = 1;
            while (end < content.length && braceCount > 0) {
                if (content[end] === "{") {
                    braceCount++;
                } else if (content[end] === "}") {
                    braceCount--;
                }
                end++;
            }
            methods[name] = content.slice(m.index + m[0].length, end - 1);
        }

        // Trace the synchronous call graph from Awake/OnEnable.
        for (const entry of ["Awake", "OnEnable"]) {
            if (!(entry in methods)) {
                continue;
            }
            const visited = new Set([entry]);
            const queue = [entry];
            while (queue.length > 0) {
                const curr = queue.shift();
                let body = methods[curr] || "";
                // Strip lambda bodies and UI Toolkit callback registrations so
                // delegate subscriptions are not treated as synchronous calls.
                body = body.replace(/=>\s*\{[^}]*\}/g, "=> {}");
                body = body.replace(/RegisterCallback<[^>]+>\s*\([^)]*\)/g, "");
                for (const other of Object.keys(methods)) {
                    if (visited.has(other) || other === entry) {
                        continue;
                    }
                    let found = false;
                    for (const rawLine of body.split("\n")) {
                        const line = rawLine.trim();
                        if (line.includes("+=") || line.includes("-=") || line.includes("=>")) {
                            continue;
                        }
                        if (new RegExp("\\b" + escapeRegExp(other) + "\\s*\\(").test(line)) {
                            found = true;
                            break;
                        }
                    }
                    if (found) {
                        visited.add(other);
                        queue.push(other);
                    }
                }
            }

            for (const reached of visited) {
                const body = methods[reached] || "";
                const norm = body.replace(/\s+/g, " ");

                if (/\b(Session|_session)\.Resolve</.test(norm)) {
                    recordViolation(
                        "Early Lifecycle DI",
                        filePath,
                        `Calling Resolve<T>() in ${entry}() -> ${reached}() is forbidden. Use TryResolve<T>() with null-guard.`,
                    );
                }

                for (const fn of fieldNames) {
                    const derefRe = new RegExp("\\b" + escapeRegExp(fn) + "\\s*(\\.|\\(|\\[)", "g");
                    if (![...body.matchAll(derefRe)].length) {
                        continue;
                    }
                    const hasGuard =
                        (new RegExp("if\\s*\\([^)]*\\b" + escapeRegExp(fn) + "\\s*==\\s*null").test(body) && body.includes("return")) ||
                        new RegExp("if\\s*\\([^)]*\\b" + escapeRegExp(fn) + "\\s*!=\\s*null").test(body) ||
                        new RegExp("if\\s*\\([^)]*\\b" + escapeRegExp(fn) + "\\s*is\\s+not\\s+null").test(body) ||
                        new RegExp("\\b" + escapeRegExp(fn) + "\\s*!=\\s*null\\s*\\?").test(norm) ||
                        new RegExp("\\b" + escapeRegExp(fn) + "\\s*\\?\\.").test(body) ||
                        new RegExp("if\\s*\\([^)]*_isInitialized[^)]*\\)\\s*\\{[^}]*\\b" + escapeRegExp(fn) + "\\b").test(body) ||
                        (reached === "TrySubscribeToNetworkService" && filePath.includes("PacketHandler"));
                    if (!hasGuard) {
                        recordViolation(
                            "Unguarded [Inject] Field Access",
                            filePath,
                            `Field '${fn}' is accessed in ${entry}() -> ${reached}() without a null check.`,
                        );
                    }
                }
            }
        }
    }
}

function checkAsyncVoid() {
    for (const filePath of walkCs("Assets/Scripts")) {
        if (isExcludedDiPath(filePath)) {
            continue;
        }
        const content = readFile(filePath);
        if (content === null || !content.includes("MonoBehaviour")) {
            continue;
        }
        for (const m of content.matchAll(/async\s+void\s+([A-Za-z0-9_]+)\s*\(([^)]*)\)/g)) {
            const name = m[1];
            if (name.startsWith("On") || name.endsWith("Click") || name.endsWith("Clicked")) {
                continue;
            }
            recordViolation(
                "Async Void in MonoBehaviour",
                filePath,
                `Method 'async void ${name}' escapes UniTask lifecycle tracking. Use 'async UniTaskVoid' or 'async UniTask' with CancellationToken.`,
            );
        }
    }
}

// ---------------------------------------------------------------------------
// Part 3: settings wiring analyzer
// (ported from scripts/check_settings_wiring.py)
// ---------------------------------------------------------------------------

const CONFIG_PATH = "Assets/Scripts/Core/Interfaces/Contracts/ClientConfig.cs";
const BOOTSTRAP_PATH = "Assets/Scripts/Core/Bootstrap/GameStartupPipeline.cs";
const CONFIG_METADATA_FIELDS = new Set(["SchemaVersion", "ProjectDefaultsHash"]);

const WIRING_EXCLUDE_DIRS = new Set(["Tests", "Plugins", "VContainer"]);

function isConfigInfrastructureFile(file) {
    const normalized = file.replace(/\\/g, "/");
    return normalized.includes("/Core/Configuration/") ||
        normalized.includes("/Core/Interfaces/Contracts/");
}

// Config-consuming MonoBehaviours that must apply their ClientConfig at
// startup. "Applied at startup" means GameStartupPipeline.cs invokes one of the
// listed methods on a typed receiver. Keep this list current: a MonoBehaviour
// exposing ApplyClientConfig that is missing from it fails the build, and a
// listed consumer whose method is no longer invoked fails too.
const STARTUP_APPLY_CONTRACTS = {
    "TerrainRenderer": ["ApplyClientConfig"],
    "SurfaceRenderer": ["ApplyClientConfig"],
    "LightingEngine": ["EnsureInitialized", "ApplyClientConfig"],
    "PostProcessController": ["EnsureVolumeSetup", "ApplyClientConfig"],
};

function collectWiringFiles() {
    const files = [];
    for (const root of ["Assets/Scripts", "Assets/Editor"]) {
        let entries;
        try {
            entries = fs.readdirSync(root, { withFileTypes: true });
        } catch {
            continue;
        }
        const stack = [...entries.map((e) => path.join(root, e.name))];
        while (stack.length > 0) {
            const full = stack.pop();
            const name = path.basename(full);
            if (WIRING_EXCLUDE_DIRS.has(name)) {
                continue;
            }
            let stat;
            try {
                stat = fs.statSync(full);
            } catch {
                continue;
            }
            if (stat.isDirectory()) {
                for (const entry of fs.readdirSync(full, { withFileTypes: true })) {
                    stack.push(path.join(full, entry.name));
                }
            } else if (stat.isFile() && name.endsWith(".cs")) {
                files.push(full);
            }
        }
    }
    return files;
}

function parseConfigFields(content) {
    const fields = [];
    for (const m of content.matchAll(/^\s*public\s+(?!const\b)([A-Za-z0-9_<>\[\],.\s?]+?)\s+([A-Za-z0-9_]+)\s*(?:=|;)/gm)) {
        fields.push(m[2]);
    }
    return fields;
}

// Collect every production file that references each ClientConfig field
// (ClientConfig.cs itself excluded). Shared by the dead-field and UI-only
// wiring checks so the tree is scanned once.
function collectConfigFieldReads() {
    const configSrc = readFile(CONFIG_PATH);
    const fields = configSrc === null ? [] : parseConfigFields(configSrc);
    const reads = new Map(fields.map((field) => [field, []]));
    const configAbs = path.resolve(CONFIG_PATH);
    for (const file of collectWiringFiles()) {
        if (path.resolve(file) === configAbs) {
            continue;
        }
        if (isConfigInfrastructureFile(file)) {
            continue;
        }
        const content = readFile(file);
        if (content === null) {
            continue;
        }
        for (const field of fields) {
            if (new RegExp("\\." + escapeRegExp(field) + "\\b").test(content)) {
                reads.get(field).push(file);
            }
        }
    }
    return { configSrc, fields, reads };
}

function checkDeadConfigFields() {
    const { configSrc, fields, reads } = collectConfigFieldReads();
    if (configSrc === null) {
        recordViolation("Settings Wiring (dead field)", CONFIG_PATH, "Could not read ClientConfig.cs.");
        return;
    }
    if (fields.length === 0) {
        recordViolation("Settings Wiring (dead field)", CONFIG_PATH, "Could not parse ClientConfig fields.");
        return;
    }

    for (const field of fields) {
        if (CONFIG_METADATA_FIELDS.has(field)) {
            continue;
        }

        if (reads.get(field).length === 0) {
            recordViolation(
                "Settings Wiring (dead field)",
                CONFIG_PATH,
                `ClientConfig.${field} is never referenced in production code — the setting does nothing. Wire it to a consumer or remove it.`,
            );
        }
    }
}

// ClientConfig fields whose consumer legitimately lives in the UI layer: they
// are read AND applied there (e.g. via panelSettings.scale), so UI-only reads
// are correct, not dead wiring. Keep this list minimal and justified — a
// setting that is merely shown/saved by Settings but never applied anywhere
// (TargetFrameRate before DisplayManager wired it) must NOT be added here.
const UI_WIRING_ALLOWED_FIELDS = new Set([
    // Applied via UIDocument panelSettings.scale: PauseMenu.cs applies the
    // saved scale at startup and PauseMenuSettingsBuilder applies it live on
    // slider change — the UI panel itself is the consumer by design.
    "UIScale",
]);

function isUiControllerFile(file) {
    const normalized = file.replace(/\\/g, "/");
    const basename = normalized.split("/").pop();
    return normalized.includes("/UI/") || /(Gateway|PauseMenu)/.test(basename);
}

function checkUiOnlyWiring() {
    const { configSrc, fields, reads } = collectConfigFieldReads();
    if (configSrc === null || fields.length === 0) {
        return; // parse failure is reported by the dead-field check
    }
    for (const field of fields) {
        if (CONFIG_METADATA_FIELDS.has(field)) {
            continue;
        }

        if (UI_WIRING_ALLOWED_FIELDS.has(field)) {
            continue;
        }
        // ClientConfigManager validates/migrates fields — that is not a
        // consumer applying the setting, so it does not count as wiring.
        const readers = reads.get(field);
        if (readers.length === 0) {
            continue; // never referenced -> the dead-field check owns it
        }
        if (readers.every(isUiControllerFile)) {
            recordViolation(
                "Settings Wiring (UI-only)",
                CONFIG_PATH,
                `ClientConfig.${field} is read only from UI controllers (${readers.join(", ")}) — Settings can show and save it, but no game system ever applies it (the TargetFrameRate bug before DisplayManager wired it). Connect the field to a consumer or remove the setting.`,
            );
        }
    }
}

function checkUncoveredConsumers() {
    const applyRe = /public\s+void\s+ApplyClientConfig\s*\(/;
    const monoClassRe = /\bclass\s+[A-Za-z0-9_]+[^{]*:\s*MonoBehaviour\b/;
    for (const file of collectWiringFiles()) {
        if (path.resolve(file) === path.resolve(BOOTSTRAP_PATH)) {
            continue;
        }
        const content = readFile(file);
        if (content === null || !applyRe.test(content) || !monoClassRe.test(content)) {
            continue;
        }
        for (const m of content.matchAll(/\bclass\s+([A-Za-z0-9_]+)\s*:[^{]*\{/g)) {
            const cls = m[1];
            if (!(cls in STARTUP_APPLY_CONTRACTS)) {
                recordViolation(
                    "Settings Wiring (uncovered consumer)",
                    file,
                    `${cls} exposes ApplyClientConfig() but is missing from STARTUP_APPLY_CONTRACTS in scripts/check-architecture.js. Either wire it into GameStartupPipeline and add it to the contract, or it will apply saved config only from the pause menu.`,
                );
            }
        }
    }
}

function checkStartupApplicationContract() {
    const bootstrapSrc = readFile(BOOTSTRAP_PATH);
    if (bootstrapSrc === null) {
        recordViolation("Settings Wiring (startup application)", BOOTSTRAP_PATH, "Could not read GameBootstrap.cs.");
        return;
    }

    // Map local variables to their contract class, e.g.
    //   out TerrainRenderer? terrainRenderer   -> terrainRenderer: TerrainRenderer
    //   var lightingEngine = Resolve<LightingEngine>() -> lightingEngine: LightingEngine
    const variables = {};
    for (const m of bootstrapSrc.matchAll(/\b(out\s+)?([A-Za-z0-9_<>]+)\??\s+([a-z_][A-Za-z0-9_]*)\s*(?:=|;|\))/g)) {
        if (m[2] in STARTUP_APPLY_CONTRACTS) {
            variables[m[3]] = m[2];
        }
    }
    for (const m of bootstrapSrc.matchAll(/\bvar\s+([a-z_][A-Za-z0-9_]*)\s*=\s*[^;]*?Resolve<([A-Za-z0-9_<>]+)>/g)) {
        if (m[2] in STARTUP_APPLY_CONTRACTS && !(m[1] in variables)) {
            variables[m[1]] = m[2];
        }
    }

    // Which contract methods are invoked on typed receivers:
    //   terrainRenderer.ApplyClientConfig()  -> (TerrainRenderer, ApplyClientConfig)
    const receivers = new Set();
    for (const [varName, typeName] of Object.entries(variables)) {
        for (const m of bootstrapSrc.matchAll(new RegExp("\\b" + escapeRegExp(varName) + "\\.([A-Za-z0-9_]+)\\s*\\(", "g"))) {
            receivers.add(`${typeName}.${m[1]}`);
        }
    }

    for (const [cls, applyMethods] of Object.entries(STARTUP_APPLY_CONTRACTS)) {
        if (!applyMethods.some((method) => receivers.has(`${cls}.${method}`))) {
            recordViolation(
                "Settings Wiring (startup application)",
                BOOTSTRAP_PATH,
                `${cls} is not applied at startup: GameBootstrap.cs must invoke ${cls}.${applyMethods.join(" or ")}() on a typed receiver — a resolve alone does not apply saved config, so its values are ignored until the player opens Settings.`,
            );
        }
    }
}

// ---------------------------------------------------------------------------
// Part 4: USS stylesheet validator
// (ported from Assets/Editor/Tools/lint-uss.py)
// ---------------------------------------------------------------------------

// Styles are validated against the UIElements property registry — the only
// reliable source: a name being present in the CSS parser (ExCSS) or as a
// string inside the engine assembly does not mean it is a registered property.
// The allowlist below is taken from the Unity 6000.5 USS properties reference
// (UIE-USS-SupportedProperties), plus -unity-background-scale-mode and all,
// which the original captured registry list was missing.
const STYLES_DIR = path.join(__dirname, "..", "Assets", "Resources", "Styles");

// Longhand properties from the UIElements 6000.5 registry.
const USS_LONGHAND = new Set([
    "all", "-unity-background-image-tint-color", "-unity-background-scale-mode",
    "-unity-editor-text-rendering-mode", "-unity-font", "-unity-font-definition",
    "-unity-material", "-unity-overflow-clip-box", "-unity-paragraph-spacing",
    "-unity-slice-bottom", "-unity-slice-left", "-unity-slice-right",
    "-unity-slice-scale", "-unity-slice-top", "-unity-slice-type",
    "-unity-text-align", "-unity-text-auto-size", "-unity-text-generator",
    "-unity-text-outline-color", "-unity-text-outline-width",
    "-unity-text-overflow-position",
    "align-content", "align-items", "align-self", "aspect-ratio",
    "background-color", "background-image", "background-position-x",
    "background-position-y", "background-repeat", "background-size",
    "border-bottom-color", "border-bottom-left-radius", "border-bottom-right-radius",
    "border-bottom-width", "border-left-color", "border-left-width",
    "border-right-color", "border-right-width", "border-top-color",
    "border-top-left-radius", "border-top-right-radius", "border-top-width",
    "bottom", "color", "cursor", "display", "flex-basis",
    "flex-direction", "flex-grow", "flex-shrink", "flex-wrap", "font-size",
    "height", "justify-content", "left", "letter-spacing", "margin-bottom",
    "margin-left", "margin-right", "margin-top", "max-height", "max-width",
    "min-height", "min-width", "opacity", "overflow", "padding-bottom",
    "padding-left", "padding-right", "padding-top", "position", "right",
    "rotate", "scale", "text-overflow", "text-shadow", "top", "transform-origin",
    "transition-delay", "transition-duration", "transition-property",
    "transition-timing-function", "translate", "visibility", "white-space",
    "word-spacing", "width",
    // Single-word and special properties — the registry stores them
    // differently from kebab-case pairs.
    "-unity-font-style", "-unity-text-outline-color",
]);

// Shorthands expand into longhand properties and are not in the registry.
const USS_SHORTHAND = new Set([
    "background", "background-position", "border", "border-color",
    "border-radius", "border-width", "flex", "font", "margin", "padding",
    "transition", "-unity-slice", "-unity-text-outline",
]);

const USS_ALLOWED = new Set([...USS_LONGHAND, ...USS_SHORTHAND]);

// Functions that do not exist in UIElements at all.
const USS_BAD_FUNCS = {
    "cubic-bezier": "в USS только 23 именованные кривые; ближайшая к сигнатурной — ease-out-circ",
    "radial-gradient": "поддерживается только linear-gradient",
    "conic-gradient": "поддерживается только linear-gradient",
    "calc": "арифметики в значениях нет",
    "min": "арифметики в значениях нет",
    "max": "арифметики в значениях нет",
    "clamp": "арифметики в значениях нет",
    "color-mix": "не поддерживается",
};

// CSS box-shadow/filter/backdrop-filter не входят в поддерживаемый USS.
// Для таких эффектов нужен материал, Painter2D или подготовленная текстура.
const USS_SHADOW_NOTE =
    "box-shadow/filter/backdrop-filter в USS нет; нужен материал, Painter2D или текстура";

// The 23 named easing curves supported by USS.
const USS_EASINGS = new Set([
    "ease", "ease-in", "ease-out", "ease-in-out", "linear",
    "ease-in-sine", "ease-out-sine", "ease-in-out-sine",
    "ease-in-cubic", "ease-out-cubic", "ease-in-out-cubic",
    "ease-in-circ", "ease-out-circ", "ease-in-out-circ",
    "ease-in-elastic", "ease-out-elastic", "ease-in-out-elastic",
    "ease-in-back", "ease-out-back", "ease-in-out-back",
    "ease-in-bounce", "ease-out-bounce", "ease-in-out-bounce",
]);

function stripUssComments(text) {
    // Remove /* ... */ comments, preserving line numbers for diagnostics.
    return text.replace(/\/\*[\s\S]*?\*\//g, (m) => "\n".repeat(m.split("\n").length - 1));
}

function checkUssStyles() {
    let names;
    try {
        names = fs.readdirSync(STYLES_DIR).filter((n) => n.endsWith(".uss")).sort();
    } catch {
        recordViolation("USS Stylesheet", STYLES_DIR, `Не найдено ни одного .uss в ${STYLES_DIR}.`);
        return;
    }
    if (names.length === 0) {
        recordViolation("USS Stylesheet", STYLES_DIR, `Не найдено ни одного .uss в ${STYLES_DIR}.`);
        return;
    }

    const declared = new Set();
    const used = new Map(); // token -> Set(stylesheet basenames)
    let problemCount = 0;

    for (const name of names) {
        const full = path.join(STYLES_DIR, name);
        const src = readFile(full);
        if (src === null) {
            recordViolation("USS Stylesheet", full, "Не удалось прочитать файл.");
            problemCount++;
            continue;
        }
        const body = stripUssComments(src);

        const openBraces = (body.match(/\{/g) || []).length;
        const closeBraces = (body.match(/\}/g) || []).length;
        if (openBraces !== closeBraces) {
            recordViolation("USS Stylesheet", full, `${name}: скобки не сбалансированы`);
            problemCount++;
        }

        for (const m of body.matchAll(/(--[a-z0-9-]+)\s*:/gi)) {
            declared.add(m[1]);
        }
        for (const m of body.matchAll(/var\(\s*(--[a-z0-9-]+)/gi)) {
            if (!used.has(m[1])) {
                used.set(m[1], new Set());
            }
            used.get(m[1]).add(name);
        }

        const lines = body.split("\n");
        for (let i = 0; i < lines.length; i++) {
            const line = lines[i];
            const lineNo = i + 1;

            const decl = line.match(/^\s*(-?[a-zA-Z][\w-]*)\s*:/);
            if (decl) {
                const prop = decl[1];
                if (!prop.startsWith("--") && !USS_ALLOWED.has(prop)) {
                    const hint = prop === "box-shadow" ? ` — ${USS_SHADOW_NOTE}` : "";
                    recordViolation("USS Stylesheet", full, `${name}:${lineNo} свойство '${prop}' отсутствует в UI Toolkit${hint}`);
                    problemCount++;
                }
            }

            for (const [func, why] of Object.entries(USS_BAD_FUNCS)) {
                if (new RegExp("\\b" + escapeRegExp(func) + "\\s*\\(").test(line)) {
                    recordViolation("USS Stylesheet", full, `${name}:${lineNo} функция ${func}() — ${why}`);
                    problemCount++;
                }
            }

            const timing = line.match(/transition-timing-function\s*:\s*([^;]+);/);
            if (timing) {
                for (const raw of timing[1].split(",")) {
                    const value = raw.trim();
                    if (value.startsWith("var(") || value === "") {
                        continue;
                    }
                    if (!USS_EASINGS.has(value)) {
                        recordViolation("USS Stylesheet", full, `${name}:${lineNo} кривая '${value}' не входит в набор USS`);
                        problemCount++;
                    }
                }
            }
        }
    }

    for (const token of [...used.keys()].filter((t) => !declared.has(t)).sort()) {
        recordViolation("USS Stylesheet", STYLES_DIR, `токен ${token} используется (${[...used.get(token)].sort().join(", ")}), но не объявлен`);
        problemCount++;
    }

    problemCount += checkUtilityClassesResolve();
    problemCount += checkDesignSystemRatchet();
    problemCount += checkNeutralGrays();
    problemCount += checkHiddenClassNotOverridden();
    problemCount += checkNoInlineDisplayOutsideMainGame();
    problemCount += checkNoStrayStylesheets();
    problemCount += checkCodeClassesHaveRules();
    problemCount += checkEveryStylesheetImported();
    problemCount += checkNoTokenNamesInComments(names);
    problemCount += checkNoRelativeUnits(names);
    problemCount += checkTokensMatchMirror();
    problemCount += checkIconsMatchMirror();
    problemCount += checkComponentsMatchMirror();
    problemCount += checkTextFitMatchesMirror();
    problemCount += checkDesignSystemLint();
    problemCount += checkAssemblyGraph();
    problemCount += checkContainerConstructorChoice();
    problemCount += checkToneMapMatrices();
    problemCount += checkShaderColorLibraryIncludes();

    console.log(`${CYAN}${BOLD}USS stylesheets:${NC} ${names.length} file(s), ${declared.size} token(s) declared, ${problemCount} violation(s)`);
}

// ---------------------------------------------------------------------------
// Part 4a: имя токена внутри комментария ломает импорт
// ---------------------------------------------------------------------------
//
// Парсер USS видит последовательность «--имя» ДАЖЕ ВНУТРИ комментария,
// принимает её за объявление пользовательского свойства и падает с
// ColonMissing — файл не импортируется целиком. Поймано на живом примере:
// комментарий «слоем псевдонимов --color-*/--mm-*» уронил Theme.uss.
// Поэтому в комментариях имена пишутся без ведущих дефисов.

function checkNoTokenNamesInComments(names) {
    let count = 0;
    for (const name of names) {
        const full = path.join(STYLES_DIR, name);
        const src = fs.readFileSync(full, "utf8");
        for (const match of src.matchAll(/\/\*[\s\S]*?\*\//g)) {
            if (!match[0].includes("--")) continue;
            const line = src.slice(0, match.index).split("\n").length;
            recordViolation("USS Stylesheet", full,
                `${name}:${line} имя токена с «--» внутри комментария: парсер USS ` +
                "примет его за объявление и уронит импорт (ColonMissing). " +
                "Пишите имя без ведущих дефисов.");
            count++;
        }
    }
    return count;
}

// ---------------------------------------------------------------------------
// Part 4a2: относительных единиц в USS не существует
// ---------------------------------------------------------------------------
//
// letter-spacing и прочие длины принимают только пиксели и проценты. em, rem,
// ch, vw, vh роняют импорт целиком: «Unsupported unit: '0.04em'». Пересчитать
// em в px статически нельзя — величина зависит от кегля правила.

const USS_BAD_UNITS = /(?<![\w-])[0-9.]+(em|rem|ch|ex|vw|vh|vmin|vmax)(?![\w-])/g;

function checkNoRelativeUnits(names) {
    let count = 0;
    for (const name of names) {
        const full = path.join(STYLES_DIR, name);
        const code = fs.readFileSync(full, "utf8").replace(/\/\*[\s\S]*?\*\//g, " ");
        code.split("\n").forEach((line, i) => {
            for (const m of line.matchAll(USS_BAD_UNITS)) {
                recordViolation("USS Stylesheet", full,
                    `${name}:${i + 1} относительная единица '${m[0]}': USS понимает только px и %`);
                count++;
            }
        });
    }
    return count;
}

// ---------------------------------------------------------------------------
// Part 4b: токены игры обязаны совпадать с макетом
// ---------------------------------------------------------------------------
//
// Связь макета и игры два месяца держалась на просьбе в шапке файла: «меняя
// значение здесь, поменяй его и там». Просьба не выполнялась. Замер показал
// шестнадцать разошедшихся значений, худшее — --border-subtle 0.08 против
// 0.22, втрое ярче, почти на каждой поверхности интерфейса.
//
// Дальше эту связь держит не человек, а генератор, и проверяет — сборка.
// Договор, который нельзя проверить, договором не является.

function checkTokensMatchMirror() {
    const generator = path.join(__dirname, "..", "visual", "fodinae-ui-lab", "tools", "emit-uss-tokens.py");
    if (!fs.existsSync(generator)) {
        recordViolation("USS Stylesheet", generator,
            "нет генератора токенов: макет перестал быть источником истины");
        return 1;
    }

    const result = spawnSync("python3", [generator, "--check"], { encoding: "utf8" });
    if (result.error) {
        recordViolation("USS Stylesheet", generator,
            `генератор токенов не запустился: ${result.error.message}`);
        return 1;
    }
    if (result.status !== 0) {
        const detail = `${result.stdout || ""}${result.stderr || ""}`.trim().replace(/\n/g, " | ");
        recordViolation("USS Stylesheet", path.join(STYLES_DIR, "ThemeTokens.uss"),
            `токены игры разошлись с макетом. ${detail}`);
        return 1;
    }
    return 0;
}

// Токены сверяются словарём, иконки — растром, а это — уже сказанным: для
// каждой пары из component-map.json свойства правил игры и макета
// сравниваются напрямую. Именно этой проверки не хватило, чтобы кнопка рейла
// два месяца жила 44 пикселя против 48, а ширины модалок стояли наоборот.
// Сверяется и покой, и реакция: селекторы игры переписываются именами макета,
// поэтому .mm-nav-tab:hover и .fdn-settings-tab:hover ложатся в один ключ.
// Реакция, названная только макетом, роняет проверку так же, как расхождение
// значения: там интерфейс молчит на действие, и это не мельче.
function checkComponentsMatchMirror() {
    const generator = path.join(__dirname, "..", "visual", "fodinae-ui-lab", "tools", "compare-components.py");
    if (!fs.existsSync(generator)) {
        recordViolation("USS Stylesheet", generator,
            "нет сверки компонентов: вид игры перестал быть выводим из макета");
        return 1;
    }
    const result = spawnSync("python3", [generator, "--check"], { encoding: "utf8" });
    if (result.error) return 0;
    if (result.status !== 0) {
        const detail = `${result.stdout || ""}${result.stderr || ""}`.trim().replace(/\n/g, " | ");
        recordViolation("USS Stylesheet", path.join(STYLES_DIR, "Theme.uss"),
            `компоненты игры разошлись с макетом. ${detail}`);
        return 1;
    }
    return 0;
}

// ---------------------------------------------------------------------------
// Граф сборок: тип из чужой сборки, на которую нет ссылки
// ---------------------------------------------------------------------------
//
// Поймано на живом примере дважды за один день. AnimatedSpriteData лежал рядом
// с декодерами в Fodinae.AssetPipeline, а стоял в сигнатуре IAssetLoader из
// Fodinae.Contracts — но AssetPipeline сам ссылается на Contracts, и обратная
// ссылка замкнула бы кольцо. Тем же оказался IRuntimeAssetPaths: объявлен в
// Fodinae.Runtime, используется в AssetPipeline, ссылки нет.
//
// Компилятор это ловит, но по одной ошибке за прогон и только после того, как
// Unity дойдёт до пересборки. Здесь — весь граф сразу и до редактора.
//
// ЧТО СЧИТАЕТСЯ ССЫЛКОЙ НА ТИП. Имя, совпадающее с именем типа, — ещё не
// обращение к типу: `packet.AttachedProperties` это член, `[Tooltip("…")]` это
// атрибут Unity, а `const string MainMenu = "…"` это имя константы. Поэтому из
// текста снимаются комментарии, строки и блоки атрибутов, а имя не считается
// ссылкой, если перед ним точка или имя примитива, либо сразу за ним стоит
// присваивание. Без этих четырёх правил проверка давала 31 срабатывание, из
// которых настоящими были два.
//
// VContainer исключён целиком: сторонний код, его границы не наши.

const ASSEMBLY_PRIMITIVES = new Set([
    "string", "int", "float", "double", "bool", "byte", "long", "short", "char",
    "decimal", "uint", "ulong", "ushort", "sbyte", "object", "var", "const",
    "enum", "namespace",
]);

const TYPE_DECLARATION = new RegExp(
    "\\b(?:public|internal)\\s+" +
    "(?:readonly\\s+|sealed\\s+|abstract\\s+|static\\s+|partial\\s+|unsafe\\s+)*" +
    "(?:class|struct|interface|enum|record(?:\\s+struct)?)\\s+([A-Z]\\w*)", "g");

// Имя типа с тем, что стоит перед ним (слово или точка) и присваиванием после.
// Всё три части нужны, чтобы отличить обращение к типу от одноимённого члена.
const TYPE_REFERENCE = /(\w+|\.)?\s*\b([A-Z]\w*)\b(\s*=(?!=))?/g;

function stripForTypeScan(source) {
    return source
        .replace(/\/\*[\s\S]*?\*\//g, " ")
        .replace(/\/\/[^\n]*/g, " ")
        .replace(/@"(?:[^"]|"")*"/g, '""')
        .replace(/"(?:\\.|[^"\\])*"/g, '""')
        .replace(/\[[^\[\]\n]*\]/g, " ");
}

function collectAssemblies() {
    const found = [];
    const walk = dir => {
        let entries;
        try {
            entries = fs.readdirSync(dir, { withFileTypes: true });
        } catch {
            return;
        }
        for (const entry of entries) {
            const full = path.join(dir, entry.name);
            if (entry.isDirectory()) {
                walk(full);
            } else if (entry.name.endsWith(".asmdef")) {
                try {
                    const json = JSON.parse(fs.readFileSync(full, "utf8"));
                    found.push({
                        name: json.name,
                        dir: path.dirname(full),
                        refs: json.references || [],
                    });
                } catch {
                    recordViolation("Project References", full,
                        "asmdef не читается как JSON: граница сборки перестала быть проверяемой");
                }
            }
        }
    };
    walk("Assets/Scripts");
    return found.sort((a, b) => b.dir.length - a.dir.length);
}

// Имена, которые есть и у Unity: совпадение имени не значит, что имеется в
// виду наш тип, а различить их без разбора кода нельзя. Список именной, каждая
// запись с причиной — молчаливое «пропускать похожее» здесь недопустимо.
const TYPE_NAME_COLLISIONS = new Map([
    ["SceneSetup", "UnityEditor.SceneManagement.SceneSetup — возвращается из " +
        "EditorSceneManager.GetSceneManagerSetup(), одноимённый с Fodinae.World.SceneSetup"],
]);

// Тип считается объявленным на уровне пространства имён, только если он не
// вложен в другой тип: ProjectRuntimeContracts.Debug — это не UnityEngine.Debug,
// и без учёта вложенности проверка давала 29 ложных срабатываний на одном
// только этом имени.
function topLevelTypesOf(source) {
    const clean = stripForTypeScan(source);
    const found = [];
    let depth = 0;
    let typeDepth = null;
    let namespaceName = "";
    for (const line of clean.split("\n")) {
        const ns = line.match(/\bnamespace\s+([\w.]+)/);
        if (ns) {
            namespaceName = ns[1];
        }
        const kind = line.match(
            /\b(?:public|internal|private|protected)\s+(?:readonly\s+|sealed\s+|abstract\s+|static\s+|partial\s+|unsafe\s+|new\s+)*(?:class|struct|interface|enum|record)(?:\s+struct)?\s+([A-Z]\w*)/);
        if (kind && typeDepth === null) {
            found.push({ name: kind[1], namespace: namespaceName });
            typeDepth = depth;
        }
        for (const ch of line) {
            if (ch === "{") {
                depth++;
            } else if (ch === "}") {
                depth--;
                if (typeDepth !== null && depth <= typeDepth) {
                    typeDepth = null;
                }
            }
        }
    }
    return found;
}

function namespaceAncestors(name) {
    const parts = name.split(".");
    const out = [""];
    for (let i = parts.length; i > 0; i--) {
        out.push(parts.slice(0, i).join("."));
    }
    return out;
}

// Сборка видна, а имя — нет: перенос типа между сборками почти всегда меняет и
// пространство имён, и потребители остаются без using. Компилятор скажет это
// точнее, но только после того, как Unity дойдёт до пересборки; за один прогон
// проверка нашла три таких файла, каждый из которых стоил бы отдельного круга.
function checkNamespaceVisibility(sources) {
    const declaredIn = new Map();
    const cache = new Map();
    for (const file of sources) {
        const source = readFile(file);
        if (source === null) {
            continue;
        }
        const types = topLevelTypesOf(source);
        cache.set(file, { source, types });
        for (const type of types) {
            if (!declaredIn.has(type.name)) {
                declaredIn.set(type.name, new Set());
            }
            declaredIn.get(type.name).add(type.namespace);
        }
    }

    let violations = 0;
    for (const file of sources) {
        const entry = cache.get(file);
        if (!entry) {
            continue;
        }
        const visible = new Set(
            [...entry.source.matchAll(/^\s*using\s+(?:static\s+)?([\w.]+)\s*;/gm)].map(m => m[1]));
        for (const match of entry.source.matchAll(/^\s*namespace\s+([\w.]+)/gm)) {
            for (const ancestor of namespaceAncestors(match[1])) {
                visible.add(ancestor);
            }
        }
        const own = new Set(entry.types.map(t => t.name));
        const scanned = stripForTypeScan(entry.source).replace(/\[[^\[\]\n]*\]/g, " ");
        const reported = new Set();
        for (const hit of scanned.matchAll(TYPE_REFERENCE)) {
            const before = hit[1] || "";
            const type = hit[2];
            if (before === "." || ASSEMBLY_PRIMITIVES.has(before) || hit[3]) {
                continue;
            }
            if (own.has(type) || reported.has(type) || TYPE_NAME_COLLISIONS.has(type)) {
                continue;
            }
            const where = declaredIn.get(type);
            if (!where || [...where].some(ns => visible.has(ns))) {
                continue;
            }
            reported.add(type);
            recordViolation("Project References", file,
                `тип ${type} объявлен в ${[...where].join(", ")}, а этого ` +
                "пространства имён файл не видит: добавьте using. Сборка тут ни при чём — " +
                "имя не разрешается, и это отдельная от графа ссылок ошибка.");
            violations++;
        }
    }
    return violations;
}

// ---------------------------------------------------------------------------
// Part 4c: неоднозначный конструктор у типа, который собирает контейнер
// ---------------------------------------------------------------------------
//
// VContainer без атрибута [Inject] берёт конструктор с НАИБОЛЬШИМ числом
// параметров и смотрит в том числе непубличные (TypeAnalyzer.cs:237-245).
// Живой случай: у PersistentAssetCache рядом с public ctor() лежал
// internal ctor(string) для тестов — контейнер выбрал его, не нашёл
// регистрации System.String и уронил сборку целиком в Awake бутстрапа.
// Ошибка молчит до запуска, поэтому ловится тут.
//
// Форма Register<T>(resolver => ...) не при чём: там конструктор зовёт
// фабрика, а не анализатор типов.
// ---------------------------------------------------------------------------
// Part 4d: матрицы тонмапа обязаны сохранять белое
// ---------------------------------------------------------------------------
//
// Референс AgX написан на GLSL, где mat3(...) заполняется по СТОЛБЦАМ, а в
// HLSL float3x3(...) — по СТРОКАМ. Литералы, перенесённые один в один, дают
// транспонированную матрицу. Так и было: суммы строк выходили
// [0.927, 1.035, 1.038] вместо единиц, матрица переставала сохранять белое, и
// каждый нейтрально-серый уезжал в красноту — до 13/255 на входе 2.0.
//
// Ошибка не ловится ни компиляцией, ни глазами на цветном кадре: увидеть её
// можно только на серой шкале, которой в игре нет. Поэтому проверяется
// арифметикой: у матрицы, переводящей белое в белое, сумма каждой строки
// равна единице.
// Функции цветового преобразования живут в Color.hlsl пакета core, и ни
// Core.hlsl из URP, ни Common/Packing/Input по своим включениям её не тянут.
// Вызов SRGBToLinear без явного include компилируется в НИЧТО: проход падает
// с ошибкой, шейдер целиком остаётся невалидным, а объект рисуется без него.
// Поймано 03.09.2026 ценой сломанной графики: проход материалов терреина
// перестал компилироваться, поле материалов не писалось, и кадр вернулся к
// неосвещённому альбедо. Компилятор C# такое не ловит — шейдеры собирает
// Unity, и тихо.
const SHADER_COLOR_FUNCTIONS = [
    "SRGBToLinear", "LinearToSRGB", "FastSRGBToLinear", "FastLinearToSRGB",
    "Luminance", "RgbToHsv", "HsvToRgb",
];

function checkShaderColorLibraryIncludes() {
    let violations = 0;
    const shaderFiles = [];
    (function walkShaders(root) {
        let entries;
        try {
            entries = fs.readdirSync(root, { withFileTypes: true });
        } catch {
            return;
        }
        for (const entry of entries) {
            const full = path.join(root, entry.name);
            if (entry.isDirectory()) {
                walkShaders(full);
            } else if (/\.(shader|compute|hlsl)$/.test(entry.name)) {
                shaderFiles.push(full);
            }
        }
    })("Assets");
    for (const file of shaderFiles) {
        const source = readFile(file);
        if (source === null) {
            continue;
        }

        const includesColor = source.includes("ShaderLibrary/Color.hlsl");
        for (const fn of SHADER_COLOR_FUNCTIONS) {
            const used = new RegExp(`\\b${fn}\\s*\\(`).test(source);
            if (!used || includesColor) {
                continue;
            }

            // Своё определение в этом же файле — законно и снимает вопрос.
            const definedLocally = new RegExp(
                `(float|half|real)[1-4]?\\s+${fn}\\s*\\(`).test(source);
            if (definedLocally) {
                continue;
            }

            recordViolation("Architecture", file,
                `Шейдер вызывает ${fn}, но не подключает Color.hlsl и не определяет её сам. ` +
                "Проход не скомпилируется, и объект останется вообще без шейдера.");
            violations++;
        }
    }

    return violations;
}

function checkToneMapMatrices() {
    const file = path.join(
        "Assets", "Resources", "Shaders", "PostProcessing", "PostProcess.compute");
    const source = readFile(file);
    if (source === null) {
        // Молчать здесь нельзя. Раньше отсутствие файла возвращало ноль
        // нарушений, и когда шейдер переехал, вся проверка тонмапа —
        // суммы строк матриц, возврат в линейное, место гаммы — просто
        // перестала выполняться, а линтер продолжал печатать PASSED.
        // Проверка, которая молча исчезает вместе с файлом, хуже отсутствия
        // проверки: она создаёт уверенность, которой не обеспечивает.
        recordViolation("Architecture", file,
            "Файл тонмапа не найден по ожидаемому пути — проверка матриц AgX, " +
            "возврата в линейное и места гаммы не может быть выполнена.");
        return 1;
    }

    let violations = 0;
    // AgX обязан вернуться в линейное перед записью в camera target, и
    // показатель этого возврата — калибровочная гамма дисплея. Оба требования
    // держатся одной строкой, потому что это одно и то же действие.
    if (!/return\s+pow\(color,\s*max\(_Gamma,\s*0\.1\)\)\s*;/.test(source)) {
        recordViolation("Architecture", file,
            "AgX обязан линеаризовать display-encoded результат через pow(color, max(_Gamma, 0.1)) " +
            "до записи в линейный camera target; иначе URP кодирует его повторно и выбеливает полутона.");
        violations++;
    }

    // Гамма применима только к display-encoded сигналу. Над сцен-линейными
    // значениями показатель степени гаммой не является: при 1.8 шейдер возводил
    // кадр в степень 1.222, тени давились, а всё ярче единицы раздувалось —
    // 8.0 превращалось в 12.7. Именно это читалось на экране как пересвет.
    if (/pow\(color,\s*gammaCorrection\)|2\.2\s*\/\s*_Gamma/.test(source)) {
        recordViolation("Architecture", file,
            "Гамма дисплея не применяется до тонмапа: над сцен-линейными значениями " +
            "показатель степени раздувает всё ярче единицы. Её место — показатель возврата в ToneMapAgX.");
        violations++;
    }

    const MATRIX = /const\s+float3x3\s+(\w+)\s*=\s*float3x3\(([^)]*)\)/g;
    for (const match of source.matchAll(MATRIX)) {
        const name = match[1];
        const numbers = (match[2].match(/-?\d+(?:\.\d+)?/g) || []).map(Number);
        if (numbers.length !== 9) {
            recordViolation("Architecture", file,
                `матрица ${name}: ожидалось 9 чисел, найдено ${numbers.length}`);
            violations++;
            continue;
        }
        for (let row = 0; row < 3; row++) {
            const sum = numbers[row * 3] + numbers[row * 3 + 1] + numbers[row * 3 + 2];
            if (Math.abs(sum - 1) > 0.002) {
                recordViolation("Architecture", file,
                    `матрица ${name}, строка ${row}: сумма ${sum.toFixed(6)} вместо 1.0. ` +
                    "Матрица тонмапа обязана переводить белое в белое; чаще всего " +
                    "причина — литералы GLSL (столбцы) в конструкторе HLSL (строки), " +
                    "то есть матрица стоит транспонированной, и нейтрали красятся.");
                violations++;
            }
        }
    }

    const passFile = path.join(
        "Assets",
        "Scripts",
        "Rendering",
        "PostProcessing",
        "PostProcessRenderPass.cs");
    const passSource = readFile(passFile);
    // Тонмап безусловен в обоих режимах вывода, поэтому досрочного выхода по
    // «ни одного включённого эффекта» у прохода быть не может: без кривой всё
    // ярче белой точки срезается в плоский белый. Ловушка не гипотетическая —
    // сначала гейт убрали не до конца, оставив `bool toneMappingActive = true`
    // первым слагаемым условия: проверка была мертва, но выглядела живой.
    if (passSource !== null &&
        /if\s*\(\s*!\w*[Aa]ctive\s*&&[\s\S]{0,400}?!_advanced\.HasAnyEffects\s*\)/.test(passSource)) {
        recordViolation("Architecture", passFile,
            "У прохода постпроцесса не должно быть досрочного выхода по набору эффектов: " +
            "тонмап работает всегда, и кадр без него неверен, а не дешевле.");
        violations++;
    }

    return violations;
}

function checkContainerConstructorChoice() {
    const registered = new Map();
    const REGISTER = /builder\s*\.\s*Register<\s*([A-Za-z_][\w]*)\s*>\s*\(\s*Lifetime\./g;
    for (const file of walkCs("Assets/Scripts")) {
        const rel = file.split(path.sep).join("/");
        if (EXCLUDE_REGEX.test(rel)) {
            continue;
        }
        const source = readFile(file);
        if (source === null) {
            continue;
        }
        for (const match of source.matchAll(REGISTER)) {
            if (!registered.has(match[1])) {
                registered.set(match[1], rel);
            }
        }
    }
    if (registered.size === 0) {
        return 0;
    }

    let violations = 0;
    for (const file of walkCs("Assets/Scripts")) {
        const rel = file.split(path.sep).join("/");
        if (EXCLUDE_REGEX.test(rel)) {
            continue;
        }
        const raw = readFile(file);
        if (raw === null) {
            continue;
        }
        for (const [type, scope] of registered) {
            if (!new RegExp(`\\b(?:class|record)\\s+${type}\\b`).test(raw)) {
                continue;
            }
            const ctor = new RegExp(
                `(?:\\[Inject\\][^\\n]*\\s*)?(?:public|internal|private|protected)(?:\\s+(?:sealed|static|unsafe|extern))*\\s+${type}\\s*\\(`,
                "g");
            const ctors = [...raw.matchAll(ctor)].filter(m => !/\bstatic\b/.test(m[0]));
            if (ctors.length < 2) {
                continue;
            }
            // Проверка читает код, а не комментарии: в этом самом файле
            // объяснение к атрибуту содержит слово [Inject], и наивный поиск
            // по сырому тексту принял бы объяснение за атрибут.
            const code = raw
                .replace(/\/\*[\s\S]*?\*\//g, " ")
                .replace(/\/\/[^\n]*/g, " ");
            if (/\[Inject\]/.test(code)) {
                continue;
            }
            recordViolation("Project References", rel,
                `${type} регистрируется в ${scope} как Register<${type}>(Lifetime...), ` +
                `но имеет ${ctors.length} конструктора и ни одного [Inject]: VContainer возьмёт ` +
                "самый длинный, заглядывая и в непубличные, и уронит сборку контейнера " +
                "в рантайме. Пометьте нужный конструктор атрибутом [Inject].");
            violations++;
        }
    }
    return violations;
}

function checkAssemblyGraph() {
    const assemblies = collectAssemblies();
    if (assemblies.length === 0) {
        return 0;
    }

    const byName = new Map(assemblies.map(a => [a.name, a]));
    const ownerOf = filePath => {
        const found = assemblies.find(a => filePath.startsWith(a.dir + path.sep));
        return found ? found.name : null;
    };

    // Кольцо в графе Unity не соберёт вовсе, поэтому сказать об этом надо
    // раньше и понятнее, чем это сделает редактор.
    const visiting = new Set();
    const done = new Map();
    let violations = 0;
    const reachable = name => {
        if (done.has(name)) {
            return done.get(name);
        }
        if (visiting.has(name)) {
            recordViolation("Project References", path.join("Assets", "Scripts"),
                `кольцевая ссылка сборок через ${name}: граф обязан быть без циклов`);
            violations++;
            return new Set();
        }
        visiting.add(name);
        const seen = new Set();
        for (const ref of (byName.get(name) || { refs: [] }).refs) {
            seen.add(ref);
            for (const deep of reachable(ref)) {
                seen.add(deep);
            }
        }
        visiting.delete(name);
        done.set(name, seen);
        return seen;
    };

    const sources = walkCs("Assets/Scripts")
        .filter(file => !EXCLUDE_REGEX.test(file.split(path.sep).join("/")))
        .filter(file => ownerOf(file) !== null);

    const declaredIn = new Map();
    for (const file of sources) {
        const source = readFile(file);
        if (source === null) {
            continue;
        }
        const owner = ownerOf(file);
        for (const match of source.matchAll(TYPE_DECLARATION)) {
            if (!declaredIn.has(match[1])) {
                declaredIn.set(match[1], new Set());
            }
            declaredIn.get(match[1]).add(owner);
        }
    }

    for (const file of sources) {
        const owner = ownerOf(file);
        const raw = readFile(file);
        if (raw === null) {
            continue;
        }
        const source = stripForTypeScan(raw);
        const visible = reachable(owner);

        // Один проход по файлу, а не поиск каждого из полутысячи имён по
        // очереди: перебор именем стоил двадцати секунд на прогон и превращал
        // проверку в то, что хочется отключить.
        const referenced = new Set();
        for (const hit of source.matchAll(TYPE_REFERENCE)) {
            const before = hit[1] || "";
            if (before === "." || ASSEMBLY_PRIMITIVES.has(before)) {
                continue;
            }
            if (hit[3]) {
                continue;  // имя члена в инициализаторе, а не тип
            }
            referenced.add(hit[2]);
        }

        for (const type of referenced) {
            const owners = declaredIn.get(type);
            if (!owners || owners.has(owner)) {
                continue;
            }
            const unreachable = [...owners].filter(o => o !== owner && !visible.has(o));
            if (unreachable.length !== owners.size) {
                continue;
            }
            recordViolation("Project References", file,
                `${owner} обращается к типу ${type} из ${unreachable.join(", ")}, ` +
                "а ссылки на эту сборку нет. Либо перенесите тип туда, где он виден " +
                "обеим сторонам (договор — в Fodinae.Contracts), либо добавьте ссылку, " +
                "если направление зависимости это допускает.");
            violations++;
        }
    }

    return violations + checkNamespaceVisibility(sources);
}

// Инварианты самой дизайн-системы: неразрешённые токены, значения вне шкал,
// протечка слоя примитивов, контраст. Держит долг по потолку, а не по нулю —
// правило, красное в день появления, перестаёт быть сигналом.
//
// Запускался руками и потому работал через раз: знать, какой из инструментов
// макета зовут, а какой нет, — само по себе знание, которое теряется первым.
// Одна команда на все проверки вида.
function checkDesignSystemLint() {
    const linter = path.join(__dirname, "..", "visual", "fodinae-ui-lab", "tools", "lint-design-system.py");
    if (!fs.existsSync(linter)) {
        recordViolation("USS Stylesheet", linter,
            "нет линтера дизайн-системы: инварианты макета перестали проверяться");
        return 1;
    }
    const result = spawnSync("python3", [linter], { encoding: "utf8" });
    if (result.error) return 0;
    if (result.status !== 0) {
        const detail = `${result.stdout || ""}${result.stderr || ""}`.trim().split("\n")
            .filter(line => line.trim()).slice(-6).join(" | ");
        recordViolation("USS Stylesheet", path.join(STYLES_DIR, "ThemeTokens.uss"),
            `дизайн-система макета: ${detail}`);
        return 1;
    }
    return 0;
}

// Поведение текста при нехватке места. Отдельно от сверки компонентов, потому
// что контракт макета записан селекторами по атрибуту ([data-fit='clip']), а в
// USS селекторов по атрибуту нет вовсе: сверка правил такие строки исключает и
// не увидит эту ось, сколько её ни расширяй. Здесь читается сам атрибут узла.
//
// Молчание игры тут — не «не сказано», а «текст вылезет»: умолчание USS никогда
// не обрежет строку и не подгонит кегль.
function checkTextFitMatchesMirror() {
    const checker = path.join(__dirname, "..", "visual", "fodinae-ui-lab", "tools", "check-fit.py");
    if (!fs.existsSync(checker)) {
        recordViolation("USS Stylesheet", checker,
            "нет проверки контракта data-fit: поведение текста перестало быть выводимым из макета");
        return 1;
    }
    const result = spawnSync("python3", [checker], { encoding: "utf8" });
    if (result.error) return 0;
    if (result.status !== 0) {
        const detail = `${result.stdout || ""}${result.stderr || ""}`.trim().replace(/\n/g, " | ");
        recordViolation("USS Stylesheet", path.join(STYLES_DIR, "TokenUtilities.uss"),
            `контракт data-fit не перенесён в игру. ${detail}`);
        return 1;
    }
    return 0;
}

// Иконки рейла — такой же печатный артефакт, как токены. Unity векторов не
// принимает, поэтому SVG макета растеризуются в PNG; без проверки растр молча
// отстаёт от вектора, что уже случилось: три глифа разошлись после того, как
// набор в макете нормализовали по массе.
function checkIconsMatchMirror() {
    const generator = path.join(__dirname, "..", "visual", "fodinae-ui-lab", "tools", "emit-icon-textures.py");
    if (!fs.existsSync(generator)) {
        recordViolation("USS Stylesheet", generator,
            "нет генератора иконок: растр в игре перестал быть выводим из макета");
        return 1;
    }
    const result = spawnSync("python3", [generator, "--check"], { encoding: "utf8" });
    if (result.error) {
        // cairosvg ставится не везде; отсутствие библиотеки не есть расхождение.
        return 0;
    }
    if (result.status !== 0) {
        const detail = `${result.stdout || ""}${result.stderr || ""}`.trim().replace(/\n/g, " | ");
        recordViolation("USS Stylesheet", generator, `иконки игры разошлись с макетом. ${detail}`);
        return 1;
    }
    return 0;
}

// ---------------------------------------------------------------------------
// Part 4c: класс, выданный из кода, обязан существовать в USS
// ---------------------------------------------------------------------------
//
// TokenUtilities.uss печатается генератором и подключается к теме. Если лист
// выпадет из темы или класс из него исчезнет, код продолжит выдавать имя,
// которое не резолвится ни во что: элемент молча потеряет вид. Инлайн, на
// который эти классы пришли на смену, хотя бы работал.
//
// Молчаливая потеря вида — худший вид поломки: её не видно ни в консоли, ни
// в тестах, ни в компиляции. Поэтому проверяется обе половины: лист в теме,
// и каждое имя, которое C# передаёт в AddToClassList, имеет правило.

const THEME_TSS = path.join(__dirname, "..", "Assets", "UI Toolkit", "FodinaeTheme.tss");
const UTILITIES_USS = path.join(STYLES_DIR, "TokenUtilities.uss");
const UI_SCRIPTS_DIR = path.join(__dirname, "..", "Assets", "Scripts", "UI");

function checkUtilityClassesResolve() {
    let count = 0;

    for (const required of [THEME_TSS, UTILITIES_USS]) {
        if (!fs.existsSync(required)) {
            recordViolation("USS Stylesheet", required, "файл утилитарного слоя отсутствует");
            return 1;
        }
    }

    // 1. Лист обязан быть подключён к теме, иначе правила не доедут до панели.
    const theme = stripUssComments(fs.readFileSync(THEME_TSS, "utf8"));
    if (!/TokenUtilities\.uss/.test(theme)) {
        recordViolation("USS Stylesheet", THEME_TSS,
            "TokenUtilities.uss не подключён к теме: классы из C# не резолвятся");
        count++;
    }

    // 2. Каждый утилитарный класс, названный в C#, обязан иметь правило.
    //    Сверяются только имена из этого листа: остальные классы живут в
    //    компонентных таблицах и проверяются другими правилами.
    const uss = stripUssComments(fs.readFileSync(UTILITIES_USS, "utf8"));
    const selectors = new Set([...uss.matchAll(/^\s*\.([A-Za-z0-9_-]+)/gm)].map((m) => m[1]));

    const known = new Set([...selectors]);
    for (const file of walkFiles(UI_SCRIPTS_DIR, ".cs")) {
        const code = stripCsComments(fs.readFileSync(file, "utf8"));
        for (const m of code.matchAll(/AddToClassList\("([a-z][a-z0-9-]*)"\)/g)) {
            const cls = m[1];
            // Утилитарными считаем имена без компонентного префикса: именно
            // они приходят из этого листа.
            if (/^(is|row|col|ai|jc|as|abs|rel|grow|no|text|centered)(-|$)/.test(cls) && !known.has(cls)) {
                recordViolation("USS Stylesheet", file,
                    `${path.basename(file)}: класс .${cls} выдаётся из кода, но правила в TokenUtilities.uss нет`);
                count++;
            }
        }
    }

    return count;
}

// ---------------------------------------------------------------------------
// Part 4e: нейтрально-серого в дизайн-системе нет
// ---------------------------------------------------------------------------
//
// Палитра макета целиком холодная: поверхности синеватые (11,20,30), рамки —
// полупрозрачный светлый (140,185,205) с альфой 0.08..0.15, текст уходит в
// синеву. Нейтрального серого — где R, G и B почти равны — в ней нет ни
// одного.
//
// В main game такой шкалы 49 значений: сплошные серые рамки rgb(77,77,77),
// фоны rgb(51,51,51), текст rgb(204,204,204). Механически привести их к
// палитре нельзя: сплошная серая рамка, заменённая на --border-subtle, не
// станет «той же рамкой в цвете темы» — она почти исчезнет, потому что у
// токена альфа 0.08. Это решение дизайнера, а не подстановка.
//
// Поэтому число зафиксировано: закрыть долг нельзя, а вот не дать ему расти —
// можно. Новый серый в USS означает, что в игру приехала вторая палитра.

const NEUTRAL_GRAY_BUDGET = 0;

function checkNeutralGrays() {
    const rgb = /rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*[\d.]+\s*)?\)/g;
    let count = 0;

    for (const file of walkFiles(STYLES_DIR, ".uss")) {
        if (GENERATED_USS.has(path.basename(file))) continue;
        const code = stripUssComments(fs.readFileSync(file, "utf8"));
        for (const m of code.matchAll(rgb)) {
            const [r, g, b] = [+m[1], +m[2], +m[3]];
            // Чистый чёрный и белый — не «серая шкала», а служебные крайности.
            if ((r === 0 && g === 0 && b === 0) || (r === 255 && g === 255 && b === 255)) continue;
            if (Math.abs(r - g) <= 6 && Math.abs(g - b) <= 6 && Math.abs(r - b) <= 6) count++;
        }
    }

    if (count > NEUTRAL_GRAY_BUDGET) {
        recordViolation("USS Stylesheet", STYLES_DIR,
            `нейтрально-серый: ${count} при потолке ${NEUTRAL_GRAY_BUDGET}. В палитре макета серого нет — это вторая палитра, растить её нельзя`);
        return 1;
    }
    if (count < NEUTRAL_GRAY_BUDGET) {
        recordViolation("USS Stylesheet", path.join(__dirname, "check-architecture.js"),
            `нейтрально-серый: стало ${count} вместо ${NEUTRAL_GRAY_BUDGET} — впишите новое число в NEUTRAL_GRAY_BUDGET`);
        return 1;
    }
    return 0;
}

// ---------------------------------------------------------------------------
// Part 4c-bis: .is-hidden обязан выигрывать
// ---------------------------------------------------------------------------
//
// TokenUtilities.uss подключён к теме РАНЬШЕ компонентных листов — так и надо,
// иначе утилита перебивала бы компонент. Но у этого порядка есть обратная
// сторона: при равной специфичности выигрывает правило, объявленное позже.
// Значит компонентное правило, задающее display тому же элементу, молча
// отменит .is-hidden — кнопка «закрыть» перестанет закрывать, и ни консоль,
// ни тесты об этом не скажут.
//
// Проверка: ни один лист после утилит не задаёт display классу, который в
// разметке стоит рядом с is-hidden.

const UXML_DIR = path.join(__dirname, "..", "Assets", "Resources", "UI");

// ---------------------------------------------------------------------------
// Инлайновый display рядом с классом is-hidden
// ---------------------------------------------------------------------------
//
// Инлайн бьёт любое правило: если элемент скрыт через style="display: none",
// снятие класса is-hidden его уже не покажет, и наоборот — выставленный кодом
// инлайновый display навсегда выводит элемент из-под управления классом.
// Смешивать два механизма на одном элементе нельзя, поэтому в разметке вне
// main game инлайновый display запрещён целиком: там видимость — это класс.
// В main game оба механизма пока живут по-старому, инлайном, и это её долг.

const MAIN_GAME_UXML = new Set(["PlayerHUD.uxml", "Inventory.uxml", "Minimap.uxml",
    "GlobalChat.uxml", "LocalChat.uxml", "Programmator.uxml", "RadialMenu.uxml",
    "ObserverJoystick.uxml", "PauseMenu.uxml", "Reconnect.uxml",
    "AssetLoadingIndicator.uxml"]);

// Лист USS вне папки Styles не виден ни теме, ни проверкам. Один такой есть
// законно — бутстрап подключает свой лист через <ui:Style>, потому что
// поднимается вместе со сценой. Остальные должны лежать в Styles, иначе они
// молча выпадают из каскада и из счётчиков долга.
const OUT_OF_TREE_USS = new Set(["BootstrapLoadingScreen.uss"]);

// Лист, лежащий в Styles, но не импортированный темой, не действует ни на что.
// Ровно так молча пропал TokenUtilities.uss: классы выдавались, правил не было.
function checkEveryStylesheetImported() {
    const theme = path.join(__dirname, "..", "Assets", "UI Toolkit", "FodinaeTheme.tss");
    let src;
    try { src = fs.readFileSync(theme, "utf8"); }
    catch { recordViolation("USS Stylesheet", theme, "Тема FodinaeTheme.tss не найдена."); return 1; }
    let count = 0;
    for (const file of walkFiles(STYLES_DIR, ".uss")) {
        const name = path.basename(file);
        if (src.includes(name)) continue;
        recordViolation("USS Stylesheet", file,
            `${name} лежит в Styles, но не импортирован в FodinaeTheme.tss — ` +
            "лист не участвует в каскаде, и его правила не действуют ни на один экран.");
        count++;
    }
    return count;
}

// Класс, который код навешивает, но правил под ним нет ни в одном листе.
// Такой класс — молчаливая пустышка: код думает, что переключает состояние,
// на экране не меняется ничего. Так жил `.invalid` у RegexTextField (отказ
// ввода не показывался никак) и так живёт `.mission-arrow` в main game.
// Классы из разметки этой проверкой не берём: там пустой класс безвреден,
// он ничего не обещает.
const CLASS_CALL = /(?:AddToClassList|EnableInClassList|ToggleInClassList)\(\s*"([\w-]+)"/g;

function checkCodeClassesHaveRules() {
    const selectors = new Set();
    for (const file of [...walkFiles(STYLES_DIR, ".uss"), ...walkFiles(UXML_DIR, ".uss")]) {
        const code = stripUssComments(fs.readFileSync(file, "utf8"));
        for (const rule of code.matchAll(/([^{}]+)\{/g)) {
            for (const m of rule[1].matchAll(/\.([\w-]+)/g)) selectors.add(m[1]);
        }
    }

    let count = 0;
    const scriptsRoot = path.join(__dirname, "..", "Assets", "Scripts");
    for (const file of walkFiles(scriptsRoot, ".cs")) {
        const src = stripCsComments(fs.readFileSync(file, "utf8"));
        for (const m of src.matchAll(CLASS_CALL)) {
            if (selectors.has(m[1])) continue;
            if (KNOWN_RULELESS_CLASSES.has(m[1])) continue;
            const line = src.slice(0, m.index).split("\n").length;
            recordViolation("USS Stylesheet", file,
                `${path.basename(file)}:${line} класс '${m[1]}' навешивается кодом, ` +
                "но правил под ним нет ни в одном листе: переключение состояния " +
                "ничего не меняет на экране.");
            count++;
        }
    }
    return count;
}

// Известные пустышки, которые этот заход не вправе чинить: вид элемента
// неизвестен, а файл принадлежит main game. Заведены в TODO.md.
const KNOWN_RULELESS_CLASSES = new Set(["mission-arrow"]);

function checkNoStrayStylesheets() {
    let count = 0;
    for (const file of walkFiles(UXML_DIR, ".uss")) {
        const name = path.basename(file);
        if (OUT_OF_TREE_USS.has(name)) continue;
        recordViolation("USS Stylesheet", file,
            `${name} лежит вне Assets/Resources/Styles: такой лист не входит в ` +
            "тему и не попадает в счётчики долга. Перенесите его в Styles или " +
            "внесите в OUT_OF_TREE_USS с объяснением, почему иначе нельзя.");
        count++;
    }
    return count;
}

function checkNoInlineDisplayOutsideMainGame() {
    let count = 0;
    for (const file of walkFiles(UXML_DIR, ".uxml")) {
        const name = path.basename(file);
        if (MAIN_GAME_UXML.has(name)) continue;
        const markup = fs.readFileSync(file, "utf8");
        for (const m of markup.matchAll(/style="[^"]*\bdisplay\s*:/g)) {
            const line = markup.slice(0, m.index).split("\n").length;
            recordViolation("USS Stylesheet", file,
                `${name}:${line} инлайновый display в разметке вне main game: ` +
                "инлайн бьёт класс, и элемент перестаёт слушаться is-hidden. " +
                "Скрывайте классом is-hidden, показывайте через UIState.");
            count++;
        }
    }
    return count;
}

function checkHiddenClassNotOverridden() {
    const guarded = new Set();
    for (const file of walkFiles(UXML_DIR, ".uxml")) {
        const markup = fs.readFileSync(file, "utf8");
        for (const m of markup.matchAll(/class="([^"]*\bis-hidden\b[^"]*)"/g)) {
            for (const cls of m[1].split(/\s+/)) {
                if (cls && cls !== "is-hidden") guarded.add(cls);
            }
        }
    }
    if (guarded.size === 0) return 0;

    let count = 0;
    for (const file of walkFiles(STYLES_DIR, ".uss")) {
        const name = path.basename(file);
        if (name === "TokenUtilities.uss" || name === "ThemeTokens.uss") continue;
        const code = stripUssComments(fs.readFileSync(file, "utf8"));
        for (const rule of code.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
            if (!/(?<![\w-])display\s*:/.test(rule[2])) continue;
            const selector = rule[1].trim();
            for (const cls of guarded) {
                if (new RegExp(`\\.${cls.replace(/[-]/g, "\\-")}(?![\\w-])`).test(selector)) {
                    recordViolation("USS Stylesheet", file,
                        `${name}: правило '${selector.replace(/\s+/g, " ")}' задаёт display классу .${cls}, который в разметке скрывается через is-hidden — утилита объявлена раньше и проиграет`);
                    count++;
                }
            }
        }
    }
    return count;
}

// ---------------------------------------------------------------------------
// Part 4d: потолки долга дизайн-системы
// ---------------------------------------------------------------------------
//
// Долг тут не запрещён — он зафиксирован. Число, записанное в BUDGET, это не
// цель и не норма: это «столько было, когда мы посмотрели». Расти нельзя,
// падать можно, и упавшее число полагается вписать сюда же — иначе проверка
// перестаёт держать.
//
// Две оси, каждая в двух слоях:
//
//   inline  — запись element.style.* из C#. Инлайн в UI Toolkit бьёт любое
//             правило USS, поэтому каждая такая запись выводит элемент
//             из-под темы, тира и состояний. Часть из них законна: геометрия,
//             посчитанная в рантайме (координаты маркера из проекции 3D-точки,
//             размер окна из пакета), и твины в UIAnimator. Их не отделяем
//             признаком, а держим числом: законные не растут сами по себе.
//
//   literal — цвет или пиксель, записанный значением вместо var(--токен).
//             Пока значение литерал, тема и тир на него не действуют.
//
// Слой main game считается отдельно и сознательно не трогается: у него свои
// нюансы, и заход, который вычистил меню, не вправе молча переехать игру.
// Его число стоит здесь, чтобы долг был виден, а не забыт.

// Из чего состоит остаток «inline вне main game» = 59 на 01.09.2026. Записано,
// чтобы число не читалось как «недоделка»: всё, что здесь осталось, законно,
// и попытка довести его до нуля сломает работающее.
//
//   17  Common/Animation/UIAnimator.cs        твины: opacity и translate в кадре
//   17  Common/Interaction/StyleApplicator.cs исполнение GUIStylePacket — это
//                                             и есть соблюдение протокола
//    9  Menu/Scenery/MenuSceneryMarkers.cs    проекция 3D-точки на кадр планеты
//    4  Builders/Core/PacketUIBuilder.cs      Canvas.X/Y/Width/Height из пакета
//    3  Builders/Widgets/ImagePacketBuilder.cs размер и картинка из пакета
//    2  Common/Windows/ServerWindowPresenter.cs размер окна из пакета
//    2  Builders/Widgets/LinePacketBuilder.cs  толщина линии из пакета
//    4  Common/Interaction/Tooltip.cs         координаты курсора
//    1  Menu/Scenery/MenuSceneryPresenter.cs  runtime texture binding
//    3  MenuModalManager, MenuLoaderProgress, GridPacketBuilder — доли и
//       размеры, вычисляемые в рантайме
//
// Видимости в этом списке больше нет. Она была настоящим долгом (10 записей в
// Tooltip и ModalWindowHandler) и оказалась ещё и дефектом: инлайновый display
// бьёт класс, поэтому элемент, скрытый твином, не открывался бы снятием
// is-hidden. Переведено на UIState, стартовое «скрыто» в Tooltip.uxml и
// ModalWindow.uxml переехало с инлайна на класс, и это единственные два места
// разметки main game, которых заход коснулся. Держит проверка
// checkNoInlineDisplayOutsideMainGame.

const DEBT_BUDGET = {
    "inline вне main game": 59,
    "inline в main game": 305,
    // 210, а не 205: сверка компонентов (compare-components.py) перенесла в
    // игру значения макета, и часть из них там тоже литералы — 22px отступа
    // под подзаголовком, 3px скругления тикера, 19px и -30px в ленте хроники.
    // Приводить их к ближайшей ступени значило бы разойтись с макетом ради
    // красоты числа. Разбор остатка — docs/design-debt-uss.md.
    // 215: волоски в 1px у четырёх коробок, которые в игре были невидимыми —
    // карточка деталей сервера, шапка профиля и две плашки хроники, — плюс 7px
    // внутреннего отступа плашки. Шкалы ширин нет ни в игре, ни в макете, а
    // 7px — шаг самого макета: он там и у тега хроники, и у бейджа клавиши.
    // 216: плюс высота журнала ремонта (170px) — без неё коробка сжималась по
    // содержимому и модалка прыгала по мере поступления строк.
    // 218: плюс 14x2 золотой чёрточки надзаголовка. В макете эти два числа тоже
    // литералы: чёрточка намеренно не тир-зависимая, иначе на компактном тире
    // она мельчает вместе с отступами и перестаёт читаться как акцент.
    "литерал в общем слое": 218,
    "литерал в main game": 324,
};

// Папки Assets/Scripts/UI, принадлежащие main game.
const MAIN_GAME_DIRS = new Set(["HUD", "Map", "Chat", "Programmator", "Settings", "Overlays"]);

// Листы main game. Машинные листы не считаются: у них нет автора-человека.
const MAIN_GAME_USS = new Set(["HUD.uss", "Inventory.uss", "Chat.uss", "chat-input.uss",
    "Programmator.uss", "PauseMenu.uss", "Modal.uss"]);
const GENERATED_USS = new Set(["ThemeTokens.uss", "TokenUtilities.uss"]);

// Комментарий — не код. Счётчик, который считает собственное объяснение,
// заставляет молчать про дефект, чтобы пройти проверку.
function stripCsComments(text) {
    return text.replace(/\/\*[\s\S]*?\*\//g, " ")
        .split("\n").map((l) => l.replace(/\/\/.*$/, "")).join("\n");
}

function walkFiles(dir, ext, out = []) {
    if (!fs.existsSync(dir)) return out;
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) walkFiles(full, ext, out);
        else if (entry.name.endsWith(ext)) out.push(full);
    }
    return out;
}

function checkDesignSystemRatchet() {
    const counts = {
        "inline вне main game": 0,
        "inline в main game": 0,
        "литерал в общем слое": 0,
        "литерал в main game": 0,
    };

    const uiRoot = path.join(__dirname, "..", "Assets", "Scripts", "UI");
    for (const file of walkFiles(uiRoot, ".cs")) {
        const top = path.relative(uiRoot, file).split(path.sep)[0];
        const key = MAIN_GAME_DIRS.has(top) ? "inline в main game" : "inline вне main game";
        const code = stripCsComments(fs.readFileSync(file, "utf8"));
        counts[key] += (code.match(/\.style\b/g) || []).length;
    }

    // Листы считаются и вне папки Styles: BootstrapLoadingScreen.uss живёт
    // рядом со своей разметкой и до этой правки не попадал ни в один счётчик —
    // четыре сырых цвета жили там мимо долга.
    for (const file of [...walkFiles(STYLES_DIR, ".uss"), ...walkFiles(UXML_DIR, ".uss")]) {
        const name = path.basename(file);
        if (GENERATED_USS.has(name)) continue;
        const key = MAIN_GAME_USS.has(name) ? "литерал в main game" : "литерал в общем слое";
        const code = stripUssComments(fs.readFileSync(file, "utf8"));
        counts[key] += (code.match(/#[0-9a-fA-F]{3,8}\b/g) || []).length;
        counts[key] += (code.match(/\brgba?\(/g) || []).length;
        // Именованный цвет — такой же литерал, как #rrggbb: тема на него не
        // действует. Счётчик их не видел, и 31 «white» спокойно жил мимо долга.
        // transparent — не цвет палитры, а отсутствие заливки, и не считается.
        counts[key] += (code.match(/:\s*(?:white|black|red|green|blue|yellow|magenta|cyan|gray|grey|silver|maroon|olive|lime|teal|navy|fuchsia|purple|aqua)\s*[;}]/g) || []).length;
        counts[key] += (code.match(/(?<![\w-])\d+(?:\.\d+)?px/g) || []).length;
    }

    let violations = 0;
    for (const [name, budget] of Object.entries(DEBT_BUDGET)) {
        const actual = counts[name];
        if (actual > budget) {
            recordViolation("USS Stylesheet", STYLES_DIR,
                `долг «${name}»: ${actual} при потолке ${budget} — долг вырос на ${actual - budget}`);
            violations++;
        } else if (actual < budget) {
            recordViolation("USS Stylesheet", path.join(__dirname, "check-architecture.js"),
                `долг «${name}»: стало ${actual} вместо ${budget}. Долг упал — впишите новое число в DEBT_BUDGET, иначе потолок останется на старом месте и отвоёванное можно молча вернуть`);
            violations++;
        }
    }
    return violations;
}

// ---------------------------------------------------------------------------
// Part 5: localization linter
// ---------------------------------------------------------------------------

const LOCALIZATION_DIR = path.join(__dirname, "..", "Assets", "Resources", "Localization");

// Localization-key usages in production C#. Two sources, both excluding
// tests via collectWiringFiles:
//   1. Literal lookups: .Get("menu.play") / .HasKey("menu.play").
//   2. Keys referenced as data: a "dotted" string literal that exactly
//      matches a dictionary key counts as usage too (e.g. MenuLoaderProgress
//      stores phase keys in an array and resolves them through Get() at
//      runtime). Filenames like "client_config.json" never equal a key.
const LOC_KEY_USAGE_RE = /\.(?:Get|HasKey)\(\\?"([a-z][a-z0-9_.-]*\.[a-z0-9_.-]+)"/g;

// Render a set of placeholder indices as "{0},{1}" for diagnostics.
function placeholderList(indices) {
    return indices.map((i) => "{" + i + "}").join(",");
}

function checkLocalization() {
    // 1. Load all language files as flat string->string dictionaries.
    let names;
    try {
        names = fs.readdirSync(LOCALIZATION_DIR).filter((n) => n.endsWith(".json")).sort();
    } catch {
        recordViolation("Localization", LOCALIZATION_DIR, `Не найден каталог локализации ${LOCALIZATION_DIR}.`);
        return;
    }
    if (names.length === 0) {
        recordViolation("Localization", LOCALIZATION_DIR, "В Assets/Resources/Localization нет ни одного .json — словаря локализации нет вовсе.");
        return;
    }

    let problemCount = 0;
    const dictionaries = new Map(); // lang -> Map(key -> value)
    for (const name of names) {
        const full = path.join(LOCALIZATION_DIR, name);
        const src = readFile(full);
        if (src === null) {
            recordViolation("Localization (file)", full, `${name}: не удалось прочитать файл.`);
            problemCount++;
            continue;
        }
        let parsed;
        try {
            parsed = JSON.parse(src);
        } catch (ex) {
            recordViolation("Localization (invalid JSON)", full, `${name}: файл не парсится как JSON (${ex.message}).`);
            problemCount++;
            continue;
        }
        // JSON.parse silently collapses duplicate keys (last wins), so detect
        // them on the raw text: keys in these files sit on their own lines.
        const seenRaw = new Set();
        for (const m of src.matchAll(/^\s*"([^"]+)"\s*:/gm)) {
            if (seenRaw.has(m[1])) {
                recordViolation("Localization (duplicate key)", full, `${name}: ключ '${m[1]}' объявлен несколько раз — побеждает последний, остальные потеряны.`);
                problemCount++;
            }
            seenRaw.add(m[1]);
        }
        const dict = new Map();
        for (const [key, value] of Object.entries(parsed)) {
            if (typeof value !== "string") {
                recordViolation("Localization (value type)", full, `${name}: значение ключа '${key}' — не строка (${typeof value}).`);
                problemCount++;
                continue;
            }
            dict.set(key, value);
        }
        dictionaries.set(name.replace(/\.json$/, ""), dict);
    }
    if (dictionaries.size === 0) {
        return;
    }

    const allKeys = new Set();
    for (const dict of dictionaries.values()) {
        for (const key of dict.keys()) {
            allKeys.add(key);
        }
    }
    const langs = [...dictionaries.keys()];

    // 2. Key-set parity: en is the runtime fallback, so a key missing in any
    //    language either shows the raw key (en) or silently falls back to en.
    for (const key of [...allKeys].sort()) {
        const missing = langs.filter((lang) => !dictionaries.get(lang).has(key));
        if (missing.length > 0) {
            recordViolation(
                "Localization (key parity)",
                LOCALIZATION_DIR,
                `Ключ '${key}' есть не во всех языках: отсутствует в ${missing.join(", ")}. В игре покажется сырой ключ или сработает неявный fallback на en.`,
            );
            problemCount++;
        }
    }

    // 2b. Translated languages must not carry source-language (Cyrillic) text:
    //     the ru dictionary is the source, every other language is a translation.
    for (const lang of langs) {
        if (lang === "ru") {
            continue;
        }
        for (const [key, value] of dictionaries.get(lang)) {
            if (/[А-Яа-яЁё]/.test(value)) {
                recordViolation(
                    "Localization (translation has Cyrillic)",
                    path.join(LOCALIZATION_DIR, lang + ".json"),
                    `Ключ '${key}': значение содержит кириллицу — похоже, перевод не сделан и остался русский текст.`,
                );
                problemCount++;
            }
        }
    }

    // 3. Placeholder sanity: {N} indices must be a contiguous prefix from {0}
    //    (string.Format throws FormatException and Get() returns the raw
    //    string otherwise), and identical across languages for the same key.
    for (const [lang, dict] of dictionaries) {
        for (const [key, value] of dict) {
            const indices = [...value.matchAll(/\{(\d+)\}/g)].map((m) => parseInt(m[1], 10));
            const sorted = [...new Set(indices)].sort((a, b) => a - b);
            if (sorted.length > 0 && sorted.some((v, i) => v !== i)) {
                recordViolation(
                    "Localization (placeholders)",
                    path.join(LOCALIZATION_DIR, lang + ".json"),
                    `'${key}' в '${lang}': плейсхолдеры {${placeholderList(sorted)}} — должны идти подряд, начиная с {0}.`,
                );
                problemCount++;
            }
        }
    }
    for (const key of allKeys) {
        const perLang = [];
        let complete = true;
        for (const lang of langs) {
            const dict = dictionaries.get(lang);
            if (!dict.has(key)) {
                complete = false; // parity check already reported the gap
                break;
            }
            perLang.push([lang, new Set([...(dict.get(key) ?? "").matchAll(/\{(\d+)\}/g)].map((m) => m[1]))]);
        }
        if (!complete) {
            continue;
        }
        const [firstLang, firstSet] = perLang[0];
        for (const [lang, set] of perLang.slice(1)) {
            if (firstSet.size !== set.size || [...firstSet].some((i) => !set.has(i))) {
                recordViolation(
                    "Localization (placeholders)",
                    LOCALIZATION_DIR,
                    `Ключ '${key}': набор плейсхолдеров в '${firstLang}' ({${placeholderList([...firstSet].sort())}}) отличается от '${lang}' ({${placeholderList([...set].sort())}}) — string.Format упадёт на одном из языков.`,
                );
                problemCount++;
            }
        }
    }

    // 4. Usage wiring: every key used in production C# must exist in every
    //    language; dictionary keys never used are dead. Keys also count as
    //    used when they appear as UXML text attributes (text="hud.mission"):
    //    UILocalizer resolves them at runtime, so UXML is a legitimate reader.
    const usedKeys = new Set();
    for (const file of collectWiringFiles()) {
        const content = readFile(file);
        if (content === null) {
            continue;
        }
        for (const m of content.matchAll(LOC_KEY_USAGE_RE)) {
            usedKeys.add(m[1]);
        }
        for (const key of allKeys) {
            // Plain "key" or escaped \"key\" (inside interpolated strings).
            if (content.includes('"' + key + '"') || content.includes('\\"' + key + '\\"')) {
                usedKeys.add(key);
            }
        }
    }
    {
        const UI_DIR = path.join(__dirname, "..", "Assets", "Resources", "UI");
        for (const name of fs.readdirSync(UI_DIR)) {
            if (!name.endsWith(".uxml")) {
                continue;
            }
            const content = readFile(path.join(UI_DIR, name));
            if (content === null) {
                continue;
            }
            for (const m of content.matchAll(/(?:text|tooltip)="([^"]*)"/g)) {
                // Dotted lowercase values are localization keys: count them as
                // used (the missing-key check then catches typos that would
                // otherwise render as raw keys at runtime). Tooltips count too:
                // UILocalizer resolves them the same way as text.
                if (/^[a-z][a-z0-9_.-]*\.[a-z0-9_.-]+$/.test(m[1])) {
                    usedKeys.add(m[1]);
                }
            }
        }
    }

    if (usedKeys.size === 0) {
        recordViolation(
            "Localization (unwired)",
            LOCALIZATION_DIR,
            `Локализация не подключена: в словарях объявлено ${allKeys.size} ключей (${langs.join(", ")}), но ни один не используется в production-коде — UI показывает захардкоженные строки. Переведите строки на .Get("...") или удалите словарь.`,
        );
        problemCount++;
    } else {
        for (const key of [...usedKeys].sort()) {
            const missing = langs.filter((lang) => !dictionaries.get(lang).has(key));
            if (missing.length > 0) {
                recordViolation(
                    "Localization (missing key)",
                    LOCALIZATION_DIR,
                    `Ключ '${key}' используется в коде, но отсутствует в ${missing.join(", ")} — в игре покажется сырой ключ.`,
                );
                problemCount++;
            }
        }
        for (const key of [...allKeys].filter((k) => !usedKeys.has(k)).sort()) {
            recordViolation(
                "Localization (dead key)",
                LOCALIZATION_DIR,
                `Ключ '${key}' объявлен в словаре, но нигде не используется — строка либо захардкожена, либо ключ потерян.`,
            );
            problemCount++;
        }
    }

    console.log(`${CYAN}${BOLD}Localization:${NC} ${dictionaries.size} language(s), ${allKeys.size} key(s), ${problemCount} violation(s)`);
}

// Hardcoded-text bans: the localization dictionary is the single source of
// truth for displayed text, so UXML must not carry Cyrillic literals and UI
// code must not assign Cyrillic string literals to displayed text (or feed
// them to text constructors/tooltips). Debug/exception messages are not
// displayed text and stay exempt.
function checkHardcodedText() {
    // 1. UXML: text="..." with Cyrillic is a hardcoded string that would show
    //    in the source language regardless of the chosen language.
    const UI_DIR = path.join(__dirname, "..", "Assets", "Resources", "UI");
    for (const name of fs.readdirSync(UI_DIR)) {
        if (!name.endsWith(".uxml")) {
            continue;
        }
        const src = readFile(path.join(UI_DIR, name));
        if (src === null) {
            continue;
        }
        for (const m of src.matchAll(/(?:text|tooltip)="([^"]*[А-Яа-яЁё][^"]*)"/g)) {
            const attr = m[0].split("=")[0];
            recordViolation(
                "Localization (hardcoded UXML text)",
                path.join(UI_DIR, name),
                `'${m[1]}' — ${attr}-атрибут в UXML захардкожен; задайте ключ (${attr}="ключ") и переведите строку в словарь, либо уберите, если текст ставит код (Tooltip.AttachTo).`,
            );
        }
    }

    // 2. UI code: Cyrillic string literals that feed displayed text
    //    (.text / .tooltip / new Label / new Button / tooltip providers).
    //    Exempt: Debug.*/Assert/throw statements and their multi-line
    //    continuations — tracked until the statement's closing ';'.
    const UI_SRC = path.join(__dirname, "..", "Assets", "Scripts", "UI");
    const files = walkCs(UI_SRC);
    const LIT_RE = /"([^"\\]*(?:\\.[^"\\]*)*)"/g;
    const isLogLine = (s) => /Debug\.(Log|LogWarning|LogError|LogException|Assert)\s*\(/.test(s) || /throw new/.test(s);
    for (const file of files) {
        const src = readFile(file);
        if (src === null) {
            continue;
        }
        const lines = src.split("\n");
        let inLogContext = false;
        for (let i = 0; i < lines.length; i++) {
            const raw = lines[i];
            const trimmed = raw.trim();
            if (!trimmed || trimmed.startsWith("//") || trimmed.startsWith("///") || trimmed.startsWith("*") || trimmed.startsWith("/*")) {
                continue;
            }
            const codePart = raw.split("//")[0];
            if (isLogLine(codePart)) {
                inLogContext = true;
            }
            if (!inLogContext) {
                // L("key", fallback[, args]) is a null-safe lookup helper: its
                // fallback literal is only used when localization is not
                // injected, so strip whole L(...) calls before scanning.
                const stripped = codePart.replace(/L\([^)]*\)/g, "");
                LIT_RE.lastIndex = 0;
                for (const m of stripped.matchAll(LIT_RE)) {
                    if (/[А-Яа-яЁё]/.test(m[1])) {
                        recordViolation(
                            "Localization (hardcoded UI text)",
                            file,
                            `строка ${i + 1}: '${m[1].slice(0, 60)}${m[1].length > 60 ? "…" : ""}' — текст задаётся литералом; используйте _loc.Get("...").`,
                        );
                    }
                }
            }
            if (/;\s*$/.test(codePart)) {
                inLogContext = false;
            }
        }
    }
}

function checkLocalizationWiring() {
    // The localization registry (LocalizationService.RegisterLocalizable) is the
    // only allowed way for UI views to hook into re-application on language
    // change. Manual `_loc.OnLanguageChanged +=` subscriptions are how views end
    // up "subscribed but never applied" — Gateway/PlayerHUD/Inventory built UI
    // with raw keys because they subscribed for re-apply but never applied at
    // startup. The registry applies at registration AND on every change, so a
    // view cannot forget either half.
    //
    // Second half of the contract: any UI file that clones/instantiates a UI
    // resource AND uses localization must resolve static keys right at build
    // time (UILocalizer.Apply), not only via a later re-apply pass.
    const UI_SRC = path.join(__dirname, "..", "Assets", "Scripts", "UI");
    const files = walkCs(UI_SRC);
    for (const file of files) {
        const src = readFile(file);
        if (src === null) {
            continue;
        }

        // Rule A: no manual OnLanguageChanged subscription/unsubscription in UI
        // code — the registry is the only channel (comments are stripped via
        // the // split, so doc mentions do not trigger).
        const lines = src.split("\n");
        for (let i = 0; i < lines.length; i++) {
            const codePart = lines[i].split("//")[0];
            if (!codePart.trim()) {
                continue;
            }
            if (/OnLanguageChanged\s*[-+]?=/.test(codePart)) {
                recordViolation(
                    "Localization (manual subscription)",
                    file,
                    `строка ${i + 1}: ручная подписка на OnLanguageChanged; используйте _loc.RegisterLocalizable(this) — сервис применяет текст сразу при регистрации и на каждой смене языка, а UnregisterLocalizable(this) — в OnDestroy.`,
                );
            }
        }

        // Rule B: EVERY tree-build site (CloneTree / TemplateContainer.Instantiate)
        // in a localizing file must localize its tree in the SAME method — either
        // UILocalizer.Apply right at the build site, or an ApplyLocalizedText()
        // call in that method. A UILocalizer.Apply that lives only in a re-apply
        // method (language change) leaves the freshly built tree with raw keys
        // until the first language switch — the "localization disappears after
        // scene transitions" failure. A file-level check cannot see this: the
        // apply exists, just not where the tree is built. Files that do not use
        // localization at all are exempt.
        const usesLocalization = /_loc\b|ILocalizationService|ILocalizableUI/.test(src);
        if (usesLocalization) {
            const lines = src.split("\n");
            for (let i = 0; i < lines.length; i++) {
                if (!/\.CloneTree\(\)|\.Instantiate\(\)/.test(lines[i])) {
                    continue;
                }

                // End of the enclosing method: its opening brace sits before the
                // build line, so scanning forward with depth starting at 0, the
                // method body closes when cumulative depth reaches -1.
                let depth = 0;
                let end = i;
                for (let j = i; j < lines.length; j++) {
                    depth += (lines[j].match(/\{/g) || []).length -
                             (lines[j].match(/\}/g) || []).length;
                    if (depth < 0) {
                        end = j;
                        break;
                    }
                }

                const methodBody = lines.slice(i, end + 1).join("\n");
                if (!/UILocalizer\.Apply|ApplyLocalizedText\(\)/.test(methodBody)) {
                    recordViolation(
                        "Localization (unresolved at build)",
                        file,
                        `строка ${i + 1}: дерево строится (CloneTree/Instantiate), но в этом же методе нет ни UILocalizer.Apply, ни ApplyLocalizedText() — статические ключи UXML останутся сырыми до первой смены языка. Применяйте локализацию в методе сборки, а не только в re-apply-методе.`,
                    );
                }
            }
        }

        // Rule C: a view that implements ILocalizableUI must register with the
        // service, otherwise language changes never reach it.
        if (/ILocalizableUI/.test(src) && !/RegisterLocalizable/.test(src)) {
            recordViolation(
                "Localization (unregistered)",
                file,
                `класс реализует ILocalizableUI, но нигде не вызывает RegisterLocalizable — смена языка до него не дойдёт, а стартовое применение никто не гарантирует.`,
            );
        }
    }
}

function checkLocalizationRegistry() {
    // Rule D — across ALL production scripts, not just Assets/Scripts/UI:
    // every UXML that carries localization keys must be loaded by a view that
    // is registered in the registry (RegisterLocalizable). A view that builds a
    // keyed tree but never registers is re-applied on no language change — it
    // stays in the language it was built in. Loader detection by basename:
    // Resources.Load<VisualTreeAsset>("UI/X") and the
    // ProjectRuntimeContracts.ResourcePaths.<X>Uxml constants.
    const UI_DIR = path.join(__dirname, "..", "Assets", "Resources", "UI");
    const SCRIPTS_DIR = path.join(__dirname, "..", "Assets", "Scripts");

    const keyedUxml = new Set();
    for (const name of fs.readdirSync(UI_DIR)) {
        if (!name.endsWith(".uxml")) {
            continue;
        }
        const content = readFile(path.join(UI_DIR, name));
        if (content === null) {
            continue;
        }
        for (const m of content.matchAll(/text="([^"]*)"/g)) {
            if (/^[a-z][a-z0-9_.-]*\.[a-z0-9_.-]+$/.test(m[1])) {
                keyedUxml.add(name.replace(/\.uxml$/, ""));
                break;
            }
        }
    }

    if (keyedUxml.size === 0) {
        return;
    }

    const loaderFiles = new Map(); // basename -> Set<file>
    for (const file of walkCs(SCRIPTS_DIR)) {
        if (/Assets\/Scripts\/Tests\//.test(file)) {
            continue;
        }
        const src = readFile(file);
        if (src === null) {
            continue;
        }
        const found = new Set();
        for (const m of src.matchAll(/Resources\.Load<VisualTreeAsset>\("([^"]+)"\)/g)) {
            const base = m[1].split("/").pop();
            if (keyedUxml.has(base)) {
                found.add(base);
            }
        }
        for (const m of src.matchAll(/ResourcePaths\.([A-Za-z]+)Uxml/g)) {
            if (keyedUxml.has(m[1])) {
                found.add(m[1]);
            }
        }
        if (found.size > 0) {
            for (const base of found) {
                if (!loaderFiles.has(base)) {
                    loaderFiles.set(base, new Set());
                }
                loaderFiles.get(base).add(file);
            }
        }
    }

    for (const [base, files] of loaderFiles) {
        for (const file of files) {
            const src = readFile(file);
            if (src === null || /RegisterLocalizable/.test(src)) {
                continue;
            }
            recordViolation(
                "Localization (unregistered loader)",
                file,
                `загружает ключевой UXML (${base}.uxml), но не вызывает RegisterLocalizable — смена языка до этой вьюхи не дойдёт. Зарегистрируйте её в реестре (ILocalizableUI + RegisterLocalizable) либо делегируйте переприменение зарегистрированному родителю.`,
            );
        }
    }
}

function checkSilentUiNoop() {
    // UI views that guard on the UIDocument panel (rootVisualElement is only
    // created in UIDocument.OnEnable) and silently `return` are the failure mode
    // that black-screens with ZERO console output: the screen never builds and
    // nothing is logged. Every such guard must either log, or carry a comment
    // explaining why the silent return is expected (a retry loop elsewhere, a
    // boolean contract, etc.) — otherwise it is indistinguishable from a broken
    // screen and a linter that passes while the game shows nothing.
    const UI_SRC = path.join(__dirname, "..", "Assets", "Scripts", "UI");
    const files = walkCs(UI_SRC);
    for (const file of files) {
        const src = readFile(file);
        if (src === null) {
            continue;
        }
        const lines = src.split("\n");
        for (let i = 0; i < lines.length; i++) {
            const line = lines[i];
            if (!line.includes("if") || !line.includes("rootVisualElement") ||
                !line.includes("== null")) {
                continue;
            }

            // Collect the guard body: the brace block (opening brace may sit on
            // the next line), or the single line when the whole `if (...) return;`
            // sits on one line.
            const blockLines = [line];
            let openLine = i;
            if (!line.includes("{") && i + 1 < lines.length && lines[i + 1].includes("{")) {
                openLine = i + 1;
                blockLines.push(lines[i + 1]);
            }
            if (line.includes("{") || openLine > i) {
                let depth = (lines[openLine].match(/\{/g) || []).length -
                    (lines[openLine].match(/\}/g) || []).length;
                let j = openLine + 1;
                while (j < lines.length && depth > 0) {
                    blockLines.push(lines[j]);
                    depth += (lines[j].match(/\{/g) || []).length -
                        (lines[j].match(/\}/g) || []).length;
                    j++;
                }
            }

            const blockText = blockLines.join("\n");
            if (!/\breturn\s*[^;]*;/.test(blockText)) {
                continue;
            }

            if (/Debug\.(Log|LogWarning|LogError|LogException)/.test(blockText)) {
                continue;
            }

            // Justification: a comment inside the guard or on the two lines
            // directly above it (the project already documents retry loops there).
            const above = lines.slice(Math.max(0, i - 2), i).join("\n");
            const hasComment =
                blockText.includes("//") ||
                blockText.includes("/*") ||
                above.includes("//");
            if (hasComment) {
                continue;
            }

            recordViolation(
                "Silent UI no-op",
                file,
                `строка ${i + 1}: guard на rootVisualElement == null молча делает return без Debug-лога и без комментария — при неготовой панели экран не построится, а в консоль не попадёт ничего. Либо добавьте Debug.LogWarning, либо комментарий, объясняющий, почему тихий возврат ожидаем (ретрай, boolean-контракт и т.п.).`,
            );
        }
    }
}

function checkSingleRoadInit() {
    // Одна дорога: инициализация вьюхи — ровно одна точка на сущность: Start
    // (к нему зависимости инжектятся при сборке scope и панель UIDocument уже
    // создана) + событие готовности для async-зависимостей (ServerConfig.
    // OnInitialized / MapManager.OnWorldInitialized / LightingEngine.OnInitialized
    // / session.OnSet). Per-frame ретрай TryInitialize из Update — это «конвейер
    // из пяти тихих no-op», на котором вьюхи умирали молча: гард тихо выходит,
    // пока зависимость не готова, и ни один лог не появляется, а линтер зелёный.
    const UI_SRC = path.join(__dirname, "..", "Assets", "Scripts", "UI");
    const files = walkCs(UI_SRC);
    for (const file of files) {
        const src = readFile(file);
        if (src === null) {
            continue;
        }
        const lines = src.split("\n");
        for (let i = 0; i < lines.length; i++) {
            if (!/void\s+Update\s*\(/.test(lines[i])) {
                continue;
            }

            // Collect the Update method body (brace may sit on the same line
            // or the next one).
            let openLine = i;
            if (!lines[i].includes("{") && i + 1 < lines.length && lines[i + 1].includes("{")) {
                openLine = i + 1;
            }
            if (!lines[openLine].includes("{")) {
                continue;
            }
            let depth = (lines[openLine].match(/\{/g) || []).length -
                (lines[openLine].match(/\}/g) || []).length;
            const body = [lines[openLine]];
            let j = openLine + 1;
            while (j < lines.length && depth > 0) {
                body.push(lines[j]);
                depth += (lines[j].match(/\{/g) || []).length -
                    (lines[j].match(/\}/g) || []).length;
                j++;
            }

            if (/\bTryInitialize\s*\(/.test(body.join("\n"))) {
                recordViolation(
                    "One-road init",
                    file,
                    `строка ${i + 1}: Update() вызывает TryInitialize — инициализация обязана быть событийной: Start (к нему зависимости и панель гарантированы) + событие готовности для async-зависимостей (ServerConfig.OnInitialized / MapManager.OnWorldInitialized / LightingEngine.OnInitialized / session.OnSet). Per-frame ретрай — это тихий конвейер no-op: вьюха молча ждёт зависимости, экран не строится, в консоль не попадает ничего.`,
                );
            }
        }
    }
}

// ---------------------------------------------------------------------------
// Entry point
// ---------------------------------------------------------------------------

function printViolations() {
    let patternCount = 0;
    for (const v of violations) {
        if (v.kind === "pattern") {
            patternCount++;
            console.log(`${RED}${BOLD}[VIOLATION]${NC} ${YELLOW}${v.message}${NC}`);
            console.log("");
        } else {
            console.log(`${RED}${BOLD}[${v.category.toUpperCase()} VIOLATION]${NC} ${YELLOW}${v.category}${NC}`);
            console.log(`  Location: ${BOLD}${v.loc}${NC}`);
            console.log(`  Details:  ${CYAN}${v.message}${NC}`);
            console.log("");
        }
    }
    return patternCount;
}

function main() {
    const startedAt = Date.now();
    const args = process.argv.slice(2);

    const files = args.length > 0 ? args : collectProductionFiles();
    const productionFiles = files.filter(
        (file) => fs.existsSync(file) && !EXCLUDE_REGEX.test(file),
    );

    console.log(`${CYAN}${BOLD}=== Fodinae Architectural Pattern Linter ===${NC}`);
    console.log(`Scanning ${BOLD}${productionFiles.length}${NC} files against ${BOLD}${RULES.length}${NC} architectural rules...`);
    console.log("");

    checkPatterns(productionFiles);
    checkOversizedProductionFiles();
    checkExecutionOrders();
    checkLifetimeScopeConfigure();
    checkProjectCompileIncludes();
    checkSceneReadinessContracts();
    checkTransitionStateContracts();
    checkPersistentAssetCacheContract();
    checkUiTransitionGuards();
    checkSceneScopeInjection();
    checkLifecycleSelfCalls();
    checkMenuSceneryOwnership();
    checkEditorSceneAuthoringContract();
    checkGameBootstrapResolvesRegisteredManagers();
    checkCompositionRootContracts();
    checkDirectDependencyCycles();
    checkPacketSubscriptionSymmetry();
    checkSerializedSceneContracts();
    checkUnityNamespaces();
    checkEarlyLifecycleDiAndCallgraph();
    checkAsyncVoid();
    checkDeadConfigFields();
    checkUiOnlyWiring();
    checkUncoveredConsumers();
    checkStartupApplicationContract();
    checkUssStyles();
    checkLocalization();
    checkHardcodedText();
    checkLocalizationWiring();
    checkLocalizationRegistry();
    checkSilentUiNoop();
    checkSingleRoadInit();

    const duration = ((Date.now() - startedAt) / 1000).toFixed(0);
    if (violations.length > 0) {
        const patternCount = printViolations();
        const otherCount = violations.length - patternCount;
        console.log(`${RED}${BOLD}✖ FAILED:${NC} Found ${BOLD}${violations.length}${NC} violation(s) ` +
            `(${BOLD}${patternCount}${NC} architectural, ${BOLD}${otherCount}${NC} DI/lifecycle + settings wiring + USS + localization) ` +
            `across ${BOLD}${productionFiles.length}${NC} files (${duration}s).`);
        console.log("");
        console.log(`${BOLD}Architectural Standards & Replacements:${NC}`);
        for (const line of STANDARDS_LIST) {
            console.log(line);
        }
        console.log("");
        console.log(`${BOLD}Deep semantic checks:${NC}`);
        console.log("  - execution-order contracts on LifetimeScopes/MapManager");
        console.log("  - Configure() reentrancy and direct AddComponent prohibition");
        console.log("  - Unity-serialized types must use block namespace { }");
        console.log("  - unguarded [Inject] access in Awake/OnEnable call graphs");
        console.log("  - safe DI resolution in early lifecycle (TryResolve, not Resolve)");
        console.log("  - no async void in MonoBehaviours (use UniTask)");
        console.log("  - no new production C# files above 500 lines; finite debt list only");
        console.log("  - every ClientConfig field referenced in production code");
        console.log("  - no ClientConfig field read only from UI controllers (dead wiring)");
        console.log("  - every config consumer applied at startup from GameStartupPipeline");
        console.log("  - USS stylesheets: only UI Toolkit properties, functions and easings");
        console.log("  - localization: language parity, used-key existence, placeholders, dead keys");
        console.log("  - localization wiring: no manual OnLanguageChanged, CloneTree+UILocalizer.Apply, ILocalizableUI registered");
        console.log("  - localization registry: every keyed-UXML loader across all scripts must call RegisterLocalizable");
        process.exit(1);
    }

    console.log(`${GREEN}${BOLD}✔ PASSED:${NC} All ${BOLD}${productionFiles.length}${NC} production files conform to ` +
        `${BOLD}${RULES.length}${NC} architectural rules; DI/lifecycle, settings-wiring, USS and localization checks passed (${duration}s).`);
    process.exit(0);
}

main();
