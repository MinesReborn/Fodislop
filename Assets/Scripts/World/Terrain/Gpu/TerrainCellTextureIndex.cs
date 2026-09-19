#nullable enable

using System.Collections.Generic;
using MinesServer.Data;

namespace Kern.World.Terrain;

internal sealed class TerrainCellTextureIndex
{
    private readonly Dictionary<CellType, HashSet<long>> _backgroundQuadsByType = [];
    private readonly Dictionary<CellType, HashSet<long>> _foregroundQuadsByType = [];
    private readonly Dictionary<long, CellType> _backgroundTypeByCoordinate = [];
    private readonly Dictionary<long, CellType> _foregroundTypeByCoordinate = [];
    private readonly List<int> _textureRefreshQuads = [];
    private readonly HashSet<long> _textureRefreshMarks = [];
    private int _textureRefreshWindowX;
    private int _textureRefreshWindowY;

    public List<int> TextureRefreshQuads => _textureRefreshQuads;

    public void Clear()
    {
        _backgroundQuadsByType.Clear();
        _foregroundQuadsByType.Clear();
        _backgroundTypeByCoordinate.Clear();
        _foregroundTypeByCoordinate.Clear();
        _textureRefreshQuads.Clear();
        _textureRefreshMarks.Clear();
    }

    public void Rebuild(TerrainCellSources sources, int minX, int minY, int width, int height)
    {
        _backgroundQuadsByType.Clear();
        _foregroundQuadsByType.Clear();
        _backgroundTypeByCoordinate.Clear();
        _foregroundTypeByCoordinate.Clear();
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                UpdateTextureIndex(
                    _backgroundQuadsByType,
                    PackCoordinate(minX + x, minY + y),
                    sources.FloodFill.Buffer[x, y],
                    _backgroundTypeByCoordinate);
                UpdateTextureIndex(
                    _foregroundQuadsByType,
                    PackCoordinate(minX + x, minY + y),
                    sources.CellCache.GetCellData(x + 1, y + 1).Type,
                    _foregroundTypeByCoordinate);
            }
        }
    }

    public void UpdateCell(int gridX, int unityY, CellType backgroundType, CellType foregroundType)
    {
        long key = PackCoordinate(gridX, unityY);
        UpdateTextureIndex(_backgroundQuadsByType, key, backgroundType, _backgroundTypeByCoordinate);
        UpdateTextureIndex(_foregroundQuadsByType, key, foregroundType, _foregroundTypeByCoordinate);
    }

    public void CollectRefreshQuads(HashSet<CellType> cellTypes, int minX, int minY, int width, int height)
    {
        _textureRefreshQuads.Clear();
        _textureRefreshMarks.Clear();
        _textureRefreshWindowX = minX;
        _textureRefreshWindowY = minY;
        foreach (CellType cellType in cellTypes)
        {
            AddTextureRefreshQuads(_backgroundQuadsByType, cellType, width, height);
            AddTextureRefreshQuads(_foregroundQuadsByType, cellType, width, height);
        }

        _textureRefreshQuads.Sort();
    }

    public void RemoveScrolledOutCells(int minX, int minY, int dx, int dy, int width, int height)
    {
        int oldMinX = minX - dx;
        int oldMinY = minY - dy;
        if (dx > 0)
        {
            RemoveTextureIndexRect(oldMinX, oldMinX + dx, oldMinY, oldMinY + height);
        }
        else if (dx < 0)
        {
            RemoveTextureIndexRect(minX + width, oldMinX + width, oldMinY, oldMinY + height);
        }

        if (dy > 0)
        {
            RemoveTextureIndexRect(minX, minX + width, oldMinY, oldMinY + dy);
        }
        else if (dy < 0)
        {
            RemoveTextureIndexRect(minX, minX + width, minY + height, oldMinY + height);
        }
    }

    private void AddTextureRefreshQuads(
        Dictionary<CellType, HashSet<long>> quadsByType,
        CellType cellType,
        int width,
        int height)
    {
        if (!quadsByType.TryGetValue(cellType, out HashSet<long>? quads))
        {
            return;
        }

        foreach (long key in quads)
        {
            int worldX = UnpackX(key);
            int worldY = UnpackY(key);
            if ((uint)(worldX - _textureRefreshWindowX) >= (uint)width ||
                (uint)(worldY - _textureRefreshWindowY) >= (uint)height)
            {
                continue;
            }

            int quad = ((worldX - _textureRefreshWindowX) * height) + (worldY - _textureRefreshWindowY);
            if (_textureRefreshMarks.Add(key))
            {
                _textureRefreshQuads.Add(quad);
            }
        }
    }

    private void RemoveTextureIndexRect(int startX, int endX, int startY, int endY)
    {
        for (int x = startX; x < endX; x++)
        {
            for (int y = startY; y < endY; y++)
            {
                long key = PackCoordinate(x, y);
                RemoveTextureIndexKey(_backgroundQuadsByType, _backgroundTypeByCoordinate, key);
                RemoveTextureIndexKey(_foregroundQuadsByType, _foregroundTypeByCoordinate, key);
            }
        }
    }

    private static void UpdateTextureIndex(
        Dictionary<CellType, HashSet<long>> quadsByType,
        long key,
        CellType type,
        Dictionary<long, CellType> typeByCoordinate)
    {
        if (typeByCoordinate.Remove(key, out CellType previousType) &&
            quadsByType.TryGetValue(previousType, out HashSet<long>? previousQuads))
        {
            previousQuads.Remove(key);
            if (previousQuads.Count == 0)
            {
                quadsByType.Remove(previousType);
            }
        }

        if (!quadsByType.TryGetValue(type, out HashSet<long>? currentQuads))
        {
            currentQuads = [];
            quadsByType.Add(type, currentQuads);
        }

        currentQuads.Add(key);
        typeByCoordinate[key] = type;
    }

    private static void RemoveTextureIndexKey(
        Dictionary<CellType, HashSet<long>> quadsByType,
        Dictionary<long, CellType> typeByCoordinate,
        long key)
    {
        if (!typeByCoordinate.Remove(key, out CellType previousType) ||
            !quadsByType.TryGetValue(previousType, out HashSet<long>? previousQuads))
        {
            return;
        }

        previousQuads.Remove(key);
        if (previousQuads.Count == 0)
        {
            quadsByType.Remove(previousType);
        }
    }

    public static long PackCoordinate(int x, int y) => ((long)x << 32) | (uint)y;

    public static int UnpackX(long key) => (int)(key >> 32);

    public static int UnpackY(long key) => (int)key;
}
