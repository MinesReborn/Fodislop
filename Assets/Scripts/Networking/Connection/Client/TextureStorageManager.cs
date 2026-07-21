using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using Fodinae.Scripts.Core;
using UnityEngine;

namespace Fodinae.Scripts.Networking.Connection.Client
{
    /// <summary>
    /// Manager for storing and caching textures downloaded from the server or loaded locally.
    /// Provides thread-safe async access with in-memory caching and fallback texture generation.
    /// Writes downloaded assets to persistentDataPath to prevent Unity AssetDatabase reloads in Editor.
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Gracefully catch file and texture loading exceptions to fall back to random texture generation.")]
    public class TextureStorageManager : SingletonMonoBehaviour<TextureStorageManager>
    {
        [SerializeField]
        private bool _enableDebugLogging = false;

        [SerializeField]
        private int _fallbackTextureSize = 64;

        private readonly ConcurrentDictionary<string, Texture2D> _textureCache = new();
        private readonly ConcurrentDictionary<string, string> _resolvedPathsCache = new();

        private string _textureFolderPath;
        private bool _folderInitialized;

        /// <summary>
        /// Get a texture by filename asynchronously.
        /// </summary>
        /// <param name="filename">The texture filename (e.g. "cells/1.png", "clan/4.png").</param>
        /// <returns>Loaded Texture2D, or fallback texture if loading failed.</returns>
        public async UniTask<Texture2D> GetTextureAsync(string filename)
        {
            if (string.IsNullOrEmpty(filename))
            {
                if (_enableDebugLogging)
                {
                    Debug.LogWarning("[TextureStorageManager] Requested texture with null or empty filename");
                }

                return CreateFallbackTexture("empty");
            }

            // Return cached texture if available
            if (_textureCache.TryGetValue(filename, out var cachedTexture) && cachedTexture != null)
            {
                return cachedTexture;
            }

            // Try to load from disk
            var rawData = await LoadTextureFromStorage(filename);

            if (rawData != null && rawData.Length > 0)
            {
                try
                {
                    var texture = new Texture2D(2, 2);
                    if (texture.LoadImage(rawData))
                    {
                        texture.name = filename;
                        _textureCache.TryAdd(filename, texture);
                        return texture;
                    }
                    else
                    {
                        UnityEngine.Object.Destroy(texture);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[TextureStorageManager] Failed to decode texture '{filename}': {ex.Message}");
                }
            }

            // Fallback: generate fallback texture
            if (_enableDebugLogging)
            {
                Debug.Log($"[TextureStorageManager] Texture not found, generating fallback: {filename}");
            }

            var fallback = CreateFallbackTexture(filename);
            _textureCache.TryAdd(filename, fallback);
            return fallback;
        }

        /// <summary>
        /// Get a texture by filename synchronously.
        /// </summary>
        public Texture2D GetTexture(string filename)
        {
            return GetTextureAsync(filename).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Get raw texture bytes asynchronously by filename.
        /// </summary>
        /// <param name="filename">The texture filename.</param>
        /// <returns>PNG/WEBP bytes, or null if not found.</returns>
        public async UniTask<byte[]> GetTextureData(string filename)
        {
            return await LoadTextureFromStorage(filename);
        }

        /// <summary>
        /// Create a fallback texture and cache it.
        /// </summary>
        private Texture2D CreateFallbackTexture(string filename)
        {
            var rawData = GenerateRandomTexture();
            var texture = new Texture2D(_fallbackTextureSize, _fallbackTextureSize);

            if (rawData != null && texture.LoadImage(rawData))
            {
                texture.name = $"Fallback_{filename}";
                return texture;
            }

            return texture;
        }

        /// <summary>
        /// Load texture file bytes from storage asynchronously.
        /// Searches persistentDataPath first (dynamic downloads), then bundled Assets/Textures (read-only).
        /// </summary>
        private async UniTask<byte[]> LoadTextureFromStorage(string filename)
        {
            try
            {
                if (!_folderInitialized)
                {
                    InitializeTextureFolderPath();
                }

                if (!_resolvedPathsCache.TryGetValue(filename, out var fullPath))
                {
                    fullPath = ResolveTextureFullPath(filename);
                    if (!string.IsNullOrEmpty(fullPath))
                    {
                        _resolvedPathsCache.TryAdd(filename, fullPath);
                    }
                }

                if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
                {
                    if (_enableDebugLogging)
                    {
                        Debug.Log($"[TextureStorageManager] Texture file not found: {filename}");
                    }

                    return null;
                }

                // Load file asynchronously
                using var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
                var buffer = new byte[fileStream.Length];
                await fileStream.ReadAsync(buffer, 0, buffer.Length);

                return buffer;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TextureStorageManager] Failed to load texture from storage: {ex.Message}");
                return null;
            }
        }

        private string ResolveTextureFullPath(string filename)
        {
            var normalizedFilename = filename.TrimStart('/');

            // 1. Search persistentDataPath/Textures (dynamic downloads)
            if (!string.IsNullOrEmpty(_textureFolderPath) && Directory.Exists(_textureFolderPath))
            {
                var foundPath = SearchInFolder(_textureFolderPath, normalizedFilename);
                if (foundPath != null)
                {
                    return foundPath;
                }
            }

            // 2. Read-only search in bundled Assets/Textures
            var bundledFolder = Path.Combine(Application.dataPath, "Textures");
            if (Directory.Exists(bundledFolder))
            {
                var foundPath = SearchInFolder(bundledFolder, normalizedFilename);
                if (foundPath != null)
                {
                    return foundPath;
                }
            }

            return null;
        }

        private static string SearchInFolder(string baseFolder, string filename)
        {
            var fullPath = Path.Combine(baseFolder, filename);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }

            string directory = Path.GetDirectoryName(fullPath);
            string filenameWithoutExtension = Path.GetFileNameWithoutExtension(fullPath);

            if (Directory.Exists(directory))
            {
                var files = Directory.GetFiles(directory, filenameWithoutExtension + ".*")
                    .Where(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f =>
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext == ".webp")
                        {
                            return 0;
                        }

                        if (ext == ".gif")
                        {
                            return 1;
                        }

                        if (ext == ".png")
                        {
                            return 2;
                        }

                        return 3;
                    }).ToArray();

