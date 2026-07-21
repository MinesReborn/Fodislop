using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using UnityEngine;

namespace Fodinae.Scripts
{
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Gracefully handle any I/O failure without crashing the asset loading pipeline.")]
    public static class PersistentAssetCache
    {
        private static string _cachePath;
        private static bool _isInitialized = false;

        static PersistentAssetCache()
        {
            InitializeCachePath();
        }

        // ═══════════════════════════════════════════════════════════
        //  Public API
        // ═══════════════════════════════════════════════════════════

        public static byte[] GetAsset(string filename)
        {
            try
            {
                // Ensure cache is initialized
                InitializeCachePath();

                if (string.IsNullOrEmpty(filename))
                {
                    Debug.LogError("[PersistentAssetCache] Cannot get asset: filename is null or empty");
                    return null;
                }

                var assetPath = GetAssetPath(filename);
                if (File.Exists(assetPath))
                {
                    return File.ReadAllBytes(assetPath);
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PersistentAssetCache] Failed to get asset '{filename}': {ex.Message}");
                return null;
            }
        }

        public static void SaveAsset(string filename, byte[] data, string etag)
        {
            try
            {
                // Ensure cache is initialized
                InitializeCachePath();

                if (string.IsNullOrEmpty(filename))
                {
                    Debug.LogError("[PersistentAssetCache] Cannot save asset: filename is null or empty");
                    return;
                }

                if (data == null || data.Length == 0)
                {
                    Debug.LogError($"[PersistentAssetCache] Cannot save asset '{filename}': data is null or empty");
                    return;
                }

                var assetPath = GetAssetPath(filename);
                var etagPath = GetETagPath(filename);

                // Ensure the directory exists
                var directory = Path.GetDirectoryName(assetPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(assetPath, data);
                File.WriteAllText(etagPath, etag ?? string.Empty);

                Debug.Log($"[PersistentAssetCache] Successfully saved asset: {filename}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PersistentAssetCache] Failed to save asset '{filename}': {ex.Message}");
                throw;
            }
        }

        public static string GetETag(string filename)
        {
            try
            {
                // Ensure cache is initialized
                InitializeCachePath();

                if (string.IsNullOrEmpty(filename))
                {
                    Debug.LogError("[PersistentAssetCache] Cannot get ETag: filename is null or empty");
                    return null;
                }

                var etagPath = GetETagPath(filename);
                if (File.Exists(etagPath))
                {
                    return File.ReadAllText(etagPath);
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PersistentAssetCache] Failed to get ETag for '{filename}': {ex.Message}");
                return null;
            }
        }

        public static bool HasAsset(string filename)
        {
            try
            {
                // Ensure cache is initialized
                InitializeCachePath();

                if (string.IsNullOrEmpty(filename))
                {
                    Debug.LogError("[PersistentAssetCache] Cannot check asset existence: filename is null or empty");
                    return false;
                }

                return File.Exists(GetAssetPath(filename));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PersistentAssetCache] Failed to check asset existence for '{filename}': {ex.Message}");
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  Private Helpers
        // ═══════════════════════════════════════════════════════════

        private static void InitializeCachePath()
        {
            if (_isInitialized)
            {
                return;
            }

            try
            {
                // Validate that Application.persistentDataPath is valid
                var persistentPath = Application.persistentDataPath;

                // Check if the persistent data path is valid and not empty
                if (string.IsNullOrEmpty(persistentPath) || !Directory.Exists(Path.GetDirectoryName(persistentPath)))
                {
                    Debug.LogWarning($"[PersistentAssetCache] Invalid persistent data path: '{persistentPath}'. Falling back to application data directory.");

                    // Fallback to a safe directory
                    persistentPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Fodinae", "AssetCache");
                }

                _cachePath = Path.Combine(persistentPath, "AssetCache");

                // Ensure the cache directory exists
                if (!Directory.Exists(_cachePath))
                {
                    try
                    {
                        Directory.CreateDirectory(_cachePath);
                        Debug.Log($"[PersistentAssetCache] Created cache directory: {_cachePath}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[PersistentAssetCache] Failed to create cache directory '{_cachePath}': {ex.Message}");

                        // Fallback to temp directory if we can't create the cache directory
                        _cachePath = Path.Combine(Path.GetTempPath(), "FodinaeAssetCache");
                        if (!Directory.Exists(_cachePath))
                        {
                            Directory.CreateDirectory(_cachePath);
                            Debug.Log($"[PersistentAssetCache] Using fallback temp directory: {_cachePath}");
                        }
                    }
                }

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PersistentAssetCache] Failed to initialize cache path: {ex.Message}");

                // Emergency fallback
                _cachePath = Path.Combine(Path.GetTempPath(), "FodinaeAssetCache");
                if (!Directory.Exists(_cachePath))
                {
                    Directory.CreateDirectory(_cachePath);
                }

                _isInitialized = true;
            }
        }

        public static string GetAssetPath(string filename) => Path.Combine(_cachePath, filename.TrimStart('/'));

        private static string GetETagPath(string filename) => Path.Combine(_cachePath, filename.TrimStart('/') + ".etag");
    }
}
