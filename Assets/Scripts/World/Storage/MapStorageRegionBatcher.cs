#nullable enable

using System;
using Kern.Persistence;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World;

internal sealed class MapStorageRegionBatcher
{
    private int _regionBatchDepth;
    private bool _batchedRegionChanged;
    private int _batchedRegionMinX;
    private int _batchedRegionMinY;
    private int _batchedRegionMaxX;
    private int _batchedRegionMaxY;
    private bool _clippedRegionWarningLogged;

    public int Depth => _regionBatchDepth;

    public void BeginBatch(WorldLayer<CellType>? cellLayer)
    {
        _regionBatchDepth++;
        if (_regionBatchDepth == 1)
        {
            cellLayer?.BeginChunkLoadBatch();
        }
    }

    public void EndBatch(WorldLayer<CellType>? cellLayer, Action<int, int, int, int>? onRegionChanged)
    {
        if (_regionBatchDepth <= 0)
        {
            throw new InvalidOperationException("[MapStorage] Region batch is not active.");
        }

        _regionBatchDepth--;
        if (_regionBatchDepth != 0)
        {
            return;
        }

        cellLayer?.EndChunkLoadBatch();
        if (_batchedRegionChanged)
        {
            _batchedRegionChanged = false;
            onRegionChanged?.Invoke(
                _batchedRegionMinX,
                _batchedRegionMinY,
                _batchedRegionMaxX - _batchedRegionMinX,
                _batchedRegionMaxY - _batchedRegionMinY);
        }
    }

    public void RecordRegionChange(int startX, int startY, int width, int height)
    {
        if (!_batchedRegionChanged)
        {
            _batchedRegionMinX = startX;
            _batchedRegionMinY = startY;
            _batchedRegionMaxX = startX + width;
            _batchedRegionMaxY = startY + height;
            _batchedRegionChanged = true;
        }
        else
        {
            _batchedRegionMinX = Math.Min(_batchedRegionMinX, startX);
            _batchedRegionMinY = Math.Min(_batchedRegionMinY, startY);
            _batchedRegionMaxX = Math.Max(_batchedRegionMaxX, startX + width);
            _batchedRegionMaxY = Math.Max(_batchedRegionMaxY, startY + height);
        }
    }

    public void ValidateRegionParameters(
        int startX,
        int startY,
        int width,
        int height,
        int cellCount,
        int worldWidth,
        int worldHeight)
    {
        long expectedCellCount = (long)width * height;
        if (width <= 0 || height <= 0 || cellCount < expectedCellCount)
        {
            throw new ArgumentException(
                $"[MapStorage] Invalid region ({startX},{startY}) {width}x{height}: " +
                $"payload has {cellCount} cells, expected at least {expectedCellCount}.");
        }

        if (startX < 0 || startY < 0 || startX >= worldWidth || startY >= worldHeight)
        {
            string message =
                "[MapStorage] Region " +
                $"({startX},{startY}) {width}x{height} " +
                $"is outside world bounds {worldWidth}x{worldHeight}.";
            throw new ArgumentOutOfRangeException(
                nameof(startX),
                message);
        }
    }

    public (int appliedWidth, int appliedHeight) ClipRegionBounds(
        int startX,
        int startY,
        int width,
        int height,
        int worldWidth,
        int worldHeight)
    {
        int appliedWidth = Math.Min(width, worldWidth - startX);
        int appliedHeight = Math.Min(height, worldHeight - startY);
        if (appliedWidth != width || appliedHeight != height)
        {
            if (!_clippedRegionWarningLogged)
            {
                Debug.LogWarning(
                    $"[MapStorage] Clipping padded edge regions to world bounds " +
                    $"({worldWidth}x{worldHeight}); first region " +
                    $"({startX},{startY}) {width}x{height} -> " +
                    $"{appliedWidth}x{appliedHeight}.");
                _clippedRegionWarningLogged = true;
            }
        }

        return (appliedWidth, appliedHeight);
    }
}
