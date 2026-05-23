using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Fodinae.Scripts.Networking.Connection.Client
{
    /// <summary>
    /// Manages texture loading from local storage with fallback to random generation.
    /// Supports both development (Unity Editor) and build environments.
    /// </summary>
    public class TextureStorageManager : MonoBehaviour
    {
        private static TextureStorageManager _instance;
        private static bool _isQuitting = false;
        public static TextureStorageManager Instance
        {
            get
            {
                if (_isQuitting) return null;
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<TextureStorageManager>();
                    if (_instance == null && !_isQuitting)
                    {
                        var go = new GameObject("[TextureStorageManager]");
                        _instance = go.AddComponent<TextureStorageManager>();

                        // System Grouping
                        var parent = GameObject.Find("[Systems]") ?? new GameObject("[Systems]");
                        UnityEngine.Object.DontDestroyOnLoad(parent);
                        go.transform.SetParent(parent.transform);
                    }
                }
                return _instance;
            }
        }

        [Header("Texture Storage Configuration")]
        [Tooltip("Enable debug logging for texture loading")]
        [SerializeField] private bool _enableDebugLogging = true;
        [Tooltip("Texture size for generated fallback textures")]
        [SerializeField] private int _fallbackTextureSize = 32;

        private readonly ConcurrentDictionary<string, byte[]> _textureCache = new();
        private string _textureFolderPath = string.Empty;
        private bool _folderInitialized = false;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            // Ensure parented if created in scene
            var parent = GameObject.Find("[Systems]") ?? new GameObject("[Systems]");
            UnityEngine.Object.DontDestroyOnLoad(parent);
            transform.SetParent(parent.transform);

            _isQuitting = false;
        }

        private void OnApplicationQuit()
        {
            _isQuitting = true;
        }

        /// <summary>
        /// Get texture data for a specific filename.
        /// First tries to load from local storage, falls back to random generation if not found.
        /// </summary>
        /// <param name="filename">The texture filename (e.g., "/cells/1.png")</param>
        /// <returns>Texture data as PNG bytes, or null if loading failed</returns>
        public async UniTask<byte[]> GetTextureData(string filename)
        {
            if (string.IsNullOrEmpty(filename))
            {
                Debug.LogError("[TextureStorageManager] Cannot load texture: filename is null or empty");
                return null;
            }

            // Normalize filename by removing leading slash if present
            var normalizedFilename = filename.StartsWith("/") ? filename.Substring(1) : filename;

            // Check cache first
            if (_textureCache.TryGetValue(normalizedFilename, out var cachedData))
            {
                if (_enableDebugLogging)
                    Debug.Log($"[TextureStorageManager] Cache hit for: {normalizedFilename}");
                return cachedData;
            }

            // Try to load from local storage
            var textureData = await LoadTextureFromStorage(normalizedFilename);
            
            if (textureData != null)
            {
                // Cache the loaded texture
                _textureCache.TryAdd(normalizedFilename, textureData);
                
                if (_enableDebugLogging)
                    Debug.Log($"[TextureStorageManager] Loaded texture from storage: {normalizedFilename}");
                
                return textureData;
            }

            // Fallback to random generation
            if (_enableDebugLogging)
                Debug.LogWarning($"[TextureStorageManager] Texture not found, generating fallback: {normalizedFilename}");

            var fallbackData = GenerateRandomTexture();
            if (fallbackData != null)
            {
                _textureCache.TryAdd(normalizedFilename, fallbackData);
            }

            return fallbackData;
        }

        /// <summary>
        /// Load texture from local storage (file system)
        /// </summary>
        /// <param name="filename">The texture filename</param>
        /// <returns>Texture data as PNG bytes, or null if not found</returns>
        private async UniTask<byte[]> LoadTextureFromStorage(string filename)
        {
            try
            {
                // Initialize folder path on first use
                if (!_folderInitialized)
                {
                    InitializeTextureFolderPath();
                }

                if (string.IsNullOrEmpty(_textureFolderPath) || !Directory.Exists(_textureFolderPath))
                {
                    if (_enableDebugLogging)
                        Debug.LogWarning($"[TextureStorageManager] Texture folder not found: {_textureFolderPath}");
                    return null;
                }

                var fullPath = Path.Combine(_textureFolderPath, filename);
                
                // Fuzzy search for the file if it doesn't have an extension or file not found
                if (!File.Exists(fullPath))
                {
                    string directory = Path.GetDirectoryName(fullPath);
                    string filenameWithoutExtension = Path.GetFileNameWithoutExtension(fullPath);

                    if (Directory.Exists(directory))
                    {
                        var files = Directory.GetFiles(directory, filenameWithoutExtension + ".*")
                            .Where(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                            .OrderBy(f => {
                                string ext = Path.GetExtension(f).ToLower();
                                if (ext == ".webp") return 0;
                                if (ext == ".gif") return 1;
                                if (ext == ".png") return 2;
                                return 3;
                            }).ToArray();

                        if (files.Length > 0)
                        {
                            fullPath = files[0];
                            if (_enableDebugLogging)
                                Debug.Log($"[TextureStorageManager] Fuzzy matched {filename} to {fullPath}");
                        }
                    }
                }

                if (!File.Exists(fullPath))
                {
                    if (_enableDebugLogging)
                        Debug.Log($"[TextureStorageManager] Texture file not found: {fullPath}");
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

        /// <summary>
        /// Generate a random texture as fallback
        /// </summary>
        /// <returns>Random texture data as PNG bytes</returns>
        private byte[] GenerateRandomTexture()
        {
            try
            {
                var texture = new Texture2D(_fallbackTextureSize, _fallbackTextureSize);
                
                // Generate random colors
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
        /// Initialize the texture folder path based on the current environment
        /// </summary>
        private void InitializeTextureFolderPath()
        {
            if (_folderInitialized) return;

            try
            {
                // Try different folder locations in order of preference
                var possiblePaths = new[]
                {
                    // Development: Assets/Textures/ (relative to project)
                    Path.Combine(Application.dataPath, "Textures"),
                    
                    // Build: ../Textures/ (relative to executable)
                    Path.Combine(Application.dataPath, "../Textures"),
                    
                    // Build: Textures/ (in same directory as executable)
                    Path.Combine(Application.dataPath, "Textures"),
                    
                    // Fallback: Persistent data path
                    Path.Combine(Application.persistentDataPath, "Textures")
                };

                foreach (var path in possiblePaths)
                {
                    if (Directory.Exists(path))
                    {
                        _textureFolderPath = path;
                        if (_enableDebugLogging)
                            Debug.Log($"[TextureStorageManager] Using texture folder: {_textureFolderPath}");
                        _folderInitialized = true;
                        return;
                    }
                }

                // If no existing folder found, try to create the preferred development folder
                var preferredPath = Path.Combine(Application.dataPath, "Textures");
                try
                {
                    Directory.CreateDirectory(preferredPath);
                    _textureFolderPath = preferredPath;
                    if (_enableDebugLogging)
                        Debug.Log($"[TextureStorageManager] Created texture folder: {_textureFolderPath}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[TextureStorageManager] Could not create texture folder: {ex.Message}");
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
        /// Clear the texture cache (useful for testing or memory management)
        /// </summary>
        public void ClearCache()
        {
            _textureCache.Clear();
            if (_enableDebugLogging)
                Debug.Log("[TextureStorageManager] Texture cache cleared");
        }

        /// <summary>
        /// Get current texture folder path
        /// </summary>
        /// <returns>Current texture folder path, or empty string if not initialized</returns>
        public string GetTextureFolderPath()
        {
            if (!_folderInitialized)
            {
                InitializeTextureFolderPath();
            }
            return _textureFolderPath;
        }

        /// <summary>
        /// Check if a texture file exists in local storage
        /// </summary>
        /// <param name="filename">The texture filename</param>
        /// <returns>True if the texture exists in local storage</returns>
        public bool HasTexture(string filename)
        {
            if (!_folderInitialized)
            {
                InitializeTextureFolderPath();
            }

            if (string.IsNullOrEmpty(_textureFolderPath) || !Directory.Exists(_textureFolderPath))
            {
                return false;
            }

            var normalizedFilename = filename.StartsWith("/") ? filename.Substring(1) : filename;
            var fullPath = Path.Combine(_textureFolderPath, normalizedFilename);
            
            return File.Exists(fullPath);
        }

        /// <summary>
        /// Get cache statistics
        /// </summary>
        /// <returns>Cache statistics string</returns>
        public string GetCacheStats()
        {
            return $"Texture Cache: {_textureCache.Count} entries, Folder: {_textureFolderPath}";
        }
    }
}