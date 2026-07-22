#if UNITY_EDITOR
using System;
using System.IO;

using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    /// <summary>
    /// Editor utility for synchronizing FMOD Studio compiled banks
    /// from 'FodinaeAudio/Build/Desktop/' to 'Assets/StreamingAssets/Audio/' and CDN export targets.
    ///
    /// CLI:
    ///   Unity -quit -batchmode -nographics -projectPath . \
    ///         -executeMethod Fodinae.Editor.FmodBankBuilder.SyncBanks
    ///
    /// Menu: Tools > FMOD > Sync Banks to StreamingAssets & CDN
    /// </summary>
    public static class FmodBankBuilder
    {
        private const string FmodSourceBuildPath = "FodinaeAudio/Build/Desktop";
        private const string StreamingAssetsAudioPath = "Assets/StreamingAssets/Audio";

        [MenuItem("Tools/FMOD/Sync Banks to StreamingAssets & CDN")]
        public static void SyncBanks()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var fsproPath = Path.Combine(projectRoot, "FodinaeAudio", "FodinaeAudio.fspro");
            var sourceDir = Path.Combine(projectRoot, FmodSourceBuildPath);
            var targetDir = Path.Combine(projectRoot, StreamingAssetsAudioPath);

            TryCompileFmodStudioProject(fsproPath);

            Log($"Starting FMOD Banks sync from '{sourceDir}' to '{targetDir}'...");

            if (!Directory.Exists(sourceDir))
            {
                Fail($"Source FMOD build directory does not exist: {sourceDir}. Make sure FMOD Studio has built the banks to Desktop platform.");
                return;
            }

            Directory.CreateDirectory(targetDir);

            var bankFiles = Directory.GetFiles(sourceDir, "*.bank", SearchOption.AllDirectories);
            if (bankFiles.Length == 0)
            {
                Fail($"No .bank files found in '{sourceDir}'.");
                return;
            }

            int syncedCount = 0;
            foreach (var bankFile in bankFiles)
            {
                var fileName = Path.GetFileName(bankFile);
                var destPath = Path.Combine(targetDir, fileName);

                File.Copy(bankFile, destPath, true);
                syncedCount++;
                Log($"Copied bank: '{fileName}' -> '{destPath}'");
            }

            AssetDatabase.Refresh();
            Log($"Successfully synchronized {syncedCount} FMOD bank(s) to '{StreamingAssetsAudioPath}'.");
        }

        private static void TryCompileFmodStudioProject(string fsproPath)
        {
            if (!File.Exists(fsproPath))
            {
                Log($"FMOD project not found at: {fsproPath}");
                return;
            }

            var fmodCliPath = ResolveFmodStudioCliPath();
            if (fmodCliPath == null)
            {
                Log("FMOD Studio CLI not found. Skipping compilation step — sync will use previously built banks.");
                return;
            }

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var buildScriptPath = Path.Combine(projectRoot, "FodinaeAudio", "build_fmod_project.js");
            var scriptArg = File.Exists(buildScriptPath) ? $"-script \"{buildScriptPath}\" " : string.Empty;

            try
            {
                Log($"Invoking FMOD Studio CLI compiler: '{fmodCliPath}' -build {scriptArg}'{fsproPath}'...");
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fmodCliPath,
                    Arguments = $"-build {scriptArg}-ignore-warnings \"{fsproPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using var process = System.Diagnostics.Process.Start(psi);
                process.WaitForExit(30000);
                Log($"FMOD Studio CLI build completed with exit code: {process.ExitCode}");
            }
            catch (Exception ex)
            {
                Log($"Could not run FMOD Studio CLI compiler: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolves the FMOD Studio command-line compiler path for the current OS.
        /// Returns null if the executable cannot be found on either macOS or Windows.
        /// </summary>
        private static string ResolveFmodStudioCliPath()
        {
            // macOS default installation path
            const string macos = "/Applications/FMOD Studio.app/Contents/MacOS/fmodstudiocl";

            // Windows default installation path (64-bit)
            const string windows = @"C:\Program Files (x86)\FMOD SoundSystem\FMOD Studio\fmodstudiocl.exe";

            if (File.Exists(macos))
            {
                return macos;
            }

            if (File.Exists(windows))
            {
                return windows;
            }

            return null;
        }

        private static void Log(string message) => Debug.Log($"[FmodBankBuilder] {message}");

        private static void Fail(string message)
        {
            Debug.LogError($"[FmodBankBuilder] {message}");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
#endif
