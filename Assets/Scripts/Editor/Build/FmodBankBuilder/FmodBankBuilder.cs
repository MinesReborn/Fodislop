#nullable enable

using System;
using System.IO;
using System.Linq;
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

    // Явный режим CI: банки уже лежат в KernAudio/Build/Desktop, FMOD Studio
    // на агенте нет, поэтому компиляция проекта пропускается осознанно.
    private const string SkipCompileEnvironmentVariable = "KERN_FMOD_SKIP_COMPILE";
    private const string SkipCompileArgument = "-kernFmodSkipCompile";
    private static readonly string[] s_requiredBanks = new[] {
"Master.bank", "Master.strings.bank"
 };

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report) => SyncBanksCore(throwOnFailure: true);

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

    private static void SyncBanksCore(bool throwOnFailure)
    {
        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var fsproPath = Path.Combine(projectRoot, "KernAudio/KernAudio.fspro");
        var sourceDir = Path.Combine(projectRoot, FmodSourceBuildPath);
        var targetDir = Path.Combine(projectRoot, StreamingAssetsAudioPath);

        if (SkipCompileRequested())
        {
            Log($"[FmodBankBuilder] Compilation skipped: using committed banks from '{sourceDir}'.");
        }
        else
        {
            TryCompileFmodStudioProject(fsproPath);
        }

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

        Directory.CreateDirectory(targetDir);
        int syncedCount = 0;
        foreach (var bankFile in bankFiles)
        {
            var fileName = Path.GetFileName(bankFile);
            var destPath = Path.Combine(targetDir, fileName);

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

    private static bool SkipCompileRequested() =>
        Environment.GetCommandLineArgs().Contains(SkipCompileArgument) ||
        Environment.GetEnvironmentVariable(SkipCompileEnvironmentVariable) == "1";

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

    private static void TryCompileFmodStudioProject(string fsproPath)
    {
        if (!File.Exists(fsproPath))
        {
            throw new BuildFailedException($"FMOD project not found at: {fsproPath}");
        }

        var fmodCliPath = ResolveFmodStudioCliPath();
        if (fmodCliPath == null)
        {
            throw new BuildFailedException("FMOD Studio command-line compiler was not found. Install FMOD Studio before building.");
        }

        string fmodProjectDirectory = Path.GetDirectoryName(fsproPath)
            ?? throw new BuildFailedException($"FMOD project path has no directory: {fsproPath}");
        bool macCommandLine = fmodCliPath.EndsWith("/fmodstudiocl", StringComparison.Ordinal);
        string arguments = $"-build -ignore-warnings {QuoteArgument(fsproPath)}";

        try
        {
            string executable = macCommandLine ? "/usr/bin/script" : fmodCliPath;
            string processArguments = macCommandLine
                ? $"-q /dev/null {QuoteArgument(fmodCliPath)} {arguments}"
                : arguments;
            Log($"Invoking FMOD Studio CLI compiler: '{executable}' {processArguments}...");
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = executable,
                Arguments = processArguments,
                WorkingDirectory = fmodProjectDirectory,
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
                throw new BuildFailedException("FMOD Studio CLI build timed out after 5 minutes. Bank synchronization aborted; existing banks were not used.");
            }

            Task.WhenAll(standardOutputTask, standardErrorTask).GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                string details = standardErrorTask.Result + standardOutputTask.Result;
                throw new BuildFailedException(
                    $"FMOD Studio CLI build failed with exit code {process.ExitCode}: {details}");
            }

            Log(standardOutputTask.Result.Trim());
            Log("FMOD Studio CLI build completed successfully.");
        }
        catch (Exception ex) when (ex is not BuildFailedException)
        {
            throw new BuildFailedException($"Could not run FMOD Studio CLI compiler: {ex}");
        }
    }

    private static string QuoteArgument(string argument) => $"\"{argument.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    private static string? ResolveFmodStudioCliPath()
    {
        // The supported macOS launcher is a wrapper that requires a TTY.
        // TryCompileFmodStudioProject provides one with /usr/bin/script.
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
