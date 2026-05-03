using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using MinesServer.Data;
using Fodinae.Assets.Scripts.Game.Managers;

namespace Fodinae.Assets.Scripts.World
{
    public class TextureAtlas
    {
        public int Size { get; }
        public int CellSize { get; }
        public int Padding { get; }

        private Texture2D _atlasTexture;
        private Color32[] _atlasPixels;
        private readonly ConcurrentDictionary<CellType, AtlasCell> _cells = new();
        private readonly List<Rectangle> _freeRectangles = new();
        private readonly List<Rectangle> _usedRectangles = new();

        private bool _isDirty = false;
        private readonly object _lock = new object();

        public TextureAtlas(int size, int cellSize, int padding)
        {
            Size = size;
            CellSize = cellSize;
            Padding = padding;

            _atlasTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            _atlasTexture.filterMode = FilterMode.Point;
            _atlasTexture.wrapMode = TextureWrapMode.Clamp;
            _atlasPixels = new Color32[size * size];

            for (int i = 0; i < _atlasPixels.Length; i++)
            {
                _atlasPixels[i] = new Color32(0, 0, 0, 0);
            }

            _atlasTexture.SetPixels32(_atlasPixels);
            _atlasTexture.Apply();

            _freeRectangles.Add(new Rectangle(0, 0, size, size));
        }

        public void Clear()
        {
            lock (_lock)
            {
                _cells.Clear();
                _usedRectangles.Clear();
                _freeRectangles.Clear();
                _freeRectangles.Add(new Rectangle(0, 0, Size, Size));

                for (int i = 0; i < _atlasPixels.Length; i++)
                {
                    _atlasPixels[i] = new Color32(0, 0, 0, 0);
                }
                _atlasTexture.SetPixels32(_atlasPixels);
                _atlasTexture.Apply();

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

            // The cell's sub-atlas is packed at cell.Rectangle.X, cell.Rectangle.Y
            int subAtlasX = cell.Rectangle.X;
            int subAtlasY = cell.Rectangle.Y;
            int subAtlasWidth = cell.Rectangle.Width;
            int subAtlasHeight = cell.Rectangle.Height;

            // Use 32x32 as the tile size for terrain rendering
            const int terrainTileSize = 32;

            // How many 32x32 tiles fit in the SUB-ATLAS width and height
            int tilesPerRow = subAtlasWidth / terrainTileSize;

            // If frameHeightPixels is provided, it defines the wrapping boundary for animations
            int effectiveSubAtlasHeight = frameHeightPixels > 0 ? frameHeightPixels : subAtlasHeight;
            int tilesPerColumn = effectiveSubAtlasHeight / terrainTileSize;

            if (tilesPerRow <= 0) tilesPerRow = 1;
            if (tilesPerColumn <= 0) tilesPerColumn = 1;

            // Calculate wrapped position within the SUB-ATLAS (or frame)
            int wrappedX = ((globalX % tilesPerRow) + tilesPerRow) % tilesPerRow;
            // Invert Y wrapping for bottom-to-top texture sampling with top-to-bottom server coords
            int wrappedY = (tilesPerColumn - 1) - (((globalY % tilesPerColumn) + tilesPerColumn) % tilesPerColumn);

            // Calculate the absolute atlas position by adding the sub-atlas base position
            int atlasX = subAtlasX + (wrappedX * terrainTileSize);
            // Add frame offset: subAtlasY + wrapped cell offset + current frame offset
            int atlasY = subAtlasY + (wrappedY * terrainTileSize) + (frameIndex * (frameHeightPixels > 0 ? frameHeightPixels : 0));

            return new AtlasCoordinate(
                atlasX,
                atlasY,
                terrainTileSize,  // We only want to render one 32x32 tile
                terrainTileSize,
                Size,             // Full atlas width
                Size              // Full atlas height
            );
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
                // Pack the ENTIRE texture size. Do not restrict to 16x16.
                var bestFit = FindBestFit(texture.width, texture.height);
                if (bestFit == null) return false;

                var atlasCell = new AtlasCell
                {
                    CellType = cellType,
                    Rectangle = bestFit.Value,
                    BaseCoordinate = new AtlasCoordinate(
                        bestFit.Value.X, bestFit.Value.Y,
                        texture.width, texture.height,
                        Size, Size)
                };

                _usedRectangles.Add(bestFit.Value);
                SplitFreeRectangles(bestFit.Value);
                _cells.TryAdd(cellType, atlasCell);
                _isDirty = true;

                coordinate = atlasCell.BaseCoordinate;
                return true;
            }
        }

        public async UniTask<Texture2D> GetAtlasTexture()
        {
            if (_isDirty) await UpdateAtlasTexture();
            return _atlasTexture;
        }

