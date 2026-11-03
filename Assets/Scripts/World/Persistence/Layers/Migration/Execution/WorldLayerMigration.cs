#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.Core;

namespace Kern.Persistence;

internal static class WorldLayerMigration<T> where T : unmanaged
{
    public static async UniTask MigrateLegacyFileAsync(
        string filePath,
        int widthChunks,
        int heightChunks,
        int chunkSize = WorldGridDefaults.ChunkSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("World layer file path is required.", nameof(filePath));
        }

        if (widthChunks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthChunks));
        }

        if (heightChunks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(heightChunks));
        }

        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize));
        }

        cancellationToken.ThrowIfCancellationRequested();
        int? sourceFormatVersion;
        using (var stream = File.OpenRead(filePath))
        {
            sourceFormatVersion = WorldLayerFileHeader.TryReadFormatVersion(stream);
        }

        bool requiresMigration = sourceFormatVersion.HasValue &&
            sourceFormatVersion.Value < WorldLayerFileHeader.CurrentFormatVersion;
        Stopwatch? migrationTimer = null;
        if (requiresMigration)
        {
            migrationTimer = Stopwatch.StartNew();
            UnityEngine.Debug.Log(
                $"[WorldLayerMigration] Converting '{filePath}' from format {sourceFormatVersion} " +
                $"to {WorldLayerFileHeader.CurrentFormatVersion}.");
        }

        await UniTask.RunOnThreadPool(
            () => MigrateLegacyFileIfRequired(filePath, widthChunks, heightChunks, chunkSize));
        cancellationToken.ThrowIfCancellationRequested();
        if (migrationTimer != null)
        {
            migrationTimer.Stop();
            UnityEngine.Debug.Log(
                $"[WorldLayerMigration] Converted '{filePath}' in " +
                $"{migrationTimer.Elapsed.TotalSeconds:F1} s.");
        }
    }
    internal static void MigrateLegacyFileIfRequired(
        string filePath,
        int widthChunks,
        int heightChunks,
        int chunkSize)
    {
        WorldLayerFileHeader.MigrateLegacyFormatIfRequired(
            filePath,
            widthChunks,
            heightChunks,
            chunkSize);
        if (!File.Exists(filePath))
        {
            return;
        }

        int? fileVersion;
        using (var versionStream = File.OpenRead(filePath))
        {
            fileVersion = WorldLayerFileHeader.TryReadFormatVersion(versionStream);
        }

        if (fileVersion == WorldLayerFileHeader.LegacyRLEFormatVersion)
        {
            WorldLayerFileMigrator.MigrateV1ToCurrent<T>(filePath, widthChunks, heightChunks, chunkSize);
        }
        else if (fileVersion == WorldLayerFileHeader.LegacyFramedFormatVersion)
        {
            WorldLayerFileMigrator.MigrateV2ToCurrent<T>(filePath, widthChunks, heightChunks, chunkSize);
        }
    }
    internal static void MigrateLegacyFileSynchronously(
        string filePath,
        int widthChunks,
        int heightChunks,
        int chunkSize)
    {
        MigrateLegacyFileIfRequired(filePath, widthChunks, heightChunks, chunkSize);
    }
}
