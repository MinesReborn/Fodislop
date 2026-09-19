#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World;

public class CellTextureCache
{
    private readonly ConcurrentDictionary<CellType, CellTextureInfo> _textureCache = new();
    private readonly ConcurrentDictionary<CellType, Texture2D> _loadedTextures = new();

    /// <param name="cellType">The cell type.</param>
    /// <param name="textureInfo">Texture information.</param>
    public void AddTexture(CellType cellType, CellTextureInfo textureInfo)
    {
        if (_textureCache.TryGetValue(cellType, out CellTextureInfo previous) &&
            previous.OwnsBaseTexture &&
            previous.BaseTexture != textureInfo.BaseTexture)
        {
            DestroyTexture(previous.BaseTexture);
        }

        _textureCache.AddOrUpdate(cellType, textureInfo, (key, oldValue) => textureInfo);
        _loadedTextures.AddOrUpdate(cellType, textureInfo.BaseTexture, (key, oldValue) => textureInfo.BaseTexture);
    }

    /// <param name="cellType">The cell type.</param>
    /// <param name="textureInfo">Output texture information.</param>
    /// <returns>True if found, false otherwise.</returns>
    public bool TryGetTexture(CellType cellType, out CellTextureInfo textureInfo) =>
        _textureCache.TryGetValue(cellType, out textureInfo);

    /// <param name="cellType">The cell type.</param>
    /// <returns>The cached texture or null if not found.</returns>
    public Texture2D? GetCachedTexture(CellType cellType) =>
        _loadedTextures.TryGetValue(cellType, out var texture) ? texture : null;

    public void Clear()
    {
        HashSet<Texture2D> ownedTextures = [];
        foreach (CellTextureInfo textureInfo in _textureCache.Values)
        {
            if (textureInfo.OwnsBaseTexture && textureInfo.BaseTexture != null)
            {
                ownedTextures.Add(textureInfo.BaseTexture);
            }
        }

        _textureCache.Clear();
        _loadedTextures.Clear();
        foreach (Texture2D texture in ownedTextures)
        {
            DestroyTexture(texture);
        }
    }

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

    /// <returns>Cache statistics string.</returns>
    public string GetCacheStats() =>
        $"Cache: {_textureCache.Count} textures, {GetMemoryUsage() / 1024} KB";

    private static void DestroyTexture(Texture2D texture)
    {
        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(texture);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
