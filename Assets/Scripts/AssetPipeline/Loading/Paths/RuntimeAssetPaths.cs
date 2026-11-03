#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using UnityEngine;
using UnityEngine.Networking;

namespace Kern.AssetPipeline.Loading;

public sealed class RuntimeAssetPaths : IRuntimeAssetPaths
{
    private const string EmbeddedTexturesFolderName = "EmbeddedTextures";
    private const string EmbeddedTextureManifestFilename = "EmbeddedTextures.manifest";
    private const string DummyWorldTexturesFolderName = "DummyWorldTextures";
    private const string DummyWorldTextureManifestFilename = "DummyWorldTextures.manifest";
    private const string AudioFolderName = "Audio";
    private const string AudioManifestFilename = "Audio.manifest";
    private readonly string? _textureRootOverride;
#if UNITY_EDITOR
    private readonly Lazy<TextureDeliveryCatalogData> _textureDeliveryCatalog = new(() =>
        TextureDeliveryCatalog.Read(GetTextureDeliveryCatalogPath()));
#endif
    private IReadOnlyDictionary<string, byte[]> _textureBytes =
        new Dictionary<string, byte[]>(StringComparer.Ordinal);
    private IReadOnlyList<string> _embeddedTextureFilenames = Array.Empty<string>();
    private IReadOnlyList<string> _dummyWorldTextureFilenames = Array.Empty<string>();
    private IReadOnlyList<string> _audioBankFilenames = Array.Empty<string>();
    private readonly SemaphoreSlim _ensureReadyGate = new(1, 1);
    private bool _isReady;
    private bool _audioBanksReady;

    public RuntimeAssetPaths()
    {
    }

    public RuntimeAssetPaths(string textureRoot)
    {
        _textureRootOverride = textureRoot ?? throw new ArgumentNullException(nameof(textureRoot));
    }

    public async UniTask EnsureReadyAsync()
    {
        await _ensureReadyGate.WaitAsync();
        try
        {
            if (_isReady)
            {
                return;
            }

            await LoadAssetsAsync();
            _isReady = true;
        }
        finally
        {
            _ensureReadyGate.Release();
        }
    }

