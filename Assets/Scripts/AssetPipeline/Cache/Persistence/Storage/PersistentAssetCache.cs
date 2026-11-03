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

    // ETag, записанный в манифест кэша при сохранении (или при первом чтении); null — файла в манифесте нет
    string? GetRecordedETag(string filename);

    // Манифест мира пришёл: в кэше остаются только его файлы, всё остальное удаляется.
    // Возвращает, сколько файлов удалено
    int RetainOnly(IReadOnlyCollection<string> manifestFiles);
}

public sealed class PersistentAssetCache : IPersistentAssetCache
{
    private const string AssetsDirectoryName = "Assets";
    private const int ManifestFlushDelayMilliseconds = 250;
    private readonly string _cachePath;
    private readonly IAsyncOperationSupervisor? _operations;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _entryGates =
        new(StringComparer.Ordinal);
    private readonly PersistentAssetCacheManifest _manifest;
    private int _manifestFlushScheduled;

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
        _manifest = new PersistentAssetCacheManifest(_cachePath);
        if (_manifest.WasBroken)
        {
            ClearUnverifiableFiles();
        }

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
            byte[]? payload = VerifyOrDrop(filename, assetPath, ReadAsset(assetPath));
            if (payload != null)
            {
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
            byte[]? payload = await VerifyOrDropAsync(filename, assetPath, await ReadAssetAsync(assetPath));
            if (payload != null)
            {
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
            _manifest.Set(filename, PersistentAssetCacheManifest.Fingerprint(data));
        }
        finally
        {
            gate.Release();
        }

        FlushManifest();
    }

    public async UniTask SaveAssetAsync(string filename, byte[] data)
    {
        ValidateAssetInput(filename, data);
        // MD5 всего файла — в пуле потоков, до входа под замок записи
        PersistentAssetCacheEntry fingerprint = await UniTask.RunOnThreadPool(
            () => PersistentAssetCacheManifest.Fingerprint(data));
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
            // Файл и запись о нём меняются под одним замком: читатель не увидит свежий файл со старой записью
            _manifest.Set(filename, fingerprint);
        }
        finally
        {
            gate.Release();
        }

