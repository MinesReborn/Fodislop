#nullable enable

using System;
using System.IO;
using System.Linq;
using Kern.Core;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Kern.Editor;

public static class BuildScript
{
    private const string ProductName = "Kern";
    private const string DevArg = "-kernDev";

    // Идентификатор приложения, а не подпись проекта: Unity подставляет
    // com.DefaultCompany.<productName>, пока поле пусто. Общий для Android и iOS.
    private const string ApplicationIdentifier = "com.darkar25.kern";

    private static string[] EnabledScenes =>
        EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

    [MenuItem("Kern/Build/macOS (Apple Silicon)")]
    public static void BuildMacOS() =>
        BuildPlayerArtifact(BuildTarget.StandaloneOSX, $"Build/macOS/{ProductName}.app", isApple: true);

    [MenuItem("Kern/Build/macOS (Apple Silicon, development)")]
    public static void BuildMacOSDevelopment() =>
        BuildPlayerArtifact(
            BuildTarget.StandaloneOSX,
            $"Build/macOS-Development/{ProductName}.app",
            isApple: true,
            development: true);

    // Сборка для замеров: профайлер подключён, маркеры и счётчики живые.
    // AllowDebugging не ставится — отладочный код исказил бы сами замеры.
    // Десктоп собирается на Mono (см. BuildPlayerArtifact); на IL2CPP-
    // платформах берётся Release, а не Debug, по той же причине.
    [MenuItem("Kern/Build/macOS (Apple Silicon, профилирование)")]
    public static void BuildMacOSProfiling() =>
        BuildPlayerArtifact(BuildTarget.StandaloneOSX, $"Build/macOS-Profiling/{ProductName}.app", isApple: true, profiling: true);

    [MenuItem("Kern/Build/Windows 64")]
    public static void BuildWindows() =>
        BuildPlayerArtifact(BuildTarget.StandaloneWindows64, $"Build/Windows/{ProductName}.exe");

    [MenuItem("Kern/Build/Linux 64")]
    public static void BuildLinux() =>
        BuildPlayerArtifact(BuildTarget.StandaloneLinux64, $"Build/Linux/{ProductName}");

    [MenuItem("Kern/Build/Android APK")]
    public static void BuildAndroid() =>
        BuildPlayerArtifact(BuildTarget.Android, $"Build/Android/{ProductName}.apk");

    // Магазинный формат: ABI-сплит и подпись Play делают из AAB, а не из APK.
    [MenuItem("Kern/Build/Android AAB")]
    public static void BuildAndroidBundle() =>
        BuildPlayerArtifact(BuildTarget.Android, $"Build/Android/{ProductName}.aab", appBundle: true);

    [MenuItem("Kern/Build/iOS Xcode Project")]
    public static void BuildIOS() =>
        BuildPlayerArtifact(BuildTarget.iOS, "Build/iOS");

