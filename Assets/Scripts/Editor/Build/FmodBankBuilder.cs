#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Kern.Editor;

public sealed class FmodBankBuilder : IPreprocessBuildWithReport
{
    private const string FmodSourceBuildPath = "KernAudio/Build/Desktop";
    private const string StreamingAssetsAudioPath = "Assets/StreamingAssets/Audio";
    private static readonly string[] s_requiredBanks = ["Master.bank", "Master.strings.bank"];

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report) => SyncBanksForBuild();

    [MenuItem("Kern/Audio/Sync FMOD Banks")]
    public static void SyncBanks()
    {
        try
        {
            SyncBanksCore(throwOnFailure: false);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[FmodBankBuilder] {ex.Message}");
        }
    }

    private static void SyncBanksForBuild() => SyncBanksCore(throwOnFailure: true);

    private static void SyncBanksCore(bool throwOnFailure)
    {
        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var fsproPath = Path.Combine(projectRoot, "KernAudio", "KernAudio.fspro");
        var sourceDir = Path.Combine(projectRoot, FmodSourceBuildPath);
        var targetDir = Path.Combine(projectRoot, StreamingAssetsAudioPath);

        bool compiled = TryCompileFmodStudioProject(fsproPath);

        Log($"Starting FMOD Banks sync from '{sourceDir}' to '{targetDir}'...");

        if (!Directory.Exists(sourceDir))
        {
            Fail(throwOnFailure, $"Source FMOD build directory does not exist: {sourceDir}. Make sure FMOD Studio has built the banks to Desktop platform.");
            return;
        }

        var bankFiles = Directory.GetFiles(sourceDir, "*.bank", SearchOption.AllDirectories);
        if (bankFiles.Length == 0)
        {
            Fail(throwOnFailure, $"No .bank files found in '{sourceDir}'.");
            return;
        }

        // Without the FMOD compiler, an already populated StreamingAssets
        // directory may contain a newer local build than the checked-in
        // fallback binaries. Preserve that build on this machine.
        if (!compiled && RequiredBanksExist(targetDir))
        {
            Log("FMOD CLI unavailable; preserving the existing complete StreamingAssets bank set.");
            return;
        }

        Directory.CreateDirectory(targetDir);
        int syncedCount = 0;
        foreach (var bankFile in bankFiles)
        {
            var fileName = Path.GetFileName(bankFile);
            var destPath = Path.Combine(targetDir, fileName);

            if (!compiled && File.Exists(destPath))
            {
                continue;
            }

            File.Copy(bankFile, destPath, true);
            syncedCount++;
            Log($"Copied bank: '{fileName}' -> '{destPath}'");
        }

        if (!RequiredBanksExist(targetDir))
        {
            Fail(throwOnFailure,
                $"Required FMOD banks are missing from '{targetDir}'. Expected: {string.Join(", ", s_requiredBanks)}.");
            return;
        }

        AssetDatabase.Refresh();
        Log($"Successfully synchronized {syncedCount} FMOD bank(s) to '{StreamingAssetsAudioPath}'.");
    }

    private static bool RequiredBanksExist(string directory)
    {
        foreach (string bank in s_requiredBanks)
        {
            if (!File.Exists(Path.Combine(directory, bank)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryCompileFmodStudioProject(string fsproPath)
    {
        if (!File.Exists(fsproPath))
        {
            Log($"FMOD project not found at: {fsproPath}");
            return false;
        }

        var fmodCliPath = ResolveFmodStudioCliPath();
        if (fmodCliPath == null)
        {
            Log("FMOD Studio CLI not found. Skipping compilation step — sync will use previously built banks.");
            return false;
        }

        try
        {
            Log($"Invoking FMOD Studio CLI compiler: '{fmodCliPath}' -build -ignore-warnings \"{fsproPath}\"...");
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fmodCliPath,
                Arguments = $"-build -ignore-warnings \"{fsproPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = System.Diagnostics.Process.Start(psi) ??
                throw new BuildFailedException("FMOD Studio CLI could not be started.");
            Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();

            // Компиляция крупного проекта может занять заметно больше 30 с —
            // таймаут сделан щедрым, а не минимальным.
            if (!process.WaitForExit(5 * 60 * 1000))
            {
                process.Kill();
                process.WaitForExit();
                Task.WhenAll(standardOutputTask, standardErrorTask).GetAwaiter().GetResult();
                throw new BuildFailedException("FMOD Studio CLI build timed out after 5 minutes. Banks were not synced.");
            }

            Task.WhenAll(standardOutputTask, standardErrorTask).GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                string error = standardErrorTask.Result;
                throw new BuildFailedException(
                    $"FMOD Studio CLI build failed with exit code {process.ExitCode}: {error}");
            }

            Log("FMOD Studio CLI build completed successfully.");
            return true;
        }
        catch (Exception ex)
        {
            if (ex is BuildFailedException)
            {
                throw;
            }

            throw new BuildFailedException($"Could not run FMOD Studio CLI compiler: {ex.Message}");
        }
    }

    private static string? ResolveFmodStudioCliPath()
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

    private static void Fail(bool throwOnFailure, string message)
    {
        Debug.LogError($"[FmodBankBuilder] {message}");
        if (throwOnFailure)
        {
            throw new BuildFailedException(message);
        }
    }
}