        FlushManifest();
    }

    public async UniTask<string?> GetETagAsync(string filename)
    {
        string assetPath = GetAssetPath(filename);
        SemaphoreSlim gate = GetEntryGate(assetPath);
        await gate.WaitAsync();
        try
        {
            byte[]? payload = await VerifyOrDropAsync(filename, assetPath, await ReadAssetAsync(assetPath));
            if (payload == null)
            {
                return null;
            }
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
            byte[]? payload = VerifyOrDrop(filename, assetPath, ReadAsset(assetPath));
            if (payload == null)
            {
                return null;
            }
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
            _manifest.Remove(filename);
        }
        finally
        {
            gate.Release();
        }

        FlushManifest();
    }

    public string? GetRecordedETag(string filename) =>
        _manifest.TryGet(filename, out PersistentAssetCacheEntry entry) ? entry.ETag : null;

    public int RetainOnly(IReadOnlyCollection<string> manifestFiles)
    {
        var keep = new HashSet<string>(manifestFiles, StringComparer.Ordinal);
        _manifest.RemoveUnlessKept(keep);

        // По диску, а не только по записям манифеста кэша: файл без записи тоже лишний, если сервер о нём не говорит
        int removed = 0;
        foreach (string assetPath in Directory.EnumerateFiles(_cachePath, "*", SearchOption.AllDirectories).ToList())
        {
            string filename = Path.GetRelativePath(_cachePath, assetPath).Replace('\\', '/');
            if (filename == PersistentAssetCacheManifest.FileName ||
                PersistentAssetCacheFiles.IsSystemFile(filename) ||
                keep.Contains(filename))
            {
                continue;
            }

            SemaphoreSlim gate = GetEntryGate(assetPath);
            gate.Wait();
            try
            {
                DeleteAssetAndEmptyDirectories(assetPath);
                _manifest.Remove(filename);
                removed++;
            }
            finally
            {
                gate.Release();
            }
        }

        FlushManifest();
        return removed;
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

    // Файл читается только если его MD5 сходится с записью манифеста. Несовпадение — порча: файл и запись
    // удаляются, вызывающий видит «нет в кэше» и качает заново. Файл без записи (кэш прошлых версий) усыновляется:
    // его отпечаток записывается при первом чтении, и дальше порча ловится
    private byte[]? VerifyOrDrop(string filename, string assetPath, byte[]? payload)
    {
        if (payload == null)
        {
            return null;
        }

        return ApplyVerification(
            filename,
            assetPath,
            payload,
            PersistentAssetCacheManifest.Fingerprint(payload));
    }

    private async UniTask<byte[]?> VerifyOrDropAsync(string filename, string assetPath, byte[]? payload)
    {
        if (payload == null)
        {
            return null;
        }

        PersistentAssetCacheEntry actual = await UniTask.RunOnThreadPool(
            () => PersistentAssetCacheManifest.Fingerprint(payload));
        return ApplyVerification(filename, assetPath, payload, actual);
    }

    private byte[]? ApplyVerification(string filename, string assetPath, byte[] payload, PersistentAssetCacheEntry actual)
    {
        if (!_manifest.TryGet(filename, out PersistentAssetCacheEntry recorded))
        {
            _manifest.Set(filename, actual);
            FlushManifest();
            return payload;
        }

        if (recorded.Matches(actual.ETag))
        {
            return payload;
        }

        Debug.LogWarning(
            $"[PersistentAssetCache] '{filename}' is corrupted (MD5 differs from the cache manifest); " +
            "the file is dropped and will be downloaded again.");
        DeleteAssetAndEmptyDirectories(assetPath);
        _manifest.Remove(filename);
        FlushManifest();
        return null;
    }

    // Манифест кэша повреждён: узнать, какие файлы целы, нельзя, а усыновление приняло бы порченые за целые.
    // Кэш скачивается заново с сервера, поэтому честнее очистить его и начать с чистого манифеста
    private void ClearUnverifiableFiles()
    {
        int removed = 0;
        try
        {
            foreach (string path in Directory.EnumerateFiles(_cachePath, "*", SearchOption.AllDirectories).ToList())
            {
                if (PersistentAssetCacheFiles.IsSystemFile(path))
                {
                    continue;
                }

                PersistentAssetCacheFiles.DeleteIfExists(path);
                removed++;
            }

            // Пустые папки убираем изнутри наружу; папка со служебным файлом остаётся
            foreach (string directory in Directory.EnumerateDirectories(_cachePath, "*", SearchOption.AllDirectories)
                         .OrderByDescending(path => path.Length)
                         .ToList())
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.LogWarning($"[PersistentAssetCache] Cache cleanup after a broken manifest failed: {exception.Message}");
        }

        Debug.LogWarning(
            $"[PersistentAssetCache] The cache manifest is unreadable; {removed} cached files were removed " +
            "and will be downloaded again.");
        _manifest.Flush();
    }

    // Файлы сохраняются десятками подряд: манифест переписывается одной записью после серии изменений, а не на каждый файл.
    // Не успел записаться при выходе — файлы без записи усыновятся при следующем чтении
    private void FlushManifest()
    {
        if (_operations == null)
        {
            _manifest.Flush();
            return;
        }

        if (Interlocked.Exchange(ref _manifestFlushScheduled, 1) == 1)
        {
            return;
        }

        try
        {
            _operations.Run(
                "persistent_asset_cache_manifest_flush",
                async cancellationToken =>
                {
                    await UniTask.Delay(ManifestFlushDelayMilliseconds, cancellationToken: cancellationToken);
                    Volatile.Write(ref _manifestFlushScheduled, 0);
                    await UniTask.RunOnThreadPool(_manifest.Flush, cancellationToken: cancellationToken);
                });
        }
        catch (ObjectDisposedException)
        {
            Volatile.Write(ref _manifestFlushScheduled, 0);
        }
    }

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
}
