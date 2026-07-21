using System;
using System.Collections.Concurrent;
using System.Linq;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Scripts.World
{
    /// <summary>
    /// Manages caching of cell textures for efficient loading and memory management.
    /// </summary>
    public class CellTextureCache
    {
        private readonly ConcurrentDictionary<CellType, CellTextureInfo> _textureCache = new();
        private readonly ConcurrentDictionary<CellType, Texture2D> _loadedTextures = new();
        private readonly ConcurrentDictionary<string, CellType> _filenameCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Get the number of cached textures.
        /// </summary>
        public int CacheCount => _textureCache.Count;

        /// <summary>
        /// Add a texture to the cache.
        /// </summary>
        /// <param name="cellType">The cell type.</param>
        /// <param name="textureInfo">Texture information.</param>
        public void AddTexture(CellType cellType, CellTextureInfo textureInfo)
        {
            _textureCache.AddOrUpdate(cellType, textureInfo, (key, oldValue) => textureInfo);
            _loadedTextures.AddOrUpdate(cellType, textureInfo.BaseTexture, (key, oldValue) => textureInfo.BaseTexture);

            // Cache filename mapping
            var filename = $"Cells/{(int)cellType}";
            _filenameCache.TryAdd(filename, cellType);
        }

        /// <summary>
        /// Try to get texture information from cache.
        /// </summary>
        /// <param name="cellType">The cell type.</param>
        /// <param name="textureInfo">Output texture information.</param>
        /// <returns>True if found, false otherwise.</returns>
        public bool TryGetTexture(CellType cellType, out CellTextureInfo textureInfo)
        {
            return _textureCache.TryGetValue(cellType, out textureInfo);
        }

        /// <summary>
        /// Get a cached texture for a cell type.
        /// </summary>
        /// <param name="cellType">The cell type.</param>
        /// <returns>The cached texture or null if not found.</returns>
        public Texture2D GetCachedTexture(CellType cellType)
        {
            _loadedTextures.TryGetValue(cellType, out var texture);
            return texture;
        }

        /// <summary>
        /// Check if a texture is cached.
        /// </summary>
        /// <param name="cellType">The cell type.</param>
        /// <returns>True if cached, false otherwise.</returns>
        public bool IsCached(CellType cellType)
        {
            return _textureCache.ContainsKey(cellType);
        }

        /// <summary>
        /// Remove a texture from cache.
        /// </summary>
        /// <param name="cellType">The cell type.</param>
        public void RemoveTexture(CellType cellType)
        {
            _textureCache.TryRemove(cellType, out _);
            _loadedTextures.TryRemove(cellType, out _);

            var filename = $"Cells/{(int)cellType}";
            _filenameCache.TryRemove(filename, out _);
        }

        /// <summary>
        /// Clear all cached textures.
        /// </summary>
        public void Clear()
        {
            foreach (var texture in _loadedTextures.Values)
            {
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }

            _textureCache.Clear();
            _loadedTextures.Clear();
            _filenameCache.Clear();
        }

        /// <summary>
        /// Get all cached cell types.
        /// </summary>
        public CellType[] GetCachedCellTypes()
        {
            return _textureCache.Keys.ToArray();
        }

        /// <summary>
        /// Get texture info for a filename.
        /// </summary>
        /// <param name="filename">The texture filename.</param>
        /// <returns>The cell type if found, otherwise CellType.Unloaded.</returns>
        public CellType GetCellTypeFromFilename(string filename)
        {
            if (_filenameCache.TryGetValue(filename, out var cellType))
            {
                return cellType;
            }

            // Try to parse cell type from filename
            if (TryParseCellTypeFromFilename(filename, out cellType))
            {
                _filenameCache.TryAdd(filename, cellType);
                return cellType;
            }

            return CellType.Unloaded;
        }

        /// <summary>
        /// Get memory usage of cached textures.
        /// </summary>
        /// <returns>Approximate memory usage in bytes.</returns>
        public long GetMemoryUsage()
        {
            long totalSize = 0;

            foreach (var texture in _loadedTextures.Values)
            {
                if (texture != null)
                {
                    // Approximate texture memory usage (width * height * bytes per pixel)
                    totalSize += texture.width * texture.height * 4; // RGBA32 = 4 bytes per pixel
                }
            }

            return totalSize;
        }

        /// <summary>
        /// Get cache statistics.
        /// </summary>
        /// <returns>Cache statistics string.</returns>
        public string GetCacheStats()
        {
            return $"Cache: {_textureCache.Count} textures, {GetMemoryUsage() / 1024} KB";
        }

        /// <summary>
        /// Try to parse cell type from filename.
        /// </summary>
        /// <param name="filename">The filename to parse.</param>
        /// <param name="cellType">Output cell type.</param>
        /// <returns>True if successfully parsed, false otherwise.</returns>
        private static bool TryParseCellTypeFromFilename(string filename, out CellType cellType)
        {
            cellType = CellType.Unloaded;

            try
            {
                // Extract cell ID from filename like "Cells/50"
                if (filename.StartsWith("Cells/", StringComparison.OrdinalIgnoreCase) || filename.StartsWith("cells/", StringComparison.OrdinalIgnoreCase))
                {
                    string idStr = filename.Substring(6);

                    // Remove extension if present (though we shouldn't have one now)
                    int dotIndex = idStr.LastIndexOf('.');
                    if (dotIndex > 0)
                    {
                        idStr = idStr.Substring(0, dotIndex);
                    }

                    if (int.TryParse(idStr, out int cellId))
                    {
                        if (Enum.IsDefined(typeof(CellType), cellId))
                        {
                            cellType = (CellType)cellId;
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            return false;
        }
    }
}