    private static void BuildPlayerArtifact(
        BuildTarget target,
        string relativeOutput,
        bool isApple = false,
        bool profiling = false,
        bool development = false,
        bool appBundle = false)
    {
        BuildSceneOrder.Validate();

        var scenes = EnabledScenes;
        if (scenes.Length == 0)
        {
            Fail("No enabled scenes in EditorBuildSettings — nothing to build.");
            return;
        }

        if (!EnsureActiveBuildTarget(target))
        {
            return;
        }

        // Формат артефакта — глобальный флаг редактора, а не поле BuildPlayerOptions:
        // оставленный включённым, он превратил бы следующую APK-сборку в AAB.
        EditorUserBuildSettings.buildAppBundle = appBundle;

        if (target == BuildTarget.Android)
        {
            ConfigureAndroidPlayer();
        }
        else if (target == BuildTarget.iOS)
        {
            ConfigureIOSPlayer();
        }

        string output = Path.GetFullPath(relativeOutput);
        string outputDirectory = Path.GetDirectoryName(output)
            ?? throw new InvalidOperationException($"Build output has no parent directory: {output}");

        CleanOutput(output, outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        if (isApple)
        {
            TrySetAppleSiliconArchitecture();
        }

        bool commandLineDevelopment = Environment.GetCommandLineArgs().Contains(DevArg);
        bool isDevelopment = development || commandLineDevelopment;

        UnityEditor.Build.NamedBuildTarget namedTarget =
            UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(
                BuildPipeline.GetBuildTargetGroup(target));

        // Десктоп собирается на Mono: IL2CPP переводил весь C# в C++ и
        // гнал его через clang и LTO-линковку — десятки минут на сборку
        // ради выигрыша в скрипты, которые занимают 1–2 мс кадра.
        // iOS без IL2CPP не собирается, Android для магазина требует его
        // же, поэтому мобильные платформы остаются на IL2CPP.
        bool desktop = target is BuildTarget.StandaloneOSX
            or BuildTarget.StandaloneWindows64
            or BuildTarget.StandaloneLinux64;
        PlayerSettings.SetScriptingBackend(
            namedTarget,
            desktop ? ScriptingImplementation.Mono2x : ScriptingImplementation.IL2CPP);

        // Master компилируется в разы дольше и не даёт ничего отладке.
        // Раньше он ставился и для development-сборок тоже.
        PlayerSettings.SetIl2CppCompilerConfiguration(
            namedTarget,
            profiling ? Il2CppCompilerConfiguration.Release
            : isDevelopment ? Il2CppCompilerConfiguration.Debug
            : Il2CppCompilerConfiguration.Master);
        // Development builds are for iteration: emit less IL2CPP code to shorten
        // native code generation. Shipping builds keep the runtime-speed setting.
        PlayerSettings.SetIl2CppCodeGeneration(
            namedTarget,
            isDevelopment
                ? UnityEditor.Build.Il2CppCodeGeneration.OptimizeSize
                : UnityEditor.Build.Il2CppCodeGeneration.OptimizeSpeed);

        // Вариант управляемого кода задаётся явно: отладочные органы
        // управления и проверки закрыты #if UNITY_ENABLE_CHECKS, а этот символ
        // определяет только Checked (и Debug поверх него). Без явной записи
        // остаётся вариант из Player Settings, и development-сборка выходила
        // без отладочной части меню паузы. Профилировочной нужен Instrumented
        // (ENABLE_PROFILER) без проверок, которые исказили бы замеры.
        PlayerSettings.SetManagedCodeVariant(
            namedTarget,
            profiling ? ManagedCodeVariant.Instrumented
            : isDevelopment ? ManagedCodeVariant.Checked
            : ManagedCodeVariant.Release);

        // Minimal, а не Medium/High: в проекте VContainer, а он резолвит
        // типы рефлексией — агрессивный стриппинг вырезает то, на что
        // нет статических ссылок, и DI падает только в билде.
        PlayerSettings.SetManagedStrippingLevel(namedTarget, ManagedStrippingLevel.Minimal);

        // В редакторе useHDRDisplay следует выбору разработчика (HDROutput),
        // а игрок стартует в режиме по умолчанию из контракта.
        PlayerSettings.useHDRDisplay = ClientConfigurationDefaults.DefaultHDREnabled;

        BuildOptions buildOptions = BuildOptions.None;
        if (profiling)
        {
            buildOptions = BuildOptions.Development | BuildOptions.ConnectWithProfiler;
        }
        else if (development)
        {
            buildOptions = BuildOptions.Development;
        }
        else if (commandLineDevelopment)
        {
            // Оставляем managed debugging, но не запускаем прямую попытку
            // автоподключения профайлера: если Editor закрыт, Android Player
            // ждёт его до 10 секунд перед продолжением старта.
            buildOptions = BuildOptions.Development | BuildOptions.AllowDebugging;
        }

        var playerOptions = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = output,
            target = target,
            options = buildOptions,
        };

        Log($"Building {target} -> {output} (development={isDevelopment}, profiling={profiling}, scenes={scenes.Length})");
        BuildSummary summary = BuildPipeline.BuildPlayer(playerOptions).summary;
        Log($"Result={summary.result} size={summary.totalSize}B " +
            $"time={summary.totalTime} warnings={summary.totalWarnings} errors={summary.totalErrors}");

        if (summary.result != BuildResult.Succeeded)
        {
            Fail($"Build failed: {summary.result} ({summary.totalErrors} errors).");
            return;
        }

        Log($"Build succeeded: {output}");
        Log($"Версия {PlayerSettings.bundleVersion}{(isDevelopment ? " (development)" : string.Empty)}");
        Log($"Запуск: {LaunchHint(target, output)}");
    }

    private static string LaunchHint(BuildTarget target, string output) => target switch
    {
        BuildTarget.StandaloneOSX => $"open \"{output}\"",
        BuildTarget.StandaloneWindows64 => $"\"{output}\"",
        BuildTarget.StandaloneLinux64 => $"\"{output}\"",
        BuildTarget.Android => Path.GetExtension(output) == ".aab"
            ? $"bundletool build-apks --bundle=\"{output}\" --output=\"{Path.ChangeExtension(output, ".apks")}\""
            : $"adb install -r \"{output}\"",
        BuildTarget.iOS => $"open \"{output}\"",
        _ => output,
    };

    // Настройки Android в редакторе — это состояние GUI, а не часть проекта:
    // ABI, версии SDK и подпись остаются такими, какими их выставили руками,
    // и тихо уезжают в артефакт. Сборка выставляет их сама, чтобы батч-запуск
    // и пункт меню давали один и тот же результат.
    private static void ConfigureAndroidPlayer()
    {
        PlayerSettings.SetApplicationIdentifier(
            UnityEditor.Build.NamedBuildTarget.Android,
            ApplicationIdentifier);

        // Только ARM64: он обязателен для магазина, а 32-битные библиотеки
        // FMOD удвоил бы размер ради устройств без ARM64.
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel36;

        // Без compute-шейдеров движок не работает: LightingShaderValidator
        // бросает NotSupportedException, и на GLES 3.0 игра падает на старте.
        // Список API и требование установки поэтому совпадают: Vulkan, а не
        // автоматический набор, где вторым шёл GLES.
        PlayerSettings.SetGraphicsAPIs(
            BuildTarget.Android,
            new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan });

        // Транспорт — сырые TCP-сокеты (System.Net.Sockets), из кода Unity
        // выводит INTERNET только для UnityEngine.Networking: без явного флага
        // на устройстве сокет получает EACCES вместо отказа подключения.
        PlayerSettings.Android.forceInternetPermission = true;

        ConfigureLandscapeOrientation();

        Log($"Android: id={ApplicationIdentifier}, " +
            $"arch={PlayerSettings.Android.targetArchitectures}, " +
            $"minSdk={PlayerSettings.Android.minSdkVersion}, " +
            $"targetSdk={PlayerSettings.Android.targetSdkVersion}, " +
            $"graphicsApi={string.Join(", ", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android))}, " +
            $"orientation={PlayerSettings.defaultInterfaceOrientation}, " +
            $"appBundle={EditorUserBuildSettings.buildAppBundle}");
    }

    // Как и для Android: всё, что iOS-сборка берёт из Player Settings,
    // выставляется здесь, а не остаётся состоянием GUI.
    private static void ConfigureIOSPlayer()
    {
        PlayerSettings.SetApplicationIdentifier(
            UnityEditor.Build.NamedBuildTarget.iOS,
            ApplicationIdentifier);

        // Compute-шейдеры света требуют Metal; GLES на iOS давно не поддерживается.
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.iOS, false);
        PlayerSettings.SetGraphicsAPIs(
            BuildTarget.iOS,
            new[] { UnityEngine.Rendering.GraphicsDeviceType.Metal });

        PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
        PlayerSettings.iOS.targetOSVersionString = "16.0";
        PlayerSettings.iOS.requiresFullScreen = true;
        PlayerSettings.iOS.hideHomeButton = false;

        // Кнопки HUD стоят у краёв экрана: без отложенных системных жестов
        // первый свайп у края уходит в Пункт управления или домой, а не в игру.
        PlayerSettings.iOS.deferSystemGesturesMode = UnityEngine.iOS.SystemGestureDeferMode.All;

        ConfigureLandscapeOrientation();

        Log($"iOS: id={ApplicationIdentifier}, " +
            $"device={PlayerSettings.iOS.targetDevice}, " +
            $"minOS={PlayerSettings.iOS.targetOSVersionString}, " +
            $"graphicsApi={string.Join(", ", PlayerSettings.GetGraphicsAPIs(BuildTarget.iOS))}, " +
            $"orientation={PlayerSettings.defaultInterfaceOrientation}");
    }

    // Игра только альбомная, но в обе стороны: телефон держат и вырезом
    // влево, и вправо, а раскладушка при раскрытии может оказаться любой
    // стороной вверх. Портрет не поддерживается.
    private static void ConfigureLandscapeOrientation()
    {
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
    }

    private static bool EnsureActiveBuildTarget(BuildTarget target)
    {
        if (EditorUserBuildSettings.activeBuildTarget == target)
        {
            return true;
        }

        var group = BuildPipeline.GetBuildTargetGroup(target);
        Log($"Переключаю платформу: {EditorUserBuildSettings.activeBuildTarget} → {target} (это перезапустит импорт ассетов).");

        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
        {
            Fail($"Не удалось переключиться на {target}. Модуль платформы установлен?");
            return false;
        }

        return true;
    }

    private static void CleanOutput(string output, string outputDirectory)
    {
        try
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
            else if (File.Exists(output))
            {
                File.Delete(output);
                string dataDirectory = Path.Combine(
                    outputDirectory,
                    $"{Path.GetFileNameWithoutExtension(output)}_Data");
                if (Directory.Exists(dataDirectory))
                {
                    Directory.Delete(dataDirectory, recursive: true);
                }
            }
        }
        catch (Exception exception)
        {
            Log($"Прошлый билд удалить не удалось ({exception.Message}); собираю поверх.");
        }
    }

    private static void TrySetAppleSiliconArchitecture()
    {
        try
        {
            Type settings =
                Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor.OSXStandalone.Extensions")
                ?? Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor")
                ?? throw new InvalidOperationException(
                    "Unity macOS build module does not expose UserBuildSettings.");

            var property = settings.GetProperty("architecture") ??
                throw new InvalidOperationException(
                    "Unity macOS build settings do not expose architecture.");

            // MacOSArchitecture enum: x64 = 0, ARM64 = 1, x64ARM64 (Universal) = 2.
            property.SetValue(null, Enum.ToObject(property.PropertyType, 1));
            Log("macOS target architecture set to Apple Silicon (ARM64).");
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "Failed to configure the required Apple Silicon macOS build architecture.",
                exception);
        }
    }

    private static void Log(string message) => Debug.Log($"[BuildScript] {message}");

    private static void Fail(string message)
    {
        Debug.LogError($"[BuildScript] {message}");
        if (Application.isBatchMode)
        {
            EditorApplication.Exit(1);
        }
    }
}
