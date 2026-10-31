#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using UnityEngine;
using VContainer;

namespace Kern.AssetPipeline.Cache;

public interface IPersistentAssetCache
{
    UniTask<byte[]?> GetAssetAsync(string filename);

    UniTask SaveAssetAsync(string filename, byte[] data);

    UniTask<string?> GetETagAsync(string filename);

    bool HasAsset(string filename);

    void RemoveAsset(string filename);

    string GetAssetPath(string filename);
}

public sealed class PersistentAssetCache : IPersistentAssetCache
{
    private const string AssetsDirectoryName = "Assets";
    private const long DiskMaintenanceIntervalTicks = TimeSpan.TicksPerSecond * 30;
    private readonly string _cachePath;
    private readonly IAsyncOperationSupervisor? _operations;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _entryGates =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _diskMaintenanceGate = new(1, 1);
    private long _nextDiskMaintenanceUtcTicks;

    [Inject]
    public PersistentAssetCache(IAsyncOperationSupervisor operations)
        : this(GetDefaultCachePath(), operations)
    {
    }

    internal PersistentAssetCache(string cachePath, IAsyncOperationSupervisor? operations = null)
    {
        _cachePath = Path.GetFullPath(cachePath);
        _operations = operations;
        Directory.CreateDirectory(_cachePath);
#if UNITY_IOS && !UNITY_EDITOR
        // Кэш скачивается заново с CDN: в резервную копию iCloud ему нельзя,
        // иначе он съедает квоту игрока и нарушает правила App Store.
        UnityEngine.iOS.Device.SetNoBackupFlag(_cachePath);
#endif
    }

    private static string GetDefaultCachePath()
    {
        string persistentPath = Application.persistentDataPath;
        if (string.IsNullOrWhiteSpace(persistentPath))
        {
            throw new InvalidOperationException(
                "Application.persistentDataPath is required for the persistent asset cache.");
        }

        string? parentPath = Path.GetDirectoryName(persistentPath);
        if (string.IsNullOrWhiteSpace(parentPath) || !Directory.Exists(parentPath))
        {
            throw new DirectoryNotFoundException(
                $"Persistent data parent directory '{parentPath}' does not exist.");
        }

        return Path.Combine(persistentPath, AssetsDirectoryName);
    }

    public byte[]? GetAsset(string filename)
    {
        string assetPath = GetAssetPath(filename);
        SemaphoreSlim gate = GetEntryGate(assetPath);
        gate.Wait();
        try
        {
            byte[]? payload = ReadAsset(assetPath);
            if (payload != null)
            {
                PersistentAssetCacheFiles.Touch(assetPath);
            }

            return payload;
        }
        finally
        {
            gate.Release();
        }
    }

    public async UniTask<byte[]?> GetAssetAsync(string filename)
    {
        string assetPath = GetAssetPath(filename);
        SemaphoreSlim gate = GetEntryGate(assetPath);
        await gate.WaitAsync();
        try
        {
            byte[]? payload = await ReadAssetAsync(assetPath);
            if (payload != null)
            {
                PersistentAssetCacheFiles.Touch(assetPath);
            }

            return payload;
        }
        finally
        {
            gate.Release();
        }
    }

    public void SaveAsset(string filename, byte[] data)
    {
        ValidateAssetInput(filename, data);
        string assetPath = GetAssetPath(filename);
        SemaphoreSlim gate = GetEntryGate(assetPath);
        gate.Wait();
        try
        {
            string? directory = Path.GetDirectoryName(assetPath);
            if (directory == null)
            {
                throw new InvalidOperationException($"Asset cache path has no parent directory: '{assetPath}'.");
            }

            Directory.CreateDirectory(directory);
            PersistentAssetCacheFiles.WriteAtomically(assetPath, data);
        }
        finally
        {
            gate.Release();
        }

        ScheduleDiskTrim();
    }

