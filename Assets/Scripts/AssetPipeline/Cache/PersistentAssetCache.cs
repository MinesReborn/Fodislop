#nullable enable

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;

namespace Fodinae;

public interface IPersistentAssetCache
{
    UniTask<byte[]?> GetAssetAsync(string filename);

    UniTask SaveAssetAsync(string filename, byte[] data, string etag);

    UniTask<string?> GetETagAsync(string filename);

    bool HasAsset(string filename);

    void RemoveAsset(string filename);

    string GetAssetPath(string filename);
}

public sealed class PersistentAssetCache : IPersistentAssetCache
{
    private readonly string _cachePath;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _entryGates =
        new(StringComparer.OrdinalIgnoreCase);

    /// <remarks>
    /// [Inject] обязателен, хотя параметров нет. Без него VContainer выбирает
    /// конструктор с НАИБОЛЬШИМ числом параметров, просматривая и непубличные,
    /// то есть тестовый (string cachePath) — и падает на разрешении System.String,
    /// роняя сборку контейнера целиком в Awake бутстрапа.
    /// </remarks>
    [Inject]
    public PersistentAssetCache()
        : this(GetDefaultCachePath())
    {
    }

    /// <summary>Путь задаётся только из тестов; контейнер сюда не ходит.</summary>
    internal PersistentAssetCache(string cachePath)
    {
        _cachePath = Path.GetFullPath(cachePath);
        PersistentAssetCacheFormat.EnsureCurrent(_cachePath);
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

        return Path.Combine(persistentPath, "AssetCache");
    }

    // ═══════════════════════════════════════════════════════════
    //  Public API
    // ═══════════════════════════════════════════════════════════

    public byte[]? GetAsset(string filename)
    {
        string assetPath = GetAssetPath(filename);
        SemaphoreSlim gate = GetEntryGate(assetPath);
        gate.Wait();
        try
        {
            return ReadVerifiedAsset(assetPath, out _);
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
            return await ReadVerifiedAssetAsync(assetPath);
        }
        finally
        {
            gate.Release();
        }
    }

