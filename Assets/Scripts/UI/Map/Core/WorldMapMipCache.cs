#nullable enable

using System;
using MinesServer.Data;
using UnityEngine;

namespace Kern.UI;

internal sealed class WorldMapMipCache
{
    private const long MaxCacheBytes = 64L * 1024L * 1024L;

    private readonly Color32[][] _levels;
    private readonly int[] _levelWidths;
    private readonly int[] _levelHeights;
    private readonly Color32[] _cellColorTable;
    private readonly Color32 _unloadedColor;
    private readonly int _chunkSize;

    private int _currentChunkIndex = -1;
    private long _redSum;
    private long _greenSum;
    private long _blueSum;
    private long _alphaSum;
    private int _cellCount;

    public WorldMapMipCache(
        int widthChunks,
        int heightChunks,
        int chunkSize,
        Color32[] cellColorTable,
        Color32 unloadedColor)
    {
        if (widthChunks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthChunks));
        }

        if (heightChunks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(heightChunks));
        }

        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize));
        }

        if (cellColorTable == null || cellColorTable.Length < 256)
        {
            throw new ArgumentException(
                "The map color table must contain all 256 cell entries.",
                nameof(cellColorTable));
        }

        _chunkSize = chunkSize;
        _cellColorTable = cellColorTable;
        _unloadedColor = unloadedColor;
        int levelCount = 1;
        int levelWidth = widthChunks;
        int levelHeight = heightChunks;
        long totalPixels = (long)levelWidth * levelHeight;
        while (levelWidth > 1 || levelHeight > 1)
        {
            levelWidth = Mathf.Max(1, (levelWidth + 1) / 2);
            levelHeight = Mathf.Max(1, (levelHeight + 1) / 2);
            totalPixels += (long)levelWidth * levelHeight;
            levelCount++;
        }

        long cacheBytes = totalPixels * sizeof(uint);
        if (cacheBytes > MaxCacheBytes)
        {
            throw new InvalidOperationException(
                $"World map mip cache needs {cacheBytes / (1024f * 1024f):F1} MiB; " +
                $"the per-session limit is {MaxCacheBytes / (1024f * 1024f):F0} MiB.");
        }

        _levels = new Color32[levelCount][];
        _levelWidths = new int[levelCount];
        _levelHeights = new int[levelCount];
        levelWidth = widthChunks;
        levelHeight = heightChunks;
        for (int level = 0; level < levelCount; level++)
        {
            _levelWidths[level] = levelWidth;
            _levelHeights[level] = levelHeight;
            _levels[level] = new Color32[checked(levelWidth * levelHeight)];
            Array.Fill(_levels[level], _unloadedColor);
            levelWidth = Mathf.Max(1, (levelWidth + 1) / 2);
            levelHeight = Mathf.Max(1, (levelHeight + 1) / 2);
        }
    }

    public int WidthChunks => _levelWidths[0];

    public int HeightChunks => _levelHeights[0];

    public int ChunkSize => _chunkSize;

    public void AddStoredRun(int chunkIndex, CellType cellType, int runLength)
    {
        if (chunkIndex < 0 || chunkIndex >= WidthChunks * HeightChunks || runLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkIndex));
        }

        if (_currentChunkIndex != chunkIndex)
        {
            FlushCurrentChunk();
            _currentChunkIndex = chunkIndex;
        }

        Color32 color = GetColor(cellType);
        _redSum += color.r * (long)runLength;
        _greenSum += color.g * (long)runLength;
        _blueSum += color.b * (long)runLength;
        _alphaSum += color.a * (long)runLength;
        _cellCount += runLength;
    }

    public void CompleteStoredScan() => FlushCurrentChunk();

    public void SetChunkCells(int chunkIndex, CellType[] cells)
    {
        if (cells == null)
        {
            throw new ArgumentNullException(nameof(cells));
        }

        int chunkArea = checked(_chunkSize * _chunkSize);
        if (cells.Length < chunkArea)
        {
            throw new ArgumentException(
                $"Chunk has {cells.Length} cells; expected at least {chunkArea}.",
                nameof(cells));
        }

        long red = 0;
        long green = 0;
        long blue = 0;
        long alpha = 0;
        for (int i = 0; i < chunkArea; i++)
        {
            Color32 color = GetColor(cells[i]);
            red += color.r;
            green += color.g;
            blue += color.b;
            alpha += color.a;
        }

        SetChunkAverage(chunkIndex, CreateAverage(red, green, blue, alpha, chunkArea));
    }

    public Color32 Sample(float worldX, float worldY, float cellsPerPixel)
    {
        float cellsPerOverviewPixel = Mathf.Max(1f, cellsPerPixel / _chunkSize);
        float lod = Mathf.Clamp(
            Mathf.Log(cellsPerOverviewPixel, 2f),
            0f,
            _levels.Length - 1);
        int lowerLevel = Mathf.FloorToInt(lod);
        int upperLevel = Mathf.Min(lowerLevel + 1, _levels.Length - 1);
        float blend = lod - lowerLevel;
        Color lower = SampleLevel(lowerLevel, worldX, worldY);
        if (upperLevel == lowerLevel || blend <= 0f)
        {
            return lower;
        }

        Color upper = SampleLevel(upperLevel, worldX, worldY);
        return Color32.Lerp((Color32)lower, (Color32)upper, blend);
    }

    private void FlushCurrentChunk()
    {
        if (_currentChunkIndex < 0)
        {
            return;
        }

        if (_cellCount != _chunkSize * _chunkSize)
        {
            throw new InvalidOperationException(
                $"Stored chunk {_currentChunkIndex} has {_cellCount} cells; " +
                $"expected {_chunkSize * _chunkSize}.");
        }

        SetChunkAverage(
            _currentChunkIndex,
            CreateAverage(_redSum, _greenSum, _blueSum, _alphaSum, _cellCount));
        _currentChunkIndex = -1;
        _redSum = 0;
        _greenSum = 0;
        _blueSum = 0;
        _alphaSum = 0;
        _cellCount = 0;
    }

    private void SetChunkAverage(int chunkIndex, Color32 average)
    {
        if (chunkIndex < 0 || chunkIndex >= WidthChunks * HeightChunks)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkIndex));
        }

        int chunkX = chunkIndex / HeightChunks;
        int chunkY = chunkIndex % HeightChunks;
        SetLevelPixel(0, chunkX, chunkY, average);
        int childX = chunkX;
        int childY = chunkY;
        for (int level = 1; level < _levels.Length; level++)
        {
            int parentX = childX / 2;
            int parentY = childY / 2;
            SetLevelPixel(
                level,
                parentX,
                parentY,
                AverageChildren(level - 1, parentX, parentY));
            childX = parentX;
            childY = parentY;
        }
    }

    private Color32 GetColor(CellType cellType) =>
        cellType == CellType.Unloaded ? _unloadedColor : _cellColorTable[(byte)cellType];

    private Color32 AverageChildren(int childLevel, int parentX, int parentY)
    {
        Color32[] children = _levels[childLevel];
        int childWidth = _levelWidths[childLevel];
        int childHeight = _levelHeights[childLevel];
        long red = 0;
        long green = 0;
        long blue = 0;
        long alpha = 0;
        int count = 0;

        for (int offsetY = 0; offsetY < 2; offsetY++)
        {
            int childY = parentY * 2 + offsetY;
            if (childY >= childHeight)
            {
                continue;
            }

            for (int offsetX = 0; offsetX < 2; offsetX++)
            {
                int childX = parentX * 2 + offsetX;
                if (childX >= childWidth)
                {
                    continue;
                }

                Color32 color = children[childY * childWidth + childX];
                red += color.r;
                green += color.g;
                blue += color.b;
                alpha += color.a;
                count++;
            }
        }

        return CreateAverage(red, green, blue, alpha, count);
    }

    private void SetLevelPixel(int level, int x, int y, Color32 color)
    {
        _levels[level][y * _levelWidths[level] + x] = color;
    }

    private Color SampleLevel(int level, float worldX, float worldY)
    {
        float levelScale = _chunkSize * Mathf.Pow(2f, level);
        float pixelX = (worldX / levelScale) - 0.5f;
        float pixelY = (worldY / levelScale) - 0.5f;
        int x0 = Mathf.FloorToInt(pixelX);
        int y0 = Mathf.FloorToInt(pixelY);
        float blendX = pixelX - x0;
        float blendY = pixelY - y0;
        Color top = Color.Lerp(
            ReadClamped(level, x0, y0),
            ReadClamped(level, x0 + 1, y0),
            blendX);
        Color bottom = Color.Lerp(
            ReadClamped(level, x0, y0 + 1),
            ReadClamped(level, x0 + 1, y0 + 1),
            blendX);
        return Color.Lerp(top, bottom, blendY);
    }

    private Color ReadClamped(int level, int x, int y)
    {
        int clampedX = Mathf.Clamp(x, 0, _levelWidths[level] - 1);
        int clampedY = Mathf.Clamp(y, 0, _levelHeights[level] - 1);
        return _levels[level][clampedY * _levelWidths[level] + clampedX];
    }

    private static Color32 CreateAverage(long red, long green, long blue, long alpha, int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        return new Color32(
            (byte)(red / count),
            (byte)(green / count),
            (byte)(blue / count),
            (byte)(alpha / count));
    }
}