    public async UniTask SaveAssetAsync(string filename, byte[] data)
    {
        ValidateAssetInput(filename, data);
        string assetPath = GetAssetPath(filename);
        SemaphoreSlim gate = GetEntryGate(assetPath);
        await gate.WaitAsync();
        try
        {
            string? directory = Path.GetDirectoryName(assetPath);
            if (directory == null)
            {
                throw new InvalidOperationException($"Asset cache path has no parent directory: '{assetPath}'.");
            }

            Directory.CreateDirectory(directory);
            await PersistentAssetCacheFiles.WriteAtomicallyAsync(assetPath, data);
        }
        finally
        {
            gate.Release();
        }

        await TrimDiskCacheAsync();
    }

    public async UniTask<string?> GetETagAsync(string filename)
    {
        string assetPath = GetAssetPath(filename);
        SemaphoreSlim gate = GetEntryGate(assetPath);
        await gate.WaitAsync();
        try
        {
            byte[]? payload = await ReadAssetAsync(assetPath);
            if (payload == null)
            {
                return null;
            }

            PersistentAssetCacheFiles.Touch(assetPath);
            return GetETag(payload);
        }
        finally
        {
            gate.Release();
        }
    }

    public string? GetETag(string filename)
    {
        string assetPath = GetAssetPath(filename);
        SemaphoreSlim gate = GetEntryGate(assetPath);
        gate.Wait();
        try
        {
            byte[]? payload = ReadAsset(assetPath);
            if (payload == null)
            {
                return null;
            }

            PersistentAssetCacheFiles.Touch(assetPath);
            return GetETag(payload);
        }
        finally
        {
            gate.Release();
        }
    }

    public bool HasAsset(string filename)
    {
        string assetPath = GetAssetPath(filename);
        return File.Exists(assetPath);
    }

    public void RemoveAsset(string filename)
    {
        string assetPath = GetAssetPath(filename);
        SemaphoreSlim gate = GetEntryGate(assetPath);
        gate.Wait();
        try
        {
            DeleteAssetAndEmptyDirectories(assetPath);
        }
        finally
        {
            gate.Release();
        }
    }

    public string GetAssetPath(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            throw new ArgumentException("Asset filename cannot be empty.", nameof(filename));
        }

        if (Path.IsPathRooted(filename) || filename.Contains('\\'))
        {
            throw new ArgumentException("Asset filename must be a relative path using forward slashes.", nameof(filename));
        }

        string[] segments = filename.Split('/');
        if (Array.Exists(segments, segment =>
                string.IsNullOrWhiteSpace(segment) ||
                segment == "." ||
                segment == ".."))
        {
            throw new ArgumentException("Asset filename contains an invalid path segment.", nameof(filename));
        }

        string fullPath = Path.GetFullPath(Path.Combine(_cachePath, filename));
        string relativePath = Path.GetRelativePath(_cachePath, fullPath);
        if (relativePath == ".." ||
            relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("Asset filename escapes the persistent cache directory.", nameof(filename));
        }

