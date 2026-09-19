#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
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
        if (!File.Exists(mapPath))
        {
            return;
        }

        File.Copy(mapPath, backupPath, overwrite: true);
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

        CreateBackup(path, backupMapFilePath);
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

    internal static void WriteSnapshot(
        WorldLayer<CellType> layer,
        List<(int Index, CellType[] Chunk)> snapshot,
        bool durable,
        string mapFilePath)
    {
        try
        {
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
