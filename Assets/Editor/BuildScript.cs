#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Fodinae.Editor
{
    /// <summary>
    /// Repeatable Fodinae player builds (menu + headless CLI).
    ///
    /// CLI:
    ///   Unity -quit -batchmode -nographics -projectPath . \
    ///         -executeMethod Fodinae.Editor.BuildScript.BuildMacOS
    ///   Add -fodinaeDev for a Development build (debugging + profiler).
    ///   Exit code is non-zero on failure (CI-friendly).
    ///
    /// Menu: Build > macOS (Apple Silicon) / Windows 64.
    /// Output goes to Build/&lt;platform&gt;/ (gitignored).
    /// </summary>
    public static class BuildScript
    {
        private const string ProductName = "Fodinae";
        private const string DevArg = "-fodinaeDev";

        private static string[] EnabledScenes =>
            EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

        [MenuItem("Build/macOS (Apple Silicon)")]
        public static void BuildMacOS() =>
            Build(BuildTarget.StandaloneOSX, $"Build/macOS/{ProductName}.app", isApple: true);

        [MenuItem("Build/Windows 64")]
        public static void BuildWindows() =>
            Build(BuildTarget.StandaloneWindows64, $"Build/Windows/{ProductName}.exe");

        private static void Build(BuildTarget target, string relativeOutput, bool isApple = false)
        {
            var scenes = EnabledScenes;
            if (scenes.Length == 0)
            {
                Fail("No enabled scenes in EditorBuildSettings — nothing to build.");
                return;
            }

            string output = Path.GetFullPath(relativeOutput);
            Directory.CreateDirectory(Path.GetDirectoryName(output));

            if (isApple)
            {
                TrySetAppleSiliconArchitecture();
            }

            bool development = Environment.GetCommandLineArgs().Contains(DevArg);
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = target,
                options = development
                    ? BuildOptions.Development | BuildOptions.AllowDebugging
                    : BuildOptions.None,
            };

            Log($"Building {target} -> {output} (development={development}, scenes={scenes.Length})");
            BuildSummary summary = BuildPipeline.BuildPlayer(options).summary;
            Log($"Result={summary.result} size={summary.totalSize}B " +
                $"time={summary.totalTime} warnings={summary.totalWarnings} errors={summary.totalErrors}");

            if (summary.result != BuildResult.Succeeded)
            {
                Fail($"Build failed: {summary.result} ({summary.totalErrors} errors).");
                return;
            }

            Log($"Build succeeded: {output}");
        }

        /// <summary>
        /// The macOS target architecture lives in the macOS build module
        /// (UnityEditor.OSXStandalone), which contributors on Windows-only
        /// installs may not have. Resolve it reflectively so this editor
        /// assembly keeps compiling everywhere; fall back to the project
        /// default (which already produces a working Apple Silicon build)
        /// when the module is absent.
        /// </summary>
        private static void TrySetAppleSiliconArchitecture()
        {
            try
            {
                Type settings =
                    Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor.OSXStandalone.Extensions")
                    ?? Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor");

                var property = settings?.GetProperty("architecture");
                if (property == null)
                {
                    Log("macOS build module unavailable — using project default architecture.");
                    return;
                }

                // MacOSArchitecture enum: x64 = 0, ARM64 = 1, x64ARM64 (Universal) = 2.
                property.SetValue(null, Enum.ToObject(property.PropertyType, 1));
                Log("macOS target architecture set to Apple Silicon (ARM64).");
            }
            catch (Exception e)
            {
                Log($"Could not set macOS architecture ({e.Message}); using project default.");
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
}
#endif