        public async UniTask UpdateAtlasTexture()
        {
            await UniTask.SwitchToMainThread();

            if (!_isDirty) return;

            List<(Texture2D texture, Rectangle rect)> texturesToCopy;

            lock (_lock)
            {
                if (!_isDirty) return;

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
                _isDirty = false;
            }
        }

        private async UniTask CopyTexturesToAtlas(List<(Texture2D texture, Rectangle rect)> textures)
        {
            const int batchSize = 10;

            for (int i = 0; i < textures.Count; i += batchSize)
            {
                var batch = textures.Skip(i).Take(batchSize).ToList();

                var pixelDataList = new List<(Color32[] pixels, int width, int height, Rectangle rect)>();

                foreach (var (tex, rect) in batch)
                {
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

            _atlasTexture.SetPixels32(_atlasPixels);
            _atlasTexture.Apply();
        }

        private void CopyPixelsToAtlasArray(Color32[] sourcePixels, int width, int height, Rectangle destination)
        {
            // Copy the ENTIRE texture size
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int sourceIndex = y * width + x;
                    int destX = destination.X + x;
                    int destY = destination.Y + y;
                    int destIndex = destY * Size + destX;

                    if (destIndex >= 0 && destIndex < _atlasPixels.Length && sourceIndex < sourcePixels.Length)
                    {
                        _atlasPixels[destIndex] = sourcePixels[sourceIndex];
                    }
                }
            }
        }

        private Texture2D GetBaseTexture(CellType cellType)
        {
            if (WorldTextureManager.Instance != null)
            {
                var cachedTexture = WorldTextureManager.Instance.GetCachedTexture(cellType);
                if (cachedTexture != null)
                {
                    return cachedTexture;
                }
            }
            return CreatePlaceholderTexture(cellType);
        }

        private Texture2D CreatePlaceholderTexture(CellType cellType)
        {
            var texture = new Texture2D(CellSize, CellSize);
            var color = GetCellColor(cellType);
            var pixels = new Color[CellSize * CellSize];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private Color GetCellColor(CellType cellType)
        {
            if (MapManager.Instance != null)
            {
                var serverColor = MapManager.Instance.GetCellMinimapColor(cellType);
                if (serverColor.a > 0) return serverColor;
            }
            return cellType switch
            {
                CellType.Empty => new Color(0.2f, 0.2f, 0.2f),
                CellType.Road => new Color(0.8f, 0.8f, 0.8f),
                CellType.Boulder1 => Color.black,
                CellType.WhiteSand => new Color(1f, 0.92f, 0.8f),
                CellType.GrayAcid => new Color(0f, 1f, 0f),
                _ => Color.magenta
            };
        }

        private Rectangle? FindBestFit(int width, int height)
        {
            Rectangle? bestFit = null;
            int bestScore = int.MaxValue;
            foreach (var freeRect in _freeRectangles)
            {
                if (freeRect.Width >= width + Padding && freeRect.Height >= height + Padding)
                {
                    int score = (freeRect.Width - width) * (freeRect.Height - height);
                    if (score < bestScore) { bestScore = score; bestFit = new Rectangle(freeRect.X, freeRect.Y, width, height); }
                }
            }
            return bestFit;
        }

        private void SplitFreeRectangles(Rectangle usedRect)
        {
            var newFree = new List<Rectangle>();
            foreach (var free in _freeRectangles)
            {
                if (Intersects(free, usedRect)) SplitRectangle(free, usedRect, newFree);
                else newFree.Add(free);
            }
            _freeRectangles.Clear();
            _freeRectangles.AddRange(newFree);
        }

        private void SplitRectangle(Rectangle free, Rectangle used, List<Rectangle> newFree)
        {
            if (used.Y > free.Y) newFree.Add(new Rectangle(free.X, free.Y, free.Width, used.Y - free.Y));
            if (used.Y + used.Height < free.Y + free.Height) newFree.Add(new Rectangle(free.X, used.Y + used.Height, free.Width, (free.Y + free.Height) - (used.Y + used.Height)));
            if (used.X > free.X) newFree.Add(new Rectangle(free.X, free.Y, used.X - free.X, free.Height));
            if (used.X + used.Width < free.X + free.Width) newFree.Add(new Rectangle(used.X + used.Width, free.Y, (free.X + free.Width) - (used.X + used.Width), free.Height));
        }

        private bool Intersects(Rectangle a, Rectangle b) => a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;
    }

    public struct Rectangle
    {
        public int X, Y, Width, Height;
        public Rectangle(int x, int y, int width, int height) { X = x; Y = y; Width = width; Height = height; }
    }

    internal struct AtlasCell
    {
        public CellType CellType;
        public Rectangle Rectangle;
        public AtlasCoordinate BaseCoordinate;
    }
}