#nullable enable

using System;
using System.Collections.Generic;

namespace Kern.Persistence;

/// <summary>
/// Заливка прямоугольного региона клеток в кэш чанков.
/// </summary>
///
/// Регион приходит и из сети, и из тестов, и от перезаписи карты. Полный
/// сетевой чанк авторитетен для всех своих клеток, поэтому загрузка прежнего
/// RLE-содержимого с диска была бы лишним синхронным декодом ровно тогда,
/// когда игрок переходит границу стрима. Частичный регион, наоборот, идёт
/// через обычное получение чанка — клетки вне пакета обязаны уцелеть.
internal sealed class WorldLayerRegionWriter<T>
    where T : unmanaged
{
    private readonly ChunkLRUCache<T> _cache;
    private readonly WorldLayerChunkLoader<T> _loader;
    private readonly int _chunkSize;
    private readonly int _chunkArea;
    private readonly int _heightChunks;

    public WorldLayerRegionWriter(
        ChunkLRUCache<T> cache,
        WorldLayerChunkLoader<T> loader,
        int chunkSize,
        int chunkArea,
        int heightChunks)
    {
        _cache = cache;
        _loader = loader;
        _chunkSize = chunkSize;
        _chunkArea = chunkArea;
        _heightChunks = heightChunks;
    }

    public (int ChangedCount, bool OnlyNewChunks) Write(
        int startX,
        int startY,
        int width,
        int height,
        ReadOnlySpan<T> cells,
        int cellsOffset,
        int worldWidth,
        int worldHeight)
    {
        int endX = Math.Min(startX + width, worldWidth);
        int endY = Math.Min(startY + height, worldHeight);
        int firstChunkX = startX / _chunkSize;
        int lastChunkX = (endX - 1) / _chunkSize;
        int firstChunkY = startY / _chunkSize;
        int lastChunkY = (endY - 1) / _chunkSize;

        int changedCount = 0;
        int touchedChunkCount = 0;
        int newChunkCount = 0;
        for (int chunkX = firstChunkX; chunkX <= lastChunkX; chunkX++)
        {
            for (int chunkY = firstChunkY; chunkY <= lastChunkY; chunkY++)
            {
                int chunkIndex = chunkY + (chunkX * _heightChunks);
                bool wasLoaded = _cache.Contains(chunkIndex);
                touchedChunkCount++;
                if (!wasLoaded)
                {
                    newChunkCount++;
                }

                int regionX0 = Math.Max(startX, chunkX * _chunkSize);
                int regionX1 = Math.Min(endX, (chunkX + 1) * _chunkSize);
                int regionY0 = Math.Max(startY, chunkY * _chunkSize);
                int regionY1 = Math.Min(endY, (chunkY + 1) * _chunkSize);
                bool overwritesWholeChunk =
                    regionX0 == chunkX * _chunkSize &&
                    regionX1 == (chunkX + 1) * _chunkSize &&
                    regionY0 == chunkY * _chunkSize &&
                    regionY1 == (chunkY + 1) * _chunkSize;

                T[] chunk;
                if (!wasLoaded && overwritesWholeChunk)
                {
                    chunk = new T[_chunkArea];
                    _loader.AddToCache(chunkIndex, chunk);
                }
                else
                {
                    chunk = _loader.GetOrCreateChunk(chunkIndex, touchLRU: true);
                }

                bool chunkChanged = false;
                for (int x = regionX0; x < regionX1; x++)
                {
                    int localX = x - (chunkX * _chunkSize);
                    for (int y = regionY0; y < regionY1; y++)
                    {
                        int payloadIndex = cellsOffset + ((y - startY) * width) + (x - startX);
                        T value = cells[payloadIndex];
                        int localIndex = (y - (chunkY * _chunkSize)) + (localX * _chunkSize);
                        if ((overwritesWholeChunk && !wasLoaded) ||
                            !EqualityComparer<T>.Default.Equals(chunk[localIndex], value))
                        {
                            if (wasLoaded)
                            {
                                chunk = _cache.PrepareForWrite(chunkIndex, chunk);
                            }

                            chunk[localIndex] = value;
                            chunkChanged = true;
                            if (!overwritesWholeChunk || wasLoaded)
                            {
                                changedCount++;
                            }
                        }
                    }
                }

                if (chunkChanged)
                {
                    _loader.MarkDirty(chunkIndex);
                }

                // SetRegion is also the network streaming path. Notify only
                // when this call actually materialized a previously missing
                // chunk; repeated packets for an already loaded chunk are
                // ordinary data changes and are covered by RegionChanged.
                if (!wasLoaded)
                {
                    _loader.NotifyChunkMaterialized(
                        chunkX * _chunkSize,
                        chunkY * _chunkSize);
                }
            }
        }

        return (changedCount, touchedChunkCount > 0 && touchedChunkCount == newChunkCount);
    }
}