        return fullPath;
    }

    private static void ValidateAssetInput(string filename, byte[] data)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            throw new ArgumentException("Asset filename cannot be empty.", nameof(filename));
        }

        if (data == null || data.Length == 0)
        {
            throw new ArgumentException("Asset data cannot be null or empty.", nameof(data));
        }
    }

    private SemaphoreSlim GetEntryGate(string assetPath) =>
        _entryGates.GetOrAdd(assetPath, _ => new SemaphoreSlim(1, 1));

    private static byte[]? ReadAsset(string assetPath)
    {
        if (!File.Exists(assetPath))
        {
            return null;
        }

        byte[] payload = File.ReadAllBytes(assetPath);
        return payload.Length == 0 ? null : payload;
    }

    private static async UniTask<byte[]?> ReadAssetAsync(string assetPath)
    {
        if (!File.Exists(assetPath))
        {
            return null;
        }

        byte[] payload = await File.ReadAllBytesAsync(assetPath).AsUniTask();
        return payload.Length == 0 ? null : payload;
    }

    private static string GetETag(byte[] payload) => ETagCalculator.Calculate(payload) ?? string.Empty;

    private void DeleteAssetAndEmptyDirectories(string assetPath)
    {
        PersistentAssetCacheFiles.DeleteIfExists(assetPath);
        RemoveEmptyParentDirectories(assetPath);
    }

    private void RemoveEmptyParentDirectories(string assetPath)
    {
        string? directoryPath = Path.GetDirectoryName(assetPath);
        while (directoryPath != null &&
               !string.Equals(directoryPath, _cachePath, StringComparison.Ordinal) &&
               directoryPath.StartsWith(_cachePath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            if (!Directory.Exists(directoryPath) || Directory.EnumerateFileSystemEntries(directoryPath).Any())
            {
                return;
            }

            Directory.Delete(directoryPath);
            directoryPath = Path.GetDirectoryName(directoryPath);
        }
    }

    private async UniTask TrimDiskCacheAsync()
    {
        long nowUtcTicks = DateTime.UtcNow.Ticks;
        if (nowUtcTicks < Interlocked.Read(ref _nextDiskMaintenanceUtcTicks))
        {
            return;
        }

        await UniTask.RunOnThreadPool(TryTrimDiskCache);
    }

    private void ScheduleDiskTrim()
    {
        if (_operations == null)
        {
            return;
        }

        try
        {
            _operations.Run(
                "persistent_asset_cache_trim",
                _ => UniTask.RunOnThreadPool(TryTrimDiskCache));
        }
        catch (ObjectDisposedException)
        {
            // Disk trimming is maintenance; a cache save remains valid during teardown.
        }
    }

    private void TryTrimDiskCache()
    {
        long nowUtcTicks = DateTime.UtcNow.Ticks;
        if (nowUtcTicks < Interlocked.Read(ref _nextDiskMaintenanceUtcTicks) ||
            !_diskMaintenanceGate.Wait(0))
        {
            return;
        }

        try
        {
            nowUtcTicks = DateTime.UtcNow.Ticks;
            if (nowUtcTicks < Interlocked.Read(ref _nextDiskMaintenanceUtcTicks))
            {
                return;
            }

            Interlocked.Exchange(
                ref _nextDiskMaintenanceUtcTicks,
                nowUtcTicks + DiskMaintenanceIntervalTicks);

            List<(string Filename, string AssetPath, long Size, DateTime AccessUtc)> entries =
                Directory.EnumerateFiles(_cachePath, "*", SearchOption.AllDirectories)
                    .Select(assetPath =>
                    {
                        var file = new FileInfo(assetPath);
                        return (
                            Filename: Path.GetRelativePath(_cachePath, assetPath).Replace('\\', '/'),
                            AssetPath: assetPath,
                            Size: file.Length,
                            AccessUtc: file.LastAccessTimeUtc);
                    })
                    .ToList();

            long totalBytes = entries.Sum(entry => entry.Size);
            if (totalBytes <= AssetStreamingLimits.PersistentAssetCacheCapacityBytes)
            {
                return;
            }

            entries.Sort(static (left, right) => left.AccessUtc.CompareTo(right.AccessUtc));
            foreach (var entry in entries)
            {
                if (totalBytes <= AssetStreamingLimits.PersistentAssetCacheCapacityBytes)
                {
                    break;
                }

                string assetPath = entry.AssetPath;
                long size = entry.Size;
                SemaphoreSlim entryGate = GetEntryGate(assetPath);
                if (!entryGate.Wait(0))
                {
                    continue;
                }

                try
                {
                    if (!File.Exists(assetPath))
                    {
                        continue;
                    }

                    DeleteAssetAndEmptyDirectories(assetPath);
                    totalBytes -= size;
                }
                finally
                {
                    entryGate.Release();
                }
            }

        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.LogWarning($"[PersistentAssetCache] Disk-cache trim failed: {exception.Message}");
        }
        finally
        {
            _diskMaintenanceGate.Release();
        }
    }
}
