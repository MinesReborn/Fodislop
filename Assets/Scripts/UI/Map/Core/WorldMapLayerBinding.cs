#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.Persistence;
using MinesServer.Data;
using UnityEngine;

namespace Kern.UI;

/// <summary>Owns map world-layer subscriptions and the cache invalidations they publish.</summary>
internal sealed class WorldMapLayerBinding : IDisposable
{
    private readonly MapCellSampler _cellSampler;
    private readonly WorldMapMipScan _mipScan;
    private readonly Action _requestRender;
    private IWorldDataStorage? _storage;
    private IWorldLayer<CellType>? _subscribedCellLayer;
    private int _chunkSize;

    public WorldMapLayerBinding(
        MapCellSampler cellSampler,
        WorldMapMipScan mipScan,
        Action requestRender)
    {
        _cellSampler = cellSampler ?? throw new ArgumentNullException(nameof(cellSampler));
        _mipScan = mipScan ?? throw new ArgumentNullException(nameof(mipScan));
        _requestRender = requestRender ?? throw new ArgumentNullException(nameof(requestRender));
    }

    public IWorldLayer<CellType>? CellLayer => _subscribedCellLayer;

    public int ChunkSize => _chunkSize;

    public void BindStorage(IWorldDataStorage storage)
    {
        if (_storage != null)
        {
            _storage.RegionChanged -= OnRegionChanged;
        }

        _storage = storage;
        _storage.RegionChanged -= OnRegionChanged;
        _storage.RegionChanged += OnRegionChanged;
    }

    public bool BindCellLayer(IWorldLayer<CellType>? cellLayer)
    {
        if (ReferenceEquals(_subscribedCellLayer, cellLayer))
        {
            return false;
        }

        if (_subscribedCellLayer != null)
        {
            _subscribedCellLayer.ChunkLoaded -= OnChunkLoaded;
        }

        _subscribedCellLayer = cellLayer;
        _cellSampler.Bind(cellLayer);
        _cellSampler.Invalidate();

        if (_subscribedCellLayer != null)
        {
            _chunkSize = _subscribedCellLayer.ChunkSize;
            _subscribedCellLayer.ChunkLoaded += OnChunkLoaded;
        }
        else
        {
            _chunkSize = 0;
        }

        return true;
    }

    public void BindMipScan(Color32[] cellColorTable) =>
        _mipScan.Bind(_subscribedCellLayer, _chunkSize, cellColorTable);

    public void RebindCellEvents()
    {
        if (_subscribedCellLayer == null)
        {
            return;
        }

        _subscribedCellLayer.ChunkLoaded -= OnChunkLoaded;
        _subscribedCellLayer.ChunkLoaded += OnChunkLoaded;
    }

    public void Dispose()
    {
        BindCellLayer(null);
        if (_storage != null)
        {
            _storage.RegionChanged -= OnRegionChanged;
            _storage = null;
        }
    }

    private void OnChunkLoaded(int serverX, int serverY, int width, int height)
    {
        _cellSampler.InvalidateChunk(serverX, serverY);
        _mipScan.QueueChunk(serverX / Mathf.Max(1, _chunkSize), serverY / Mathf.Max(1, _chunkSize));
        _requestRender();
    }

    private void OnRegionChanged(int startX, int startY, int width, int height)
    {
        if (width <= 0 || height <= 0 || _chunkSize <= 0)
        {
            return;
        }

        int endX = startX + width - 1;
        int endY = startY + height - 1;
        for (int chunkX = Mathf.Max(0, startX / _chunkSize); chunkX <= endX / _chunkSize; chunkX++)
        {
            for (int chunkY = Mathf.Max(0, startY / _chunkSize); chunkY <= endY / _chunkSize; chunkY++)
            {
                _mipScan.QueueChunk(chunkX, chunkY);
                _cellSampler.InvalidateChunk(chunkX * _chunkSize, chunkY * _chunkSize);
            }
        }

        _requestRender();
    }
}