                if (files.Length > 0)
                {
                    return files[0];
                }
            }

            return null;
        }

        /// <summary>
        /// Generate a random texture as fallback.
        /// </summary>
        private byte[] GenerateRandomTexture()
        {
            try
            {
                var texture = new Texture2D(_fallbackTextureSize, _fallbackTextureSize);

                var colors = new Color[_fallbackTextureSize * _fallbackTextureSize];
                for (int i = 0; i < colors.Length; i++)
                {
                    colors[i] = new Color(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value);
                }

                texture.SetPixels(colors);
                texture.Apply();

                var pngData = ImageConversion.EncodeToPNG(texture);
                UnityEngine.Object.Destroy(texture);

                return pngData;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TextureStorageManager] Failed to generate random texture: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Initialize the texture folder path for dynamic runtime downloads.
        /// </summary>
        private void InitializeTextureFolderPath()
        {
            if (_folderInitialized)
            {
                return;
            }

            try
            {
                var persistentPath = Path.Combine(Application.persistentDataPath, "Textures");
                if (!Directory.Exists(persistentPath))
                {
                    Directory.CreateDirectory(persistentPath);
                }

                _textureFolderPath = persistentPath;
                if (_enableDebugLogging)
                {
                    Debug.Log($"[TextureStorageManager] Using texture folder: {_textureFolderPath}");
                }

                _folderInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TextureStorageManager] Failed to initialize texture folder: {ex.Message}");
                _folderInitialized = true;
            }
        }

        /// <summary>
        /// Clear the texture cache.
        /// </summary>
        public void ClearCache()
        {
            _textureCache.Clear();
            _resolvedPathsCache.Clear();
            if (_enableDebugLogging)
            {
                Debug.Log("[TextureStorageManager] Texture cache cleared");
            }
        }

        public string GetTextureFolderPath()
        {
            if (!_folderInitialized)
            {
                InitializeTextureFolderPath();
            }

            return _textureFolderPath;
        }

        public bool HasTexture(string filename)
        {
            if (!_folderInitialized)
            {
                InitializeTextureFolderPath();
            }

            var path = ResolveTextureFullPath(filename);
            return !string.IsNullOrEmpty(path) && File.Exists(path);
        }

        public string GetCacheStats()
        {
            return $"Texture Cache: {_textureCache.Count} entries, Folder: {_textureFolderPath}";
        }
    }
}
