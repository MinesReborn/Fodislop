#nullable enable

using System;
using System.Collections.Generic;

namespace Kern;

public enum ChunkReadStatus
{
    Available,
    Loading,
    Missing,
    Failed,
}

public readonly record struct ChunkReadResult<T>(
    ChunkReadStatus Status,
    T[]? Data,
    Exception? Error)
    where T : unmanaged;

public interface IWorldLayer<T> : IDisposable
    where T : unmanaged
{
    int ChunkSize { get; }
    int WidthChunks { get; }
    int HeightChunks { get; }
    int MaxChunksInMemory { get; }
    bool HasDirtyChunks { get; }

    // Raised when a chunk becomes resident in the layer cache. Changes to an
    // already resident chunk use the owning storage's RegionChanged event.
    event Action<int, int, int, int>? ChunkLoaded;

    T this[int x, int y] { get; set; }

    void NotifyRegionLoaded(int startX, int startY, int width, int height);
    IEnumerable<int> GetLoadedChunkIndices();
    int GetLoadedCount();
    int GetDirtyCount();
    T GetCell(int x, int y, bool touchLru = true);
    T GetCellSync(int x, int y, bool touchLru = true);
    bool TryGetCell(int x, int y, out T value);
    void SetCell(int x, int y, T value);
    int SetRegion(
        int startX,
        int startY,
        int width,
        int height,
        T[] cells,
        int cellsOffset = 0);
    int SetRegion(
        int startX,
        int startY,
        int width,
        int height,
        ReadOnlySpan<T> cells,
        int cellsOffset = 0);
    T[] GetOrCreateChunk(int chunkIndex, bool touchLru = true);
    ChunkReadResult<T> ReadChunk(int chunkIndex, bool touchLru = true);
    void Flush(bool flushToDisk = false);
    bool GetChunkIndexAndLocal(
        int x,
        int y,
        out int chunkIndex,
        out int localIndex);
}
