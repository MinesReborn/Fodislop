#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.Persistence;
using MinesServer.Data;
using UnityEngine;

using Kern.AssetPipeline.Loading;

namespace Kern.UI.Map;

/// <summary>
/// Owns the map overview mip pyramid and its texture: asynchronous initial
/// population from the stored layer file, plus bounded live updates from chunk
/// notifications. Implements <see cref="IMapMipTextureSource"/> so the viewport
/// renderer never depends on this concrete type.
/// </summary>
internal sealed class WorldMapMipSource : IMapMipTextureSource, IDisposable
{
    private const int PendingBatchSize = 8;

    private readonly ConcurrentQueue<int> _pendingChunks = new();
    private readonly ConcurrentDictionary<int, byte> _queuedChunkSet = new();
    private Action? _requestRender;
    private WorldMapMipPyramid? _pyramid;
    private Texture2D? _texture;
    private IWorldLayer<CellType>? _cellLayer;
    private CancellationTokenSource? _scanCancellation;
    private int _mipBlockSize;
    private int _widthChunks;
    private int _heightChunks;
    private int _chunkSize;
    private int _worldWidth;
    private int _worldHeight;
    private Color32[]? _cellColorTable;
    private bool _textureDirty = true;
    private bool _pendingChanges;
    private bool _failed;
    private int _progress;
    private int _total;

    public bool IsReady { get; private set; }

    public bool Failed => _failed;

    public int Progress => Volatile.Read(ref _progress);

    public int Total => Volatile.Read(ref _total);

    public int MipLevelCount => _pyramid?.MipLevelCount ?? 0;

    public int MipBlockSize => _mipBlockSize;

    public bool HasPendingChunks => !_pendingChunks.IsEmpty;

    public Texture Texture
    {
        get
        {
            EnsureTexture();
            return _texture!;
        }
    }

    public void SetRequestRenderCallback(Action requestRender) =>
        _requestRender = requestRender ?? throw new ArgumentNullException(nameof(requestRender));

    public void Bind(
        IWorldLayer<CellType>? cellLayer,
        int chunkSize,
        int worldWidth,
        int worldHeight,
        Color32[] cellColorTable)
    {
        CancelScan();
        DisposeResources();

        _cellLayer = cellLayer;
        IsReady = false;
        _failed = false;
        _progress = 0;
        _total = 0;
        _pendingChanges = false;
        while (_pendingChunks.TryDequeue(out _))
        {
        }
        _queuedChunkSet.Clear();

        _widthChunks = cellLayer?.WidthChunks ?? 0;
        _heightChunks = cellLayer?.HeightChunks ?? 0;
        _chunkSize = chunkSize;
        _worldWidth = worldWidth;
        _worldHeight = worldHeight;
        _cellColorTable = (Color32[])cellColorTable.Clone();
        if (_widthChunks <= 0 || _heightChunks <= 0 || chunkSize <= 0 || worldWidth <= 0 || worldHeight <= 0)
        {
            return;
        }

        _mipBlockSize = WorldMapMipSizing.ChooseBlockSize(worldWidth, worldHeight, chunkSize);
    }

    public void Begin()
    {
        if (IsReady || _failed || _scanCancellation != null || _cellLayer == null ||
            _widthChunks <= 0 || _heightChunks <= 0 || _chunkSize <= 0 || _cellColorTable == null)
        {
            return;
        }

        _scanCancellation = new CancellationTokenSource();
        _ = PrepareAsync(_scanCancellation);
    }

    public void QueueChunk(int chunkX, int chunkY)
    {
        if (chunkX < 0 || chunkY < 0 || chunkX >= _widthChunks || chunkY >= _heightChunks)
        {
            return;
        }

        int chunkIndex = chunkY + (chunkX * _heightChunks);
        if (_queuedChunkSet.TryAdd(chunkIndex, 0))
        {
            _pendingChunks.Enqueue(chunkIndex);
        }
    }

    public void ApplyPending()
    {
        if (!IsReady || _pyramid == null || _cellLayer == null)
        {
            return;
        }

        bool changed = false;
        int processed = 0;
        while (processed < PendingBatchSize && _pendingChunks.TryDequeue(out int chunkIndex))
        {
            _queuedChunkSet.TryRemove(chunkIndex, out _);
            processed++;
            ChunkReadResult<CellType> result = _cellLayer.ReadChunk(chunkIndex, touchLRU: false);
            if (result.Status == ChunkReadStatus.Available && result.Data != null)
            {
                _pyramid.SetChunkCells(chunkIndex, result.Data);
                changed = true;
            }
        }

        if (changed)
        {
            _textureDirty = true;
            _pendingChanges = true;
        }

        if (_pendingChanges && _pendingChunks.IsEmpty)
        {
            _pendingChanges = false;
            _requestRender?.Invoke();
        }
    }