    public void SaveAsset(string filename, byte[] data, string etag)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            throw new ArgumentException("Asset filename cannot be empty.", nameof(filename));
        }

        if (data == null || data.Length == 0)
        {
            throw new ArgumentException("Asset data cannot be null or empty.", nameof(data));
        }

        string assetPath = GetAssetPath(filename);
        SemaphoreSlim gate = GetEntryGate(assetPath);
        gate.Wait();
        try
        {
            SaveAssetCore(assetPath, data, etag);
        }
        finally
        {
            gate.Release();
        }
    }

    private static void SaveAssetCore(string assetPath, byte[] data, string etag)
    {
        string manifestPath = GetManifestPath(assetPath);

        string? directory = Path.GetDirectoryName(assetPath);
        if (directory == null)
        {
            throw new InvalidOperationException(
                $"Asset cache path has no parent directory: '{assetPath}'.");
        }

        Directory.CreateDirectory(directory);
        WriteAtomically(assetPath, data);
        WriteAtomically(manifestPath, PersistentAssetCacheEntryManifest.Create(data, etag).Serialize());
        DeleteIfExists(GetLegacyETagPath(assetPath));
    }

    public async UniTask SaveAssetAsync(string filename, byte[] data, string etag)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            throw new ArgumentException("Asset filename cannot be empty.", nameof(filename));
        }

        if (data == null || data.Length == 0)
        {
            throw new ArgumentException("Asset data cannot be null or empty.", nameof(data));
        }

        string assetPath = GetAssetPath(filename);
        SemaphoreSlim gate = GetEntryGate(assetPath);
        await gate.WaitAsync();
        try
        {
            await SaveAssetCoreAsync(assetPath, data, etag);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async UniTask SaveAssetCoreAsync(string assetPath, byte[] data, string etag)
    {
        string manifestPath = GetManifestPath(assetPath);

        string? directory = Path.GetDirectoryName(assetPath);
        if (directory == null)
        {
            throw new InvalidOperationException(
                $"Asset cache path has no parent directory: '{assetPath}'.");
        }

        Directory.CreateDirectory(directory);
        await WriteAtomicallyAsync(assetPath, data);
        await WriteAtomicallyAsync(
            manifestPath,
            PersistentAssetCacheEntryManifest.Create(data, etag).Serialize());
        DeleteIfExists(GetLegacyETagPath(assetPath));
    }

    public string? GetETag(string filename)
    {
        string assetPath = GetAssetPath(filename);
        SemaphoreSlim gate = GetEntryGate(assetPath);
        gate.Wait();
        try
        {
            return ReadVerifiedAsset(assetPath, out PersistentAssetCacheEntryManifest manifest) != null
                ? manifest.ETag
                : null;
        }
        finally
        {
            gate.Release();
        }
    }

    public async UniTask<string?> GetETagAsync(string filename)
    {
        string assetPath = GetAssetPath(filename);
        SemaphoreSlim gate = GetEntryGate(assetPath);
        await gate.WaitAsync();
        try
        {
            byte[]? payload = await ReadVerifiedAssetAsync(assetPath);
            if (payload == null || !TryReadManifest(assetPath, out PersistentAssetCacheEntryManifest manifest))
            {
                return null;
            }

            return manifest.ETag;
        }
        finally
        {
            gate.Release();
        }
    }

    public bool HasAsset(string filename)
    {
        return GetAsset(filename) != null;
    }

    public void RemoveAsset(string filename)
    {
        string assetPath = GetAssetPath(filename);
        SemaphoreSlim gate = GetEntryGate(assetPath);
        gate.Wait();
        try
        {
            RemoveEntryFiles(assetPath);
        }
        finally
        {
            gate.Release();
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Private Helpers
    // ═══════════════════════════════════════════════════════════

    public string GetAssetPath(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            throw new ArgumentException("Asset filename cannot be empty.", nameof(filename));
        }

        var relative = filename.Replace('\\', '/').TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(_cachePath, relative));
        var cacheRoot = Path.GetFullPath(_cachePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(cacheRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Asset filename escapes the persistent cache directory.", nameof(filename));
        }

        return fullPath;
    }

    private SemaphoreSlim GetEntryGate(string assetPath) =>
        _entryGates.GetOrAdd(assetPath, _ => new SemaphoreSlim(1, 1));

    private static string GetManifestPath(string assetPath) => assetPath + ".entry";

    private static string GetLegacyETagPath(string assetPath) => assetPath + ".etag";

    private static byte[]? ReadVerifiedAsset(
        string assetPath,
        out PersistentAssetCacheEntryManifest manifest)
    {
        manifest = default;
        if (!File.Exists(assetPath) || !TryReadManifest(assetPath, out manifest))
        {
            RemoveEntryFiles(assetPath);
            return null;
        }

        byte[] payload = File.ReadAllBytes(assetPath);
        if (manifest.Matches(payload))
        {
            return payload;
        }

        RemoveEntryFiles(assetPath);
        manifest = default;
        return null;
    }

    private static async UniTask<byte[]?> ReadVerifiedAssetAsync(string assetPath)
    {
        if (!File.Exists(assetPath) || !TryReadManifest(assetPath, out PersistentAssetCacheEntryManifest manifest))
        {
            RemoveEntryFiles(assetPath);
            return null;
        }

        byte[] payload = await File.ReadAllBytesAsync(assetPath).AsUniTask();
        if (manifest.Matches(payload))
        {
            return payload;
        }

        RemoveEntryFiles(assetPath);
        return null;
    }

    private static bool TryReadManifest(
        string assetPath,
        out PersistentAssetCacheEntryManifest manifest)
    {
        string manifestPath = GetManifestPath(assetPath);
        if (!File.Exists(manifestPath))
        {
            manifest = default;
            return false;
        }

        return PersistentAssetCacheEntryManifest.TryParse(
            File.ReadAllText(manifestPath),
            out manifest);
    }

    private static void RemoveEntryFiles(string assetPath)
    {
        DeleteIfExists(assetPath);
        DeleteIfExists(GetManifestPath(assetPath));
        DeleteIfExists(GetLegacyETagPath(assetPath));
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void WriteAtomically(string path, byte[] data)
    {
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temporaryPath, data);
            ReplaceFile(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void WriteAtomically(string path, string value) =>
        WriteAtomically(path, Encoding.UTF8.GetBytes(value));

    private static async UniTask WriteAtomicallyAsync(string path, byte[] data)
    {
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, data).AsUniTask();
            ReplaceFile(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static UniTask WriteAtomicallyAsync(string path, string value) =>
        WriteAtomicallyAsync(path, Encoding.UTF8.GetBytes(value));

    private static void ReplaceFile(string temporaryPath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            File.Replace(temporaryPath, destinationPath, null);
            return;
        }

        File.Move(temporaryPath, destinationPath);
    }
}
