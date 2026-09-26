#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.Persistence;
using MinesServer.Data;
using UnityEngine;

namespace Kern.UI;

/// <summary>Owns asynchronous MIP-cache population and bounded updates from live chunk notifications.</summary>
internal sealed class WorldMapMipScan : IDisposable
{
    private readonly HashSet<int> _pendingChunks = new();
    private readonly int[] _pendingChunkBatch = new int[8];
    private Action? _requestRender;
    private WorldMapMipCache? _cache;
    private IWorldLayer<CellType>? _cellLayer;
    private CancellationTokenSource? _scanCancellation;
    private bool _failed;
    private int _progress;
    private int _total;

    public void SetRequestRenderCallback(Action requestRender)
    {
        _requestRender = requestRender ?? throw new ArgumentNullException(nameof(requestRender));
    }

    public WorldMapMipCache? Cache => _cache;

    public bool IsReady { get; private set; }

    public bool Failed => _failed;

    public int Progress => Volatile.Read(ref _progress);

    public int Total => Volatile.Read(ref _total);

    public void Bind(
        IWorldLayer<CellType>? cellLayer,
        int chunkSize,
        Color32[] cellColorTable)
    {
        _scanCancellation?.Cancel();
        _scanCancellation = null;
        _cellLayer = cellLayer;
        int widthChunks = cellLayer?.WidthChunks ?? 0;
        int heightChunks = cellLayer?.HeightChunks ?? 0;
        if (widthChunks <= 0 || heightChunks <= 0 || chunkSize <= 0)
        {
            _cache = null;
            IsReady = false;
            return;
        }

        _cache = new WorldMapMipCache(
            widthChunks,
            heightChunks,
            chunkSize,
            cellColorTable,
            MapProjection.UnknownCellColor(0, 0));
        _pendingChunks.Clear();
        IsReady = false;
        _failed = false;
        _progress = 0;
        _total = 0;
    }

    public void QueueChunk(int chunkX, int chunkY)
    {
        if (_cache == null || chunkX < 0 || chunkY < 0 ||
            chunkX >= _cache.WidthChunks || chunkY >= _cache.HeightChunks)
        {
            return;
        }

        _pendingChunks.Add(chunkY + (chunkX * _cache.HeightChunks));
    }

    public void Begin()
    {
        if (IsReady || _failed || _scanCancellation != null ||
            _cache == null || _cellLayer == null)
        {
            return;
        }

        _scanCancellation = new CancellationTokenSource();
        _ = PrepareMipCacheAsync(_scanCancellation);
    }

    public void UpdatePendingChunks()
    {
        if (!IsReady || _cache == null || _cellLayer == null || _pendingChunks.Count == 0)
        {
            return;
        }

        int pendingCount = 0;
        foreach (int chunkIndex in _pendingChunks)
        {
            _pendingChunkBatch[pendingCount++] = chunkIndex;
            if (pendingCount == _pendingChunkBatch.Length)
            {
                break;
            }
        }

        for (int index = 0; index < pendingCount; index++)
        {
            int chunkIndex = _pendingChunkBatch[index];
            ChunkReadResult<CellType> result = _cellLayer.ReadChunk(chunkIndex, touchLru: false);
            if (result.Status == ChunkReadStatus.Available && result.Data != null)
            {
                _cache.SetChunkCells(chunkIndex, result.Data);
            }

            _pendingChunks.Remove(chunkIndex);
        }
    }

    public void Dispose()
    {
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _scanCancellation = null;
    }

    private async UniTask PrepareMipCacheAsync(CancellationTokenSource scanCancellation)
    {
        CancellationToken cancellationToken = scanCancellation.Token;
        WorldMapMipCache? cache = _cache;
        IWorldLayer<CellType>? layer = _cellLayer;
        if (cache == null || layer == null)
        {
            return;
        }

        try
        {
            if (layer is IStoredChunkSource<CellType> source)
            {
                await source.VisitStoredChunkRunsAsync(
                    (chunkIndex, cellType, runLength) => cache.AddStoredRun(chunkIndex, cellType, runLength),
                    (progress, total) =>
                    {
                        Volatile.Write(ref _progress, progress);
                        Volatile.Write(ref _total, total);
                    },
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentScan(scanCancellation, cache, layer))
            {
                return;
            }

            cache.CompleteStoredScan();
            foreach (int chunkIndex in layer.GetLoadedChunkIndices())
            {
                cancellationToken.ThrowIfCancellationRequested();
                ChunkReadResult<CellType> result = layer.ReadChunk(chunkIndex, touchLru: false);
                if (result.Status == ChunkReadStatus.Available && result.Data != null)
                {
                    cache.SetChunkCells(chunkIndex, result.Data);
                }
            }

            foreach (int chunkIndex in _pendingChunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ChunkReadResult<CellType> result = layer.ReadChunk(chunkIndex, touchLru: false);
                if (result.Status == ChunkReadStatus.Available && result.Data != null)
                {
                    cache.SetChunkCells(chunkIndex, result.Data);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentScan(scanCancellation, cache, layer))
            {
                return;
            }

            IsReady = true;
            _pendingChunks.Clear();
            _requestRender?.Invoke();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            _failed = true;
        }
        finally
        {
            if (ReferenceEquals(_scanCancellation, scanCancellation))
            {
                _scanCancellation = null;
            }

            scanCancellation.Dispose();
        }
    }

    private bool IsCurrentScan(
        CancellationTokenSource scanCancellation,
        WorldMapMipCache cache,
        IWorldLayer<CellType> layer) =>
        ReferenceEquals(_scanCancellation, scanCancellation) &&
        ReferenceEquals(_cache, cache) &&
        ReferenceEquals(_cellLayer, layer);
}
