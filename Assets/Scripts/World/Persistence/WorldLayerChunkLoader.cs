#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using Kern;
using UnityEngine;

namespace Kern.Persistence;

/// <summary>
/// Асинхронная догрузка чанков с диска и учёт отказов.
/// </summary>
///
/// Чанк грузится один раз: пока идёт чтение, слот помечен как «в работе», а
/// повторный запрос получает Loading, а не вторую нитку чтения. Отказ
/// запоминается на слой, чтобы не бить в сломанный диск каждый кадр.
///
/// Полоса догрузки гасит поток событий: один вызов на регион вместо одного на
/// чанк. Событие о чанке приходит через <c>chunkLoaded</c>, потому что
/// подписчик живёт на слое, а не здесь.
internal sealed class WorldLayerChunkLoader<T>
    where T : unmanaged
{
    // A failing disk would otherwise warn once per chunk per streaming
    // pass and flood the console within seconds.
    private const int MaxLoggedChunkDiskFailures = 8;

    private readonly ChunkLRUCache<T> _cache;
    private readonly WorldLayerFile<T> _file;
    private readonly WorldLayerLifetime _lifetime;
    private readonly IAsyncOperationSupervisor _operations;
    private readonly Action<int, int, int, int> _chunkLoaded;
    private readonly int _chunkSize;
    private readonly int _chunkArea;
    private readonly int _widthChunks;
    private readonly int _heightChunks;

    private readonly HashSet<int> _loadingChunks = new();
    private readonly Dictionary<int, Exception> _failedChunkLoads = new();
    private readonly object _loadingLock = new object();
    private readonly HashSet<int> _loggedChunkLoadFailures = [];
    private bool _chunkDiskFailureCapLogged;
    private int _chunkLoadBatchDepth;
    private readonly List<RectInt> _batchedChunkRegions = new(8);

    public WorldLayerChunkLoader(
        ChunkLRUCache<T> cache,
        WorldLayerFile<T> file,
        WorldLayerLifetime lifetime,
        IAsyncOperationSupervisor operations,
        Action<int, int, int, int> chunkLoaded,
        int chunkSize,
        int chunkArea,
        int widthChunks,
        int heightChunks)
    {
        _cache = cache;
        _file = file;
        _lifetime = lifetime;
        _operations = operations;
        _chunkLoaded = chunkLoaded;
        _chunkSize = chunkSize;
        _chunkArea = chunkArea;
        _widthChunks = widthChunks;
        _heightChunks = heightChunks;
    }

    public void BeginBatch() => _chunkLoadBatchDepth++;

    public void EndBatch()
    {
        if (_chunkLoadBatchDepth <= 0)
        {
            throw new InvalidOperationException("[WorldLayer] Chunk load batch is not active.");
        }

        _chunkLoadBatchDepth--;
        if (_chunkLoadBatchDepth != 0 || _batchedChunkRegions.Count == 0)
        {
            return;
        }

        foreach (RectInt region in _batchedChunkRegions)
        {
            _chunkLoaded(region.x, region.y, region.width, region.height);
        }

        _batchedChunkRegions.Clear();
    }

    /// <summary>
    /// Чанк появился в кэше. Внутри полосы догрузки событие откладывается до её
    /// конца: иначе один регион порождал бы событие на каждый чанк.
    /// </summary>
    public void NotifyChunkMaterialized(int minX, int minY)
    {
        if (_chunkLoadBatchDepth > 0)
        {
            _batchedChunkRegions.Add(new RectInt(minX, minY, _chunkSize, _chunkSize));
            return;
        }

        _chunkLoaded(minX, minY, _chunkSize, _chunkSize);
    }

    public void NotifyRegionLoaded(int startX, int startY, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                $"Loaded region must be positive: ({startX},{startY}) {width}x{height}.");
        }

        int worldWidth = checked(_widthChunks * _chunkSize);
        int worldHeight = checked(_heightChunks * _chunkSize);
        long endX = (long)startX + width;
        long endY = (long)startY + height;
        if (startX < 0 || startY < 0 || endX > worldWidth || endY > worldHeight)
        {
            string message = $"Loaded region ({startX},{startY}) {width}x{height} is outside " +
                $"the layer of {worldWidth}x{worldHeight} cells.";
            throw new ArgumentOutOfRangeException(
                nameof(startX),
                message);
        }

        int firstChunkX = startX / _chunkSize;
        int firstChunkY = startY / _chunkSize;
        int lastChunkX = ((int)endX - 1) / _chunkSize;
        int lastChunkY = ((int)endY - 1) / _chunkSize;
        for (int chunkX = firstChunkX; chunkX <= lastChunkX; chunkX++)
        {
            for (int chunkY = firstChunkY; chunkY <= lastChunkY; chunkY++)
            {
                _chunkLoaded(
                    chunkX * _chunkSize,
                    chunkY * _chunkSize,
                    _chunkSize,
                    _chunkSize);
            }
        }
    }

    public ChunkReadResult<T> ReadChunk(int chunkIndex, bool touchLRU = true)
    {
        if (_lifetime.Disposed)
        {
            throw new ObjectDisposedException(nameof(WorldLayer<T>));
        }

        if (chunkIndex < 0 || chunkIndex >= _file.ChunkCount)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkIndex), chunkIndex, "Chunk index is outside the world layer.");
        }

        if (_cache.TryGet(chunkIndex, out T[]? chunk) && chunk != null)
        {
            if (touchLRU)
            {
                _cache.Touch(chunkIndex);
            }

            return new ChunkReadResult<T>(ChunkReadStatus.Available, chunk, null);
        }

        lock (_loadingLock)
        {
            if (_failedChunkLoads.TryGetValue(chunkIndex, out Exception? failure))
            {
                return new ChunkReadResult<T>(ChunkReadStatus.Failed, null, failure);
            }

            if (_file.OffsetAt(chunkIndex) < 0)
            {
                return new ChunkReadResult<T>(ChunkReadStatus.Missing, null, null);
            }

            if (!_loadingChunks.Add(chunkIndex))
            {
                return new ChunkReadResult<T>(ChunkReadStatus.Loading, null, null);
            }
        }

        try
        {
            _operations.Run(
                $"load_world_chunk_{chunkIndex}",
                _ => LoadChunkAsync(chunkIndex));
        }
        catch
        {
            // A supervisor can be disposed between the state transition above
            // and Run (for example during scene teardown). Do not leave this
            // slot permanently reporting Loading in that case.
            ClearLoadingChunk(chunkIndex);
            throw;
        }

        return new ChunkReadResult<T>(ChunkReadStatus.Loading, null, null);
    }

    public T[] GetOrCreateChunk(int chunkIndex, bool touchLRU = true)
    {
        if (_lifetime.Disposed)
        {
            throw new ObjectDisposedException(nameof(WorldLayer<T>));
        }

        if (chunkIndex < 0 || chunkIndex >= _file.ChunkCount)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkIndex), chunkIndex, "Chunk index is outside the world layer.");
        }

        if (_cache.TryGet(chunkIndex, out T[]? chunk) && chunk != null)
        {
            if (touchLRU)
            {
                _cache.Touch(chunkIndex);
            }

            return chunk;
        }

        try
        {
            chunk = _file.TryLoad(chunkIndex, _chunkArea, out bool corrupted);
            if (corrupted)
            {
                LogCorruptChunk(chunkIndex);
            }

            if (chunk == null)
            {
                // Sparse layers are expected while the server streams
                // regions. A synchronous write materializes the chunk;
                // read-only streaming keeps missing chunks distinct.
                chunk = new T[_chunkArea];
            }

            AddToCache(chunkIndex, chunk);
            return chunk;
        }
        catch (IOException ioEx)
        {
            throw new IOException($"[WorldLayer] Could not load/create chunk {chunkIndex}: {ioEx.Message}", ioEx);
        }
        catch (UnauthorizedAccessException authEx)
        {
            throw new UnauthorizedAccessException($"[WorldLayer] Access denied for chunk {chunkIndex}: {authEx.Message}", authEx);
        }
        catch (OutOfMemoryException)
        {
            throw;
        }
    }

    public void AddToCache(int chunkIndex, T[] chunk)
    {
        if (_lifetime.Disposed)
        {
            return;
        }

        _cache.AddOrUpdate(chunkIndex, chunk);
        lock (_loadingLock)
        {
            _failedChunkLoads.Remove(chunkIndex);
        }
    }

    public void MarkDirty(int chunkIndex)
    {
        _cache.MarkDirty(chunkIndex);
        lock (_loadingLock)
        {
            _failedChunkLoads.Remove(chunkIndex);
        }
    }

    public void ClearLoadingState()
    {
        lock (_loadingLock)
        {
            _loadingChunks.Clear();
            _failedChunkLoads.Clear();
        }
    }

    private async UniTask LoadChunkAsync(int chunkIndex)
    {
        T[]? chunk = null;
        bool corrupted = false;
        Exception? failure = null;
        try
        {
            try
            {
                (chunk, corrupted) = await UniTask.RunOnThreadPool(
                    () =>
                    {
                        T[]? loadedChunk = _file.TryLoad(chunkIndex, _chunkArea, out bool wasCorrupted);
                        return (loadedChunk, wasCorrupted);
                    });
            }
            catch (IOException ioEx)
            {
                failure = ioEx;
            }
            catch (ObjectDisposedException disposedEx)
            {
                failure = disposedEx;
            }
            catch (UnauthorizedAccessException authEx)
            {
                failure = authEx;
            }
            catch (InvalidDataException invalidDataEx)
            {
                failure = invalidDataEx;
            }
            catch (OutOfMemoryException outOfMemoryEx)
            {
                failure = outOfMemoryEx;
            }

            await UniTask.SwitchToMainThread();

            if (_lifetime.Disposed)
            {
                return;
            }

            if (failure != null)
            {
                lock (_loadingLock)
                {
                    _failedChunkLoads[chunkIndex] = failure;
                }

                LogChunkDiskFailure(
                    _loggedChunkLoadFailures,
                    chunkIndex,
                    $"[WorldLayer] Failed to load chunk {chunkIndex}: {failure.Message}");
                return;
            }

            if (corrupted)
            {
                LogCorruptChunk(chunkIndex);
            }

            // A synchronous request may have filled this slot while the disk
            // read was in flight. Do not overwrite it and, more importantly,
            // do not append a second LRU node for the same chunk.
            if (_cache.Contains(chunkIndex))
            {
                return;
            }

            // A sparse map is expected while the server is streaming regions.
            // Missing data is not an empty chunk: keep it unloaded so consumers
            // can render the explicit unloaded/black state and retry only after
            // an actual region is received.
            if (chunk == null)
            {
                return;
            }

            AddToCache(chunkIndex, chunk);
            int chunkX = chunkIndex / _heightChunks;
            int chunkY = chunkIndex % _heightChunks;
            _chunkLoaded(
                chunkX * _chunkSize,
                chunkY * _chunkSize,
                _chunkSize,
                _chunkSize);
        }
        finally
        {
            // Keep the state machine recoverable even when decoding or the
            // notification callback throws an unexpected exception.
            ClearLoadingChunk(chunkIndex);
        }
    }

    private void ClearLoadingChunk(int chunkIndex)
    {
        lock (_loadingLock)
        {
            _loadingChunks.Remove(chunkIndex);
        }
    }

    private void LogChunkDiskFailure(HashSet<int> reported, int chunkIndex, string message)
    {
        if (!reported.Add(chunkIndex))
        {
            return;
        }

        Debug.LogWarning(message);
        if (!_chunkDiskFailureCapLogged && reported.Count >= MaxLoggedChunkDiskFailures)
        {
            _chunkDiskFailureCapLogged = true;
            Debug.LogWarning(
                "[WorldLayer] Further per-chunk disk failure warnings are suppressed for this session.");
        }
    }

    private void LogCorruptChunk(int chunkIndex)
    {
        LogChunkDiskFailure(
            _loggedChunkLoadFailures,
            chunkIndex,
            $"[WorldLayer] Chunk {chunkIndex} is corrupt; loading it as an all-zero chunk.");
    }
}
