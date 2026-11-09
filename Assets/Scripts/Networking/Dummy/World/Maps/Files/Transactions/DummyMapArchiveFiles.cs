#nullable enable

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Persistence;
using UnityEngine;
using UnityEngine.Networking;

namespace Kern.Networking.Dummy;

internal static class DummyMapArchiveFiles
{
    internal const string StampSuffix = ".stamp";
    internal const string TempSuffix = ".tmp";

    internal static void EnsureFreeSpace(string directory, long requiredBytes)
    {
        long availableBytes;
        try
        {
            availableBytes = new DriveInfo(Path.GetFullPath(directory)).AvailableFreeSpace;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or
                NotSupportedException)
        {
            // Размер тома узнать нельзя — нехватку места покажет сама запись.
            return;
        }

        if (availableBytes < requiredBytes + DummyWorldMapArchive.FreeSpaceMarginBytes)
        {
            throw new IOException(
                $"Not enough disk space for the dummy server map in '{directory}': " +
                $"needs {DummyMapArchiveFiles.ToMegabytes(requiredBytes + DummyWorldMapArchive.FreeSpaceMarginBytes)} MB, " +
                $"available {DummyMapArchiveFiles.ToMegabytes(availableBytes)} MB.");
        }
    }

    internal static void InvalidateCache(string mapPath)
    {
        string stampPath = DummyMapArchiveFiles.StampPath(mapPath);
        if (File.Exists(stampPath))
        {
            File.Delete(stampPath);
        }

        if (File.Exists(mapPath))
        {
            File.Delete(mapPath);
        }
    }

    internal static void WriteStamp(string mapPath, string sourceStamp)
    {
        string stampPath = DummyMapArchiveFiles.StampPath(mapPath);
        string tempPath = DummyMapArchiveFiles.NewTempPath(stampPath);
        try
        {
            File.WriteAllText(tempPath, sourceStamp);
            DummyMapArchiveFiles.ReplaceFile(tempPath, stampPath);
        }
        finally
        {
            DummyMapArchiveFiles.DeleteFileQuietly(tempPath);
        }
    }

    internal static void ReplaceFile(string sourcePath, string destinationPath)
    {
        // Keep the previous cache visible until the complete replacement is ready.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(destinationPath))
                {
                    File.Replace(sourcePath, destinationPath, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(sourcePath, destinationPath);
                }

                return;
            }
            catch (IOException) when (attempt < 2)
            {
                // Между удалением и переносом цель записал соседний процесс
                // из того же источника — повторяем.
            }
        }
    }

    internal static void DeleteAbandonedTempFiles(string cacheDirectory)
    {
        DateTime threshold = DateTime.UtcNow - DummyWorldMapArchive.s_abandonedTempAge;
        foreach (string tempPath in Directory.GetFiles(cacheDirectory, "*" + DummyMapArchiveFiles.TempSuffix))
        {
            // Свежий временный файл может дописывать соседний процесс.
            if (File.GetLastWriteTimeUtc(tempPath) < threshold)
            {
                DummyMapArchiveFiles.DeleteFileQuietly(tempPath);
            }
        }
    }

    internal static void DeleteFileQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Брошенный файл подберёт DeleteAbandonedTempFiles.
        }
    }

    internal static string StampPath(string mapPath) => mapPath + DummyMapArchiveFiles.StampSuffix;

    internal static string NewTempPath(string path) => $"{path}.{Guid.NewGuid():N}{DummyMapArchiveFiles.TempSuffix}";

    internal static long ToMegabytes(long bytes) => bytes / (1024 * 1024);

}