    public void Dispose()
    {
        CancelScan();
        DisposeResources();
    }

    private async UniTask PrepareAsync(CancellationTokenSource scanCancellation)
    {
        CancellationToken cancellationToken = scanCancellation.Token;
        IWorldLayer<CellType>? layer = _cellLayer;
        Color32[]? colors = _cellColorTable;
        if (layer == null || colors == null)
        {
            return;
        }

        int widthChunks = _widthChunks;
        int heightChunks = _heightChunks;
        int chunkSize = _chunkSize;
        int worldWidth = _worldWidth;
        int worldHeight = _worldHeight;
        int blockSize = _mipBlockSize;

        try
        {
            WorldMapMipPyramid pyramid = await UniTask.RunOnThreadPool(
                () => new WorldMapMipPyramid(
                    widthChunks, heightChunks, chunkSize, worldWidth, worldHeight,
                    colors, MapProjection.UnknownCellSolidColor, blockSize),
                cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_scanCancellation, scanCancellation) || !ReferenceEquals(_cellLayer, layer))
            {
                return;
            }

            _pyramid = pyramid;
            pyramid.BeginStoredScan();
            if (layer is IStoredChunkSource<CellType> source)
            {
                await source.VisitStoredChunkRunsAsync(
                    (chunkIndex, cellType, runLength) => pyramid.AddStoredRun(chunkIndex, cellType, runLength),
                    (progress, total) =>
                    {
                        Volatile.Write(ref _progress, progress);
                        Volatile.Write(ref _total, total);
                    },
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentScan(scanCancellation, pyramid, layer))
            {
                return;
            }

            await UniTask.RunOnThreadPool(
                pyramid.CompleteStoredScan,
                cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentScan(scanCancellation, pyramid, layer))
            {
                return;
            }

            foreach (int chunkIndex in layer.GetLoadedChunkIndices())
            {
                cancellationToken.ThrowIfCancellationRequested();
                QueueChunk(chunkIndex / pyramid.HeightChunks, chunkIndex % pyramid.HeightChunks);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentScan(scanCancellation, pyramid, layer))
            {
                return;
            }

            IsReady = true;
            _requestRender?.Invoke();
            while (HasPendingChunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ApplyPending();
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_scanCancellation, scanCancellation) && ReferenceEquals(_cellLayer, layer))
            {
                Debug.LogException(exception);
                _failed = true;
                _requestRender?.Invoke();
            }
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

    private void EnsureTexture()
    {
        WorldMapMipPyramid? pyramid = _pyramid;
        if (pyramid == null)
        {
            throw new InvalidOperationException(
                "[WorldMapMipSource] The mip texture was requested before the pyramid was bound.");
        }

        if (_texture == null)
        {
            _texture = RuntimeTextureFactory.CreateRGBA32MipChain(
                pyramid.GetLevelWidth(0),
                pyramid.GetLevelHeight(0),
                "WorldMapMipTexture",
                RuntimeTextureColorSpace.Srgb,
                FilterMode.Point,
                TextureWrapMode.Clamp);
            _textureDirty = true;
        }

        if (_textureDirty)
        {
            for (int level = 0; level < pyramid.MipLevelCount; level++)
            {
                _texture.SetPixelData(pyramid.GetLevel(level), level);
            }

            _texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            _textureDirty = false;
        }
    }

    private void CancelScan()
    {
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _scanCancellation = null;
    }

    private void DisposeResources()
    {
        _pyramid = null;
        _cellLayer = null;
        _cellColorTable = null;
        _widthChunks = 0;
        _heightChunks = 0;
        _textureDirty = true;
        if (_texture != null)
        {
            UnityEngine.Object.Destroy(_texture);
            _texture = null;
        }
    }

    private bool IsCurrentScan(
        CancellationTokenSource scanCancellation,
        WorldMapMipPyramid pyramid,
        IWorldLayer<CellType> layer) =>
        ReferenceEquals(_scanCancellation, scanCancellation) &&
        ReferenceEquals(_pyramid, pyramid) &&
        ReferenceEquals(_cellLayer, layer);
}
