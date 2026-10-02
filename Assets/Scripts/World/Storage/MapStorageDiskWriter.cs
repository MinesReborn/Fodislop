#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Persistence;
using MinesServer.Data;

namespace Kern.World;

internal static class MapStorageDiskWriter
{
    internal static string SanitizeWorldCodeName(string worldCodeName)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var sanitized = new System.Text.StringBuilder(worldCodeName.Length);
        foreach (char c in worldCodeName)
        {
            sanitized.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        // Завершающие точка/пробел недопустимы в именах файлов Windows.
        string result = sanitized.ToString().TrimEnd('.', ' ');
        return string.IsNullOrEmpty(result) ? "world" : result;
    }

    internal static void CreateBackup(string mapPath, string backupPath)
    {
        if (File.Exists(backupPath) || !File.Exists(mapPath))
        {
            return;
        }

        CopyAtomically(mapPath, backupPath);
    }

    internal static WorldLayer<CellType> OpenWorldLayer(
        string path,
        int widthChunks,
        int heightChunks,
        IAsyncOperationSupervisor operations,
        Func<string, Stream> openMapFile,
        string backupMapFilePath)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        PrepareWorldLayerFile(
            path,
            widthChunks,
            heightChunks,
            ProjectRuntimeContracts.World.ChunkSize,
            backupMapFilePath);

        try
        {
            return new WorldLayer<CellType>(
                path,
                widthChunks,
                heightChunks,
                operations,
                openMapFile,
                ProjectRuntimeContracts.World.ChunkSize,
                maxRamChunks: ProjectRuntimeContracts.World.ResidentChunkCacheCapacity);
        }
        catch (IOException ioEx)
        {
            throw new IOException($"[MapStorage] Could not open map file '{path}': {ioEx.Message}", ioEx);
        }
        catch (UnauthorizedAccessException authEx)
        {
            throw new UnauthorizedAccessException($"[MapStorage] Access denied for map file '{path}': {authEx.Message}", authEx);
        }
    }

    internal static UniTask PrepareWorldLayerFileAsync(
        string path,
        int widthChunks,
        int heightChunks,
        int chunkSize,
        string backupMapFilePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UniTask.RunOnThreadPool(
            () => PrepareWorldLayerFile(path, widthChunks, heightChunks, chunkSize, backupMapFilePath));
    }

    private static void PrepareWorldLayerFile(
        string path,
        int widthChunks,
        int heightChunks,
        int chunkSize,
        string backupMapFilePath)
    {
        RestoreBackupWhenPrimaryHeaderIsDamaged(
            path,
            backupMapFilePath,
            widthChunks,
            heightChunks,
            chunkSize);
        CreateBackup(path, backupMapFilePath);
        WorldLayer<CellType>.MigrateLegacyFileIfRequired(path, widthChunks, heightChunks, chunkSize);
    }

    private static void RestoreBackupWhenPrimaryHeaderIsDamaged(
        string mapPath,
        string backupMapFilePath,
        int widthChunks,
        int heightChunks,
        int chunkSize)
    {
        if (!File.Exists(backupMapFilePath))
        {
            return;
        }

        bool primaryExists = File.Exists(mapPath);
        int? primaryFormatVersion = primaryExists ? ReadFormatVersion(mapPath) : null;
        if (primaryExists && HasCurrentHeader(mapPath, widthChunks, heightChunks, chunkSize))
        {
            return;
        }

        if (primaryFormatVersion == 0)
        {
            // Version zero has an explicit migration path. Let that migration
            // preserve the source instead of replacing it from an older backup.
            return;
        }

        if (primaryFormatVersion == WorldLayerFileHeader.LegacyRLEFormatVersion &&
            HasRecoverableHeader(mapPath, widthChunks, heightChunks, chunkSize))
        {
            // A valid v1 header has an explicit payload converter. Keep its
            // data as the migration source instead of replacing it with backup.
            return;
        }

        if (primaryFormatVersion > WorldLayerFileHeader.CurrentFormatVersion)
        {
            // Never silently downgrade a map created by a newer client.
            return;
        }

        if (!HasRecoverableHeader(backupMapFilePath, widthChunks, heightChunks, chunkSize))
        {
            return;
        }

        CopyAtomically(backupMapFilePath, mapPath);
    }

    private static int? ReadFormatVersion(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return WorldLayerFileHeader.TryReadFormatVersion(stream);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool HasCurrentHeader(string path, int widthChunks, int heightChunks, int chunkSize)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < WorldLayerFileHeader.HeaderSize)
            {
                return false;
            }

            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            int storedChunkSize = reader.ReadInt32();
            int formatVersion = reader.ReadInt32();
            long tableLength = (long)widthChunks * heightChunks * sizeof(long);

            return width == widthChunks && height == heightChunks &&
                storedChunkSize == chunkSize &&
                formatVersion == WorldLayerFileHeader.CurrentFormatVersion &&
                stream.Length >= WorldLayerFileHeader.HeaderSize + tableLength;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool HasRecoverableHeader(string path, int widthChunks, int heightChunks, int chunkSize)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < WorldLayerFileHeader.HeaderSize)
            {
                return false;
            }

            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            int storedChunkSize = reader.ReadInt32();
            int formatVersion = reader.ReadInt32();
            if (width != widthChunks || height != heightChunks || storedChunkSize != chunkSize)
            {
                return false;
            }

            if (formatVersion == 0)
            {
                return true;
            }

            long tableLength = (long)widthChunks * heightChunks * sizeof(long);
            return (formatVersion == WorldLayerFileHeader.LegacyRLEFormatVersion ||
                formatVersion == WorldLayerFileHeader.CurrentFormatVersion) &&
                stream.Length >= WorldLayerFileHeader.HeaderSize + tableLength;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void CopyAtomically(string sourcePath, string destinationPath)
    {
        string temporaryPath = destinationPath + ".tmp";
        try
        {
            using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var destination = new FileStream(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None))
            {
                source.CopyTo(destination);
                destination.Flush(flushToDisk: true);
            }

            if (File.Exists(destinationPath))
            {
                File.Replace(temporaryPath, destinationPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, destinationPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    internal static void WriteSnapshot(
        WorldLayer<CellType> layer,
        List<(int Index, CellType[] Chunk)> snapshot,
        bool durable,
        string mapFilePath,
        string backupMapFilePath)
    {
        try
        {
            if (durable && snapshot.Count > 0)
            {
                layer.CreateDurableBackup(backupMapFilePath);
            }

            layer.WriteSnapshot(snapshot, flushToDisk: durable);
        }
        catch (Exception ex) when (
            ex is IOException ||
            ex is UnauthorizedAccessException ||
            ex is ObjectDisposedException)
        {
            throw new IOException(
                $"[MapStorage] Failed to persist map '{mapFilePath}'. " +
                "The world cannot continue with unsaved chunks.",
                ex);
        }
    }
}