    private async UniTask LoadAssetsAsync()
    {
        Dictionary<string, byte[]> loadedTextures = new(StringComparer.Ordinal);
        IReadOnlyList<string> embeddedFilenames;
        IReadOnlyList<string> dummyWorldFilenames;

        if (_textureRootOverride != null)
        {
            string[] filenames = Directory.EnumerateFiles(
                    _textureRootOverride,
                    "*",
                    SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(_textureRootOverride, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            await LoadFilesAsync(
                filenames,
                filename => Path.Combine(
                    _textureRootOverride,
                    filename.Replace('/', Path.DirectorySeparatorChar)),
                loadedTextures);
            embeddedFilenames = filenames;
            dummyWorldFilenames = Array.Empty<string>();
        }
#if UNITY_EDITOR
        else
        {
            string source = Path.Combine(Application.dataPath, "Textures");
            embeddedFilenames = await LoadTextureGroupFromEditorAsync(
                source,
                _textureDeliveryCatalog.Value.Embedded,
                loadedTextures);
            dummyWorldFilenames = await LoadTextureGroupFromEditorAsync(
                source,
                _textureDeliveryCatalog.Value.DummyWorld,
                loadedTextures);
        }
#else
        else
        {
            embeddedFilenames = await LoadStreamingTextureGroupAsync(
                EmbeddedTextureManifestFilename,
                EmbeddedTexturesFolderName,
                loadedTextures);
            dummyWorldFilenames = await LoadStreamingTextureGroupAsync(
                DummyWorldTextureManifestFilename,
                DummyWorldTexturesFolderName,
                loadedTextures);
        }
#endif

        if (embeddedFilenames.Count == 0 && dummyWorldFilenames.Count == 0)
        {
            throw new InvalidDataException("The runtime texture manifest contains no textures.");
        }

        var allFilenames = new HashSet<string>(embeddedFilenames, StringComparer.OrdinalIgnoreCase);
        foreach (string filename in dummyWorldFilenames)
        {
            if (!allFilenames.Add(filename))
            {
                throw new InvalidDataException(
                    $"Texture path '{filename}' appears in both local texture groups.");
            }
        }

        _textureBytes = AddExtensionlessAliases(loadedTextures);
        _embeddedTextureFilenames = embeddedFilenames;
        _dummyWorldTextureFilenames = dummyWorldFilenames;
    }

    public IReadOnlyList<string> GetEmbeddedTextureFilenames() => _embeddedTextureFilenames;

    public IReadOnlyList<string> GetDummyWorldTextureFilenames() => _dummyWorldTextureFilenames;

#if UNITY_EDITOR
    private static string GetTextureDeliveryCatalogPath() => Path.Combine(
        Application.dataPath,
        TextureDeliveryCatalog.AssetPath.Substring("Assets/".Length));

    private static async UniTask<IReadOnlyList<string>> LoadTextureGroupFromEditorAsync(
        string source,
        IReadOnlyDictionary<string, string> sourceToPublished,
        Dictionary<string, byte[]> loadedTextures)
    {
        IReadOnlyDictionary<string, string> sourceByPublished = sourceToPublished
            .ToDictionary(entry => entry.Value, entry => entry.Key, StringComparer.Ordinal);
        string[] filenames = sourceByPublished.Keys
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        await LoadFilesAsync(
            filenames,
            filename => Path.Combine(
                source,
                sourceByPublished[filename].Replace('/', Path.DirectorySeparatorChar)),
            loadedTextures);
        return filenames;
    }
#endif

    private static async UniTask<IReadOnlyList<string>> LoadStreamingTextureGroupAsync(
        string manifestFilename,
        string folderName,
        Dictionary<string, byte[]> loadedTextures)
    {
        string[] manifestLines = await ReadStreamingTextAsync(manifestFilename);
        var filenameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filenames = new List<string>(manifestLines.Length);
        foreach (string line in manifestLines)
        {
            string relativePath = line.Trim();
            if (relativePath.Length == 0)
            {
                continue;
            }

            string filename = ValidateRelativePath(relativePath);
            if (!filenameSet.Add(filename))
            {
                throw new InvalidDataException(
                    $"Runtime texture manifest '{manifestFilename}' contains duplicate path '{filename}'.");
            }

            filenames.Add(filename);
        }

        filenames.Sort(StringComparer.Ordinal);
        foreach (string filename in filenames)
        {
            byte[] bytes = await ReadStreamingBytesAsync($"{folderName}/{filename}");
            if (!loadedTextures.TryAdd(filename, bytes))
            {
                throw new InvalidDataException(
                    $"Texture path '{filename}' appears in more than one runtime texture manifest.");
            }
        }

        return filenames;
    }

    public async UniTask<IReadOnlyList<string>> GetAudioBankFilenamesAsync()
    {
        await EnsureReadyAsync();
        await _ensureReadyGate.WaitAsync();
        try
        {
            if (!_audioBanksReady)
            {
                _audioBankFilenames = await LoadAudioBankFilenamesAsync();
                _audioBanksReady = true;
            }

            return _audioBankFilenames;
        }
        finally
        {
            _ensureReadyGate.Release();
        }
    }

    public byte[]? GetTextureBytes(string relativePath)
    {
        string validatedPath = ValidateRelativePath(relativePath);
        return _textureBytes.TryGetValue(validatedPath, out byte[]? bytes) ? bytes : null;
    }

    public async UniTask<byte[]?> GetAudioBankBytesAsync(string relativePath)
    {
        string validatedPath = ValidateRelativePath(relativePath);
        if (!validatedPath.StartsWith($"{AudioFolderName}/", StringComparison.Ordinal) ||
            Path.GetExtension(validatedPath) != ".bank" ||
            !(await GetAudioBankFilenamesAsync()).Contains(validatedPath, StringComparer.Ordinal))
        {
            return null;
        }

        return await ReadStreamingBytesAsync(validatedPath);
    }

    private static async UniTask<IReadOnlyList<string>> LoadAudioBankFilenamesAsync()
    {
#if UNITY_EDITOR
        string audioRoot = Path.Combine(Application.streamingAssetsPath, AudioFolderName);
        if (!Directory.Exists(audioRoot))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(audioRoot, "*.bank", SearchOption.AllDirectories)
            .Select(path =>
                $"{AudioFolderName}/{Path.GetRelativePath(audioRoot, path).Replace('\\', '/')}")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
#else
        string manifestPath = $"{AudioFolderName}/{AudioManifestFilename}";
        string[] manifestLines;
        try
        {
            manifestLines = await ReadStreamingTextAsync(manifestPath);
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }

        return manifestLines
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(ValidateRelativePath)
            .Where(filename => Path.GetExtension(filename) == ".bank")
            .Select(filename => $"{AudioFolderName}/{filename}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(filename => filename, StringComparer.Ordinal)
            .ToArray();
#endif
    }

    private static async UniTask LoadFilesAsync(
        IReadOnlyList<string> filenames,
        Func<string, string> resolvePath,
        Dictionary<string, byte[]> loadedTextures)
    {
        foreach (string filename in filenames.OrderBy(path => path, StringComparer.Ordinal))
        {
            string path = resolvePath(filename);
            loadedTextures.Add(filename, await UniTask.RunOnThreadPool(() => File.ReadAllBytes(path)));
        }
    }

    private static IReadOnlyDictionary<string, byte[]> AddExtensionlessAliases(
        Dictionary<string, byte[]> textures)
    {
        var withAliases = new Dictionary<string, byte[]>(textures, StringComparer.Ordinal);
        var usedPaths = new HashSet<string>(textures.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, byte[]> texture in textures.OrderBy(
                     pair => pair.Key,
                     StringComparer.Ordinal))
        {
            string extension = Path.GetExtension(texture.Key);
            if (extension.Length > 0)
            {
                string alias = texture.Key[..^extension.Length];
                if (!usedPaths.Add(alias) || !withAliases.TryAdd(alias, texture.Value))
                {
                    throw new InvalidDataException(
                        $"Runtime textures share the extensionless alias '{alias}'.");
                }
            }
        }

        return withAliases;
    }

    private static string ValidateRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathRooted(relativePath) ||
            relativePath.Contains('\\'))
        {
            throw new ArgumentException(
                "Texture path must be a non-empty relative path using forward slashes.",
                nameof(relativePath));
        }

        string[] segments = relativePath.Split('/');
        if (segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment) ||
                segment is "." or ".."))
        {
            throw new ArgumentException(
                $"Texture path contains an invalid segment: '{relativePath}'.",
                nameof(relativePath));
        }

