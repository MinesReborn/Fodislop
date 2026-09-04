#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.World
{
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

        private Color32[]? _atlasPixels;
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
            _textureResolver = textureResolver ?? throw new ArgumentNullException(nameof(textureResolver));
            _packer = new AtlasRectanglePacker(size, padding);

            _atlasTexture = RuntimeTextureFactory.CreateRgba32NoMip(
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
            if (!_isDirty || _atlasTexture == null)
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

            if (dirtyTextures.Count > 0 && RuntimeTextureFactory.SupportsTexture2DGpuCopy)
            {
                foreach (var (_, texture, rect) in dirtyTextures)
                {
                    UploadGpuTexture(texture, rect);
                }
            }
            else if (dirtyTextures.Count > 0)
            {
                EnsurePixelBuffer();
                foreach (var (_, texture, rect) in dirtyTextures)
                {
                    CopyPixelsToAtlasArray(texture.GetPixels32(), texture.width, texture.height, rect);
                }

                _atlasTexture.SetPixels32(_atlasPixels);
                _atlasTexture.Apply(false, false);
            }

            lock (_lock)
            {
                foreach (var (type, _, _) in dirtyTextures)
                {
                    _dirtyCells.Remove(type);
                }

                _isDirty = _dirtyCells.Count > 0;
            }
        }

        public async UniTask<Texture2D?> GetAtlasTexture()
        {
            if (_isDirty)
            {
                await UpdateAtlasTexture();
            }

            return _atlasTexture;
        }

        private void EnsurePixelBuffer()
        {
            _atlasPixels ??= new Color32[Size * Size];
        }

        public async UniTask UpdateAtlasTexture()
        {
            await UniTask.SwitchToMainThread();

            List<(Texture2D texture, Rectangle rect)> texturesToCopy;
            lock (_lock)
            {
                if (!_isDirty)
                {
                    return;
                }

                texturesToCopy = new List<(Texture2D texture, Rectangle rect)>();

                foreach (var cell in _cells.Values)
                {
                    var baseTexture = GetBaseTexture(cell.CellType);
                    if (baseTexture != null)
                    {
                        texturesToCopy.Add((baseTexture, cell.Rectangle));
                    }
                }
            }

            await CopyTexturesToAtlas(texturesToCopy);

            lock (_lock)
            {
                _dirtyCells.Clear();
                _isDirty = false;
            }
        }

        private async UniTask CopyTexturesToAtlas(List<(Texture2D texture, Rectangle rect)> textures)
        {
            if (RuntimeTextureFactory.SupportsTexture2DGpuCopy)
            {
                await UniTask.SwitchToMainThread();
                if (_atlasTexture != null)
                {
                    foreach (var (texture, rect) in textures)
                    {
                        UploadGpuTexture(texture, rect);
                    }
                }

                return;
            }

            const int BATCH_SIZE = 10;

            EnsurePixelBuffer();

            for (int i = 0; i < textures.Count; i += BATCH_SIZE)
            {
                int batchEnd = Math.Min(i + BATCH_SIZE, textures.Count);
                var pixelDataList = new List<(Color32[] pixels, int width, int height, Rectangle rect)>(batchEnd - i);

                for (int textureIndex = i; textureIndex < batchEnd; textureIndex++)
                {
                    var (tex, rect) = textures[textureIndex];
                    if (tex != null)
                    {
                        pixelDataList.Add((tex.GetPixels32(), tex.width, tex.height, rect));
                    }
                }

                await UniTask.SwitchToThreadPool();

                foreach (var data in pixelDataList)
                {
                    CopyPixelsToAtlasArray(data.pixels, data.width, data.height, data.rect);
                }

                await UniTask.SwitchToMainThread();
            }

            if (_atlasTexture != null && _atlasPixels != null)
            {
                _atlasTexture.SetPixels32(_atlasPixels);
                _atlasTexture.Apply();
            }
        }

        private void CopyPixelsToAtlasArray(Color32[] sourcePixels, int width, int height, Rectangle destination)
        {
            if (_atlasPixels == null)
            {
                throw new InvalidOperationException(
                    $"CPU pixel storage is unavailable for {Size}x{Size} atlas.");
            }

            if (sourcePixels.Length != checked(width * height))
            {
                throw new InvalidOperationException(
                    $"Source pixel count {sourcePixels.Length} does not match " +
                    $"the declared texture size {width}x{height}.");
            }

            if (width != destination.Width || height != destination.Height ||
                destination.X < 0 || destination.Y < 0 ||
                destination.X + width > Size || destination.Y + height > Size)
            {
                throw new InvalidOperationException(
                    $"Texture {width}x{height} cannot be copied into atlas rectangle " +
                    $"({destination.X}, {destination.Y}, " +
                    $"{destination.Width}, {destination.Height}) in {Size}x{Size} atlas.");
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int sourceIndex = (y * width) + x;
                    int destX = destination.X + x;
                    int destY = destination.Y + y;
                    int destIndex = (destY * Size) + destX;
                    _atlasPixels[destIndex] = sourcePixels[sourceIndex];
                }
            }
        }

        private void UploadGpuTexture(Texture2D source, Rectangle destination)
        {
            ValidateGpuCopySource(source, destination);
            Graphics.CopyTexture(
                source, 0, 0, 0, 0, source.width, source.height,
                _atlasTexture, 0, 0, destination.X, destination.Y);
        }

        private void ValidateGpuCopySource(Texture2D source, Rectangle destination)
        {
            Texture2D atlasTexture = _atlasTexture ??
                throw new ObjectDisposedException(
                    nameof(TextureAtlas),
                    "Cannot upload into a disposed terrain atlas.");
            if (source.width != destination.Width ||
                source.height != destination.Height)
            {
                throw new InvalidOperationException(
                    $"Terrain texture '{source.name}' is {source.width}x{source.height}, " +
                    $"but its reserved atlas rectangle is " +
                    $"{destination.Width}x{destination.Height}.");
            }

            if (source.graphicsFormat != atlasTexture.graphicsFormat)
            {
                throw new InvalidOperationException(
                    $"Terrain texture '{source.name}' uses GPU format " +
                    $"{source.graphicsFormat}, but atlas '{atlasTexture.name}' uses " +
                    $"{atlasTexture.graphicsFormat}. Runtime image decoding must " +
                    "canonicalize terrain textures before Graphics.CopyTexture.");
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
}
