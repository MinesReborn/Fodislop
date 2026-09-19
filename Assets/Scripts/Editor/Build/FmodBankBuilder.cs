#nullable enable

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Kern.Editor;

public static class FmodBankBuilder
{
    private const string FmodSourceBuildPath = "KernAudio/Build/Desktop";
    private const string StreamingAssetsAudioPath = "Assets/StreamingAssets/Audio";

    [MenuItem("Kern/Audio/Sync FMOD Banks")]
    public static void SyncBanks()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var fsproPath = Path.Combine(projectRoot, "KernAudio", "KernAudio.fspro");
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

            using var process = System.Diagnostics.Process.Start(psi);

            // Компиляция крупного проекта может занять заметно больше 30 с —
            // таймаут сделан щедрым, а не минимальным.
            if (!process.WaitForExit(5 * 60 * 1000))
            {
                process.Kill();
                Fail($"FMOD Studio CLI build timed out after 5 minutes. Banks were not synced.");
                return;
            }

            if (process.ExitCode != 0)
            {
                string error = process.StandardError.ReadToEnd();
                Fail($"FMOD Studio CLI build failed with exit code {process.ExitCode}: {error}");
                return;
            }

            Log("FMOD Studio CLI build completed successfully.");
        }
        catch (Exception ex)
        {
            Fail($"Could not run FMOD Studio CLI compiler: {ex.Message}");
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

    private static void Fail(string message)
    {
        Debug.LogError($"[FmodBankBuilder] {message}");
        if (Application.isBatchMode)
        {
            EditorApplication.Exit(1);
        }
    }
}