        return relativePath;
    }

    private static async UniTask<string[]> ReadStreamingTextAsync(string relativePath)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        byte[] bytes = await ReadStreamingBytesAsync(relativePath);
        return System.Text.Encoding.UTF8.GetString(bytes).Split('\n');
#else
        string path = Path.Combine(
            Application.streamingAssetsPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        return await UniTask.RunOnThreadPool(() => File.ReadAllLines(path));
#endif
    }

    private static async UniTask<byte[]> ReadStreamingBytesAsync(string relativePath)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        string uri = CombineStreamingUri(Application.streamingAssetsPath, relativePath);
        using UnityWebRequest request = UnityWebRequest.Get(uri);
        await request.SendWebRequest().ToUniTask();
        if (request.result != UnityWebRequest.Result.Success)
        {
            throw new IOException($"Failed to load runtime texture '{relativePath}': {request.error}");
        }

        return request.downloadHandler.data;
#else
        string path = Path.Combine(
            Application.streamingAssetsPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        return await UniTask.RunOnThreadPool(() => File.ReadAllBytes(path));
#endif
    }

    private static string CombineStreamingUri(string root, string relativePath)
    {
        string encodedPath = string.Join(
            "/",
            relativePath.Split('/').Select(Uri.EscapeDataString));
        return $"{root.TrimEnd('/')}/{encodedPath}";
    }
}
