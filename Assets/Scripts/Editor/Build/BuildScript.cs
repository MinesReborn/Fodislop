#nullable enable

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Kern.Editor;

public static class BuildScript
{
    private const string ProductName = "Kern";
    private const string DevArg = "-kernDev";

    private static string[] _EnabledScenes =>
        EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

    [MenuItem("Kern/Build/macOS (Apple Silicon)")]
    public static void BuildMacOS() =>
        BuildPlayerArtifact(BuildTarget.StandaloneOSX, $"Build/macOS/{ProductName}.app", isApple: true);

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

    [MenuItem("Kern/Build/iOS Xcode Project")]
    public static void BuildIOS() =>
        BuildPlayerArtifact(BuildTarget.iOS, "Build/iOS");

    private static void BuildPlayerArtifact(
        BuildTarget target,
        string relativeOutput,
        bool isApple = false,
        bool profiling = false)
    {
        BuildSceneOrder.Validate();

        var scenes = _EnabledScenes;
        if (scenes.Length == 0)
        {
            Fail("No enabled scenes in EditorBuildSettings — nothing to build.");
            return;
        }

        if (!EnsureActiveBuildTarget(target))
        {
            return;
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

        bool development = Environment.GetCommandLineArgs().Contains(DevArg);

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
            : development ? Il2CppCompilerConfiguration.Debug
            : Il2CppCompilerConfiguration.Master);
        PlayerSettings.SetIl2CppCodeGeneration(
            namedTarget,
            UnityEditor.Build.Il2CppCodeGeneration.OptimizeSpeed);

        // Minimal, а не Medium/High: в проекте VContainer, а он резолвит
        // типы рефлексией — агрессивный стриппинг вырезает то, на что
        // нет статических ссылок, и DI падает только в билде.
        PlayerSettings.SetManagedStrippingLevel(namedTarget, ManagedStrippingLevel.Minimal);

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = output,
            target = target,
            options = profiling
                ? BuildOptions.Development | BuildOptions.ConnectWithProfiler
                : development
                // ConnectWithProfiler — без него профайлер к билду не
                // цепляется, хотя AllowDebugging создаёт впечатление, что
                // всё включено.
                ? BuildOptions.Development | BuildOptions.AllowDebugging | BuildOptions.ConnectWithProfiler
                : BuildOptions.None,
        };

        Log($"Building {target} -> {output} (development={development}, profiling={profiling}, scenes={scenes.Length})");
        BuildSummary summary = BuildPipeline.BuildPlayer(options).summary;
        Log($"Result={summary.result} size={summary.totalSize}B " +
            $"time={summary.totalTime} warnings={summary.totalWarnings} errors={summary.totalErrors}");

        if (summary.result != BuildResult.Succeeded)
        {
            Fail($"Build failed: {summary.result} ({summary.totalErrors} errors).");
            return;
        }

        Log($"Build succeeded: {output}");
        Log($"Версия {PlayerSettings.bundleVersion}{(development ? " (development)" : string.Empty)}");
        Log($"Запуск: {LaunchHint(target, output)}");
    }

    private static string LaunchHint(BuildTarget target, string output) => target switch
    {
        BuildTarget.StandaloneOSX => $"open \"{output}\"",
        BuildTarget.StandaloneWindows64 => $"\"{output}\"",
        BuildTarget.StandaloneLinux64 => $"\"{output}\"",
        BuildTarget.Android => $"adb install -r \"{output}\"",
        BuildTarget.iOS => $"open \"{output}\"",
        _ => output,
    };

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
