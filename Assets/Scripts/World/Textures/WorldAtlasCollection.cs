#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Fodinae.Core.Interfaces;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.World.Textures;

/// <summary>
/// Manages the set of dynamic texture atlases, including expansion and cell texture packing.
/// </summary>
public sealed class WorldAtlasCollection : IDisposable
{
    private readonly int _initialAtlasSize;
    private readonly int _maxAtlasSize;
    private readonly int _cellTextureSize;
    private readonly int _texturePadding;
    private readonly Func<CellType, Texture2D?> _cachedTextureLookup;
    private readonly List<TextureAtlas> _atlases = new();
    private readonly List<IAtlasDescriptor> _descriptorCache = new();
    private TextureAtlas _currentAtlas = null!;

    public TextureAtlas CurrentAtlas => _currentAtlas;

    public IReadOnlyList<TextureAtlas> Atlases => _atlases;

    public WorldAtlasCollection(
        int initialAtlasSize,
        int maxAtlasSize,
        int cellTextureSize,
        int texturePadding,
        Func<CellType, Texture2D?> cachedTextureLookup)
    {
        _initialAtlasSize = initialAtlasSize;
        _maxAtlasSize = maxAtlasSize;
        _cellTextureSize = cellTextureSize;
        _texturePadding = texturePadding;
        _cachedTextureLookup = cachedTextureLookup;

        Reset();
    }

    public void Reset()
    {
        Dispose();
        _currentAtlas = new TextureAtlas(
            _initialAtlasSize,
            _cellTextureSize,
            _texturePadding,
            _cachedTextureLookup);
        _atlases.Add(_currentAtlas);
        _descriptorCache.Clear();
    }

    public void Dispose()
    {
        foreach (TextureAtlas atlas in _atlases)
        {
            atlas?.Dispose();
        }

        _atlases.Clear();
        _descriptorCache.Clear();
    }

    public bool ContainsCell(CellType cellType)
    {
        for (int i = 0; i < _atlases.Count; i++)
        {
            if (_atlases[i].ContainsCell(cellType))
            {
                return true;
            }
        }

        return false;
    }

    public TextureAtlas? GetAtlasForCell(CellType cellType)
    {
        for (int i = 0; i < _atlases.Count; i++)
        {
            if (_atlases[i].ContainsCell(cellType))
            {
                return _atlases[i];
            }
        }

        return null;
    }

    public AtlasCoordinate GetWrappedCoordinate(
        CellType cellType,
        int globalX,
        int globalY,
        CellVariation variation,
        int frameHeight,
        int frameIndex)
    {
        for (int i = 0; i < _atlases.Count; i++)
        {
            if (_atlases[i].ContainsCell(cellType))
            {
                return _atlases[i].GetWrappedCoordinate(cellType, globalX, globalY, variation, frameHeight, frameIndex);
            }
        }

        return AtlasCoordinate.Empty;
    }

    public IReadOnlyList<IAtlasDescriptor> GetAllAtlases()
    {
        if (_descriptorCache.Count != _atlases.Count)
        {
            _descriptorCache.Clear();
            for (int i = 0; i < _atlases.Count; i++)
            {
                _descriptorCache.Add(_atlases[i]);
            }
        }

        return _descriptorCache;
    }

    public void FlushDirtyAtlases()
    {
        for (int i = 0; i < _atlases.Count; i++)
        {
            if (_atlases[i].IsDirty)
            {
                _atlases[i].SyncApply();
            }
        }
    }

    public void AddTexture(CellType cellType, Texture2D texture)
    {
        if (ContainsCell(cellType))
        {
            return;
        }

        if (!_currentAtlas.TryAddTexture(cellType, texture, out _))
        {
            int newSize = Mathf.Min(_currentAtlas.Size * 2, _maxAtlasSize);
            if (newSize > _currentAtlas.Size)
            {
                var newAtlas = new TextureAtlas(
                    newSize,
                    _cellTextureSize,
                    _texturePadding,
                    _cachedTextureLookup);
                _atlases.Add(newAtlas);
                _currentAtlas = newAtlas;

                if (!_currentAtlas.TryAddTexture(cellType, texture, out _))
                {
                    throw new InvalidOperationException(
                        $"Failed to add terrain texture for cell type {cellType} to new atlas of size {newSize}.");
                }
            }
            else
            {
                throw new InvalidOperationException(
                    $"Terrain texture atlas size limit reached ({_maxAtlasSize}) while adding cell type {cellType}.");
            }
        }

        _currentAtlas.CopyTextureToAtlas(cellType, texture);
    }

    public void ValidateDimensions(CellType cellType, Texture2D texture, int frameHeight)
    {
        if (texture.width <= 0 || texture.height <= 0 ||
            texture.width % _cellTextureSize != 0 ||
            texture.height % _cellTextureSize != 0)
        {
            throw new InvalidDataException(
                $"Terrain texture for {cellType} has invalid dimensions " +
                $"{texture.width}x{texture.height}; both dimensions must be positive " +
                $"multiples of {_cellTextureSize} pixels.");
        }

        if (frameHeight == 0)
        {
            return;
        }

        if (frameHeight % _cellTextureSize != 0 || texture.height % frameHeight != 0)
        {
            throw new InvalidDataException(
                $"Terrain texture for {cellType} has height {texture.height} and " +
                $"frame height {frameHeight}; both must align to " +
                $"{_cellTextureSize}-pixel cells and frames must divide the atlas exactly.");
        }
    }
}
