#nullable enable

using System;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using UnityEngine;
using UnityEngine.Networking;

namespace Kern.Core;
// Контракт IRuntimeAssetPaths находится в Kern.Contracts, а реализация
// живёт рядом с потребителями asset pipeline.
public sealed class RuntimeAssetPaths : IRuntimeAssetPaths
{
    private const string TexturesFolderName = "Textures";

    private string? _bundledTexturesRoot;
    private readonly string? _bundledRootOverride;
    private readonly string? _persistentRootOverride;

    public RuntimeAssetPaths()
    {
    }

    public RuntimeAssetPaths(string bundledRoot, string persistentRoot)
    {
        _bundledRootOverride = bundledRoot ?? throw new ArgumentNullException(nameof(bundledRoot));
        _persistentRootOverride = persistentRoot ?? throw new ArgumentNullException(nameof(persistentRoot));
    }

    public async UniTask EnsureReadyAsync()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        string extractedRoot = Path.Combine(
            Application.persistentDataPath,
            "BundledAssets",
            TexturesFolderName);
        string markerPath = Path.Combine(extractedRoot, ".manifest");
        string manifest = await DownloadTextAsync(
            CombineStreamingURI(Application.streamingAssetsPath, "Textures.manifest"));
        if (!File.Exists(markerPath) ||
            !string.Equals(await File.ReadAllTextAsync(markerPath), manifest, System.StringComparison.Ordinal))
        {
            if (Directory.Exists(extractedRoot))
            {
                Directory.Delete(extractedRoot, recursive: true);
            }

            Directory.CreateDirectory(extractedRoot);
            string[] relativeFiles = manifest
                .Split(['\r', '\n'], System.StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Trim())
                .Where(path => path.Length > 0)
                .ToArray();
            foreach (string relativeFile in relativeFiles)
            {
                string destination = Path.Combine(
                    extractedRoot,
                    relativeFile.Replace('/', Path.DirectorySeparatorChar));
                string? directory = Path.GetDirectoryName(destination);
                if (directory == null)
                {
                    throw new InvalidDataException(
                        $"Bundled texture has no destination directory: {relativeFile}");
                }

                Directory.CreateDirectory(directory);
                byte[] bytes = await DownloadBytesAsync(
                    CombineStreamingURI(
                        Application.streamingAssetsPath,
                        $"Textures/{relativeFile}"));
                await File.WriteAllBytesAsync(destination, bytes);
            }

            await File.WriteAllTextAsync(markerPath, manifest);
        }

        _bundledTexturesRoot = extractedRoot;
#else
        await UniTask.CompletedTask;
#endif
    }

    public string BundledTexturesRoot
    {
        get
        {
            if (_bundledTexturesRoot != null)
            {
                return _bundledTexturesRoot;
            }

            _bundledTexturesRoot = ResolveBundledRoot();
            return _bundledTexturesRoot;
        }
    }

    public string PersistentTexturesRoot
    {
        get
        {
            string path = _persistentRootOverride ??
                Path.Combine(Application.persistentDataPath, TexturesFolderName);
            Directory.CreateDirectory(path);
            return Path.GetFullPath(path);
        }
    }

    public string? FindBundledTextureFile(string relativePath) =>
        FindCaseInsensitive(BundledTexturesRoot, NormalizeRelativePath(relativePath));

    public string? FindTextureFile(string relativePath)
    {
        string normalized = NormalizeRelativePath(relativePath);
        return FindCaseInsensitive(PersistentTexturesRoot, normalized) ??
            FindCaseInsensitive(BundledTexturesRoot, normalized);
    }

    private string ResolveBundledRoot()
    {
        string candidate = _bundledRootOverride ?? (Application.isEditor
            ? Path.Combine(Application.dataPath, TexturesFolderName)
            : Path.Combine(Application.streamingAssetsPath, TexturesFolderName));
        if (!Directory.Exists(candidate))
        {
            throw new DirectoryNotFoundException($"Required texture directory is missing: {candidate}");
        }

        return Path.GetFullPath(candidate);
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("Texture path must be a non-empty relative path.", nameof(relativePath));
        }

        string[] segments = relativePath.Replace('\\', '/').Split('/');
        if (segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment) ||
                segment == "." ||
                segment == ".."))
        {
            throw new ArgumentException($"Texture path contains an invalid segment: '{relativePath}'.", nameof(relativePath));
        }

        return string.Join("/", segments);
    }

    private static string? FindCaseInsensitive(string root, string relativePath)
    {
        string current = root;
        string[] segments = relativePath.Split('/');
        for (int index = 0; index < segments.Length; index++)
        {
            string segment = segments[index];
            string exact = Path.Combine(current, segment);
            if (!Directory.Exists(current))
            {
                return null;
            }

            bool isLeaf = index == segments.Length - 1;
            // Do not trust File.Exists/Directory.Exists for the fast path:
            // macOS commonly uses a case-insensitive volume and would then
            // return the caller's spelling instead of the canonical on-disk
            // spelling. Returning the actual directory entry keeps persistent
            // and bundled paths stable across platforms.
            string? match = Directory.EnumerateFileSystemEntries(current)
                .FirstOrDefault(entry => string.Equals(
                    Path.GetFileName(entry),
                    segment,
                    StringComparison.OrdinalIgnoreCase));
            if (match == null && (File.Exists(exact) || Directory.Exists(exact)))
            {
                match = exact;
            }
            if (match == null && isLeaf && string.IsNullOrEmpty(Path.GetExtension(segment)))
            {
                match = Directory.EnumerateFiles(current)
                    .Where(path => string.Equals(
                        Path.GetFileNameWithoutExtension(path),
                        segment,
                        StringComparison.OrdinalIgnoreCase))
                    .Where(path => TextureFilePriority(path) != int.MaxValue)
                    .OrderBy(TextureFilePriority)
                    .FirstOrDefault();
            }

            if (match == null)
            {
                return null;
            }

            current = match;
        }

        return File.Exists(current) ? current : null;
    }

    private static int TextureFilePriority(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => 0,
            ".jpg" or ".jpeg" => 1,
            ".exr" => 2,
            _ => int.MaxValue,
        };
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static async UniTask<string> DownloadTextAsync(string uri)
    {
        byte[] bytes = await DownloadBytesAsync(uri);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static async UniTask<byte[]> DownloadBytesAsync(string uri)
    {
        using UnityWebRequest request = UnityWebRequest.Get(uri);
        await request.SendWebRequest().ToUniTask();
        if (request.result != UnityWebRequest.Result.Success)
        {
            throw new IOException(
                $"Failed to extract required bundled asset '{uri}': {request.error}");
        }

        return request.downloadHandler.data;
    }

    private static string CombineStreamingURI(string root, string relativePath)
    {
        string encodedPath = string.Join(
            "/",
            relativePath.Split('/').Select(System.Uri.EscapeDataString));
        return $"{root.TrimEnd('/')}/{encodedPath}";
    }
#endif
}
