#nullable enable

using Kern;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using MinesServer.Data;
using UnityEngine;
using Kern.Core.Interfaces.Diagnostics;

namespace Kern.World;
internal struct AtlasCell
{
    public CellType CellType;
    public Rectangle Rectangle;
    public AtlasCoordinate BaseCoordinate;
}

public class TextureAtlas : IDisposable, IAtlasDescriptor
{
    public int Size { get; }
    public int CELL_SIZE { get; }
    public int Padding { get; }

    private Texture2D? _atlasTexture;

    public Texture2D? Texture => _atlasTexture;

    private readonly AtlasPixelTransfer _pixelTransfer;
    private readonly ConcurrentDictionary<CellType, AtlasCell> _cells = new();
    private readonly AtlasRectanglePacker _packer;
    private readonly HashSet<CellType> _dirtyCells = new();
    private readonly Func<CellType, Texture2D?> _textureResolver;

    private bool _isDirty = false;

    public bool IsDirty => _isDirty;

    private readonly object _lock = new object();

    public TextureAtlas(int size, int cellSize, int padding, Func<CellType, Texture2D?> textureResolver)
    {
        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Atlas size must be positive.");
        }

        if (cellSize <= 0 || size < cellSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cellSize),
                cellSize,
                $"Atlas cell size must be positive and fit inside the atlas ({size}).");
        }

        if (padding < 0 || padding >= size)
        {
            throw new ArgumentOutOfRangeException(
                nameof(padding),
                padding,
                $"Atlas padding must be in the range [0, {size - 1}].");
        }

        Size = size;
        CELL_SIZE = cellSize;
        Padding = padding;
        _pixelTransfer = new AtlasPixelTransfer(size, padding);
        _textureResolver = textureResolver ?? throw new ArgumentNullException(nameof(textureResolver));
        _packer = new AtlasRectanglePacker(size, padding);

        _atlasTexture = RuntimeTextureFactory.CreateRGBA32NoMip(
            size,
            size,
            $"TerrainAtlas_{size}",
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp);

        // Initialize atlas texture with transparent black so padding between
        // cells and unused atlas regions never sample uninitialized GPU VRAM.
        _atlasTexture.SetPixels32(new Color32[size * size]);
        _atlasTexture.Apply(false, false);
        FrameEventLog.Record($"атлас {size}×{size} создан");
    }

    public void Dispose()
    {
        _cells.Clear();
        _dirtyCells.Clear();

        if (_atlasTexture != null)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(_atlasTexture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(_atlasTexture);
            }

            _atlasTexture = null;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _cells.Clear();
            _dirtyCells.Clear();
            _fullyOpaque.Clear();
            _packer.Clear();
            _isDirty = false;
        }
    }

    public AtlasCoordinate GetCoordinate(CellType cellType, CellVariation variation)
    {
        if (!_cells.TryGetValue(cellType, out var cell))
        {
            return AtlasCoordinate.Empty;
        }

        return cell.BaseCoordinate;
    }

    public AtlasCoordinate GetCoordinate(CellType cellType)
    {
        return GetCoordinate(cellType, CellVariation.None);
    }

    public bool ContainsCell(CellType cellType)
    {
        return _cells.ContainsKey(cellType);
    }

    private readonly ConcurrentDictionary<CellType, bool> _fullyOpaque = new();

    public bool IsFullyOpaque(CellType cellType) =>
        _fullyOpaque.TryGetValue(cellType, out bool opaque) && opaque;

    public void SetFullyOpaque(CellType cellType, bool opaque) => _fullyOpaque[cellType] = opaque;

    // Статические декодированные текстуры сохраняют признак непрозрачности
    // до освобождения CPU-копии. Для остальных читаемых текстур проверяем
    // альфу здесь без синхронного чтения с GPU.
    public static bool MeasureFullyOpaque(Texture2D texture)
    {
        const byte OpaqueAlpha = 250;
        // RuntimeTextureFactory records alpha before releasing the CPU copy.
        // On Metal, cell textures are normally GPU-only by the time they reach
        // this atlas; treating every one as translucent retains a full
        // background layer under otherwise opaque terrain.
        if (RuntimeTextureFactory.TryGetDecodedOpacity(texture, out bool decodedOpaque))
        {
            return decodedOpaque;
        }

        if (!texture.isReadable)
        {
            return false;
        }

        // RGBA32 читается прямо из буфера текстуры, без копии: GetPixels32
        // выделял массив на всю текстуру при каждом приезде типа.
        if (texture.format == TextureFormat.RGBA32)
        {
            Unity.Collections.NativeArray<Color32> pixels = texture.GetPixelData<Color32>(0);
            for (int index = 0; index < pixels.Length; index++)
            {
                if (pixels[index].a < OpaqueAlpha)
                {
                    return false;
                }
            }

            return true;
        }

        foreach (Color32 pixel in texture.GetPixels32())
        {
            if (pixel.a < OpaqueAlpha)
            {
                return false;
            }
        }

        return true;
    }

    public AtlasCoordinate GetWrappedCoordinate(CellType cellType, int globalX, int globalY, CellVariation variation, int frameHeightPixels = 0, int frameIndex = 0)
    {
        if (!_cells.TryGetValue(cellType, out var cell))
        {
            return AtlasCoordinate.Empty;
        }

        int subAtlasX = cell.Rectangle.X;
        int subAtlasY = cell.Rectangle.Y;
        int subAtlasWidth = cell.Rectangle.Width;
        int subAtlasHeight = cell.Rectangle.Height;

        const int TERRAIN_TILE_SIZE = RenderingConstants.CELL_SIZE;
        int tilesPerRow = subAtlasWidth / TERRAIN_TILE_SIZE;
        int effectiveSubAtlasHeight = frameHeightPixels > 0 ? frameHeightPixels : subAtlasHeight;
        int tilesPerColumn = effectiveSubAtlasHeight / TERRAIN_TILE_SIZE;

        if (tilesPerRow <= 0)
        {
            throw new InvalidOperationException(
                $"Atlas cell {cellType} has invalid width {subAtlasWidth} for terrain tile size {TERRAIN_TILE_SIZE}.");
        }

        if (tilesPerColumn <= 0)
        {
            throw new InvalidOperationException(
                $"Atlas cell {cellType} has invalid height {effectiveSubAtlasHeight} for terrain tile size {TERRAIN_TILE_SIZE}.");
        }

        int wrappedX = ((globalX % tilesPerRow) + tilesPerRow) % tilesPerRow;
        int wrappedY = (tilesPerColumn - 1) - (((globalY % tilesPerColumn) + tilesPerColumn) % tilesPerColumn);

        int atlasX = subAtlasX + (wrappedX * TERRAIN_TILE_SIZE);
        int atlasY = subAtlasY + (wrappedY * TERRAIN_TILE_SIZE) + (frameIndex * (frameHeightPixels > 0 ? frameHeightPixels : 0));

        return new AtlasCoordinate(
            atlasX,
            atlasY,
            TERRAIN_TILE_SIZE,
            TERRAIN_TILE_SIZE,
            Size,
            Size);
    }

    public AtlasCoordinate GetWrappedCoordinate(CellType cellType, int globalX, int globalY)
    {
        return GetWrappedCoordinate(cellType, globalX, globalY, CellVariation.None);
    }

    public bool TryAddTexture(CellType cellType, Texture2D texture, out AtlasCoordinate coordinate)
    {
        coordinate = AtlasCoordinate.Empty;

        lock (_lock)
        {
            if (_cells.TryGetValue(cellType, out var existingCell))
            {
                coordinate = existingCell.BaseCoordinate;
                return true;
            }

            if (!_packer.TryAllocate(texture.width, texture.height, out var bestFit))
            {
                return false;
            }

            var atlasCell = new AtlasCell
            {
                CellType = cellType,
                Rectangle = bestFit,
                BaseCoordinate = new AtlasCoordinate(
                    bestFit.X,
                    bestFit.Y,
                    texture.width,
                    texture.height,
                    Size,
                    Size),
            };

            _cells.TryAdd(cellType, atlasCell);
            _dirtyCells.Add(cellType);
            _isDirty = true;

            coordinate = atlasCell.BaseCoordinate;
            return true;
        }
    }

    public void CopyTextureToAtlas(CellType cellType, Texture2D texture)
    {
        if (!_cells.TryGetValue(cellType, out var cell))
        {
            throw new InvalidOperationException(
                $"Cell type {cellType} has no reserved atlas rectangle. " +
                "TryAddTexture must succeed before the texture is copied.");
        }

        lock (_lock)
        {
            _dirtyCells.Add(cellType);
            _isDirty = true;
        }
    }

    public void SyncApply()
    {
        Texture2D? atlasTexture = _atlasTexture;
        if (!_isDirty || atlasTexture == null)
        {
            return;
        }

        List<(CellType type, Texture2D texture, Rectangle rect)> dirtyTextures = new();
        lock (_lock)
        {
            foreach (CellType type in _dirtyCells)
            {
                if (_cells.TryGetValue(type, out AtlasCell cell))
                {
                    Texture2D? texture = GetBaseTexture(type);
                    if (texture != null)
                    {
                        dirtyTextures.Add((type, texture, cell.Rectangle));
                    }
                }
            }
        }

        _pixelTransfer.Apply(atlasTexture, dirtyTextures);

        lock (_lock)
        {
            foreach (var (type, _, _) in dirtyTextures)
            {
                _dirtyCells.Remove(type);
            }

            _isDirty = _dirtyCells.Count > 0;
        }
    }

    private Texture2D GetBaseTexture(CellType cellType)
    {
        Texture2D? cachedTexture = _textureResolver(cellType);
        if (cachedTexture != null)
        {
            return cachedTexture;
        }

        throw new InvalidOperationException(
            $"No texture has been loaded for cell type '{cellType}'. " +
            "Refusing to render a placeholder texture.");
    }
}
