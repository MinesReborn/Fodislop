#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Лежат ли данные окна в памяти — и если нет, заказать их.
/// </summary>
///
/// Окно нельзя собирать по частично приехавшим чанкам: в кадре это выглядит
/// как дыры, которые потом молча зарастают. Поэтому решение принимается до
/// сборки и по всему окну сразу, с каймой в одну клетку (её читают маски
/// соседства).
public static class TerrainResidencyProbe
{
    /// <summary>
    /// Chunk statuses are stable during one synchronous terrain plan. Reuse
    /// the first read across candidate windows so overlapping probes touch an
    /// LRU node only once per frame.
    ///
    /// Stable-frame work is one full requested-window probe. A partial advance
    /// checks at most five additional candidates; worst-case unique reads are
    /// the union of six chunk rectangles, bounded by six window footprints.
    /// The dictionary retains its peak capacity on the planner and is cleared
    /// without per-frame allocation. [TerrainStall] reports probes, unique
    /// ReadChunk calls, and avoided repeated reads.
    /// </summary>
    internal sealed class FrameCache
    {
        private readonly Dictionary<int, ChunkReadStatus> _statuses = new(64);
        private IWorldLayer<CellType>? _layer;
        private IWorldDataStorage? _storage;
        private IMapDataProvider? _mapData;
        private int _worldWidth;
        private int _worldHeight;
        private int _chunkSize;
        private int _windowWidth;
        private int _windowHeight;

        public FrameCache()
        {
            ResidencyCallback = IsWindowResident;
        }

        public Func<Vector2Int, bool> ResidencyCallback { get; }

        public int ChunkReads { get; private set; }

        public int CacheHits { get; private set; }

        public int ProbeCalls { get; private set; }

        public int LruTouches { get; private set; }

        public void BeginFrame(
            IWorldDataStorage? storage,
            IMapDataProvider? mapData,
            int windowWidth,
            int windowHeight)
        {
            _statuses.Clear();
            ChunkReads = 0;
            CacheHits = 0;
            ProbeCalls = 0;
            LruTouches = 0;
            _storage = storage;
            _mapData = mapData;
            _windowWidth = windowWidth;
            _windowHeight = windowHeight;
        }

        private bool IsWindowResident(Vector2Int position) =>
            TerrainResidencyProbe.IsWindowResident(
                storage: _storage,
                mapData: _mapData,
                connectionService: null,
                gridPosition: position,
                width: _windowWidth,
                height: _windowHeight,
                frameCache: this);

        public ChunkReadStatus ReadStatus(
            IWorldLayer<CellType> layer,
            int chunkIndex,
            bool refreshLru = false)
        {
            if (_statuses.TryGetValue(chunkIndex, out ChunkReadStatus status))
            {
                CacheHits++;
                if (refreshLru && status == ChunkReadStatus.Available)
                {
                    layer.ReadChunk(chunkIndex, touchLru: true);
                    LruTouches++;
                }

                return status;
            }

            status = layer.ReadChunk(chunkIndex, touchLru: true).Status;
            _statuses.Add(chunkIndex, status);
            ChunkReads++;
            if (status == ChunkReadStatus.Available)
            {
                LruTouches++;
            }

            return status;
        }

        public void PrepareLayer(IWorldLayer<CellType> layer, int worldWidth, int worldHeight)
        {
            int chunkSize = layer.ChunkSize;
            if (ReferenceEquals(_layer, layer) &&
                _worldWidth == worldWidth &&
                _worldHeight == worldHeight &&
                _chunkSize == chunkSize)
            {
                return;
            }

            _statuses.Clear();
            _layer = layer;
            _worldWidth = worldWidth;
            _worldHeight = worldHeight;
            _chunkSize = chunkSize;
        }

        public void RecordProbe() => ProbeCalls++;
    }

    /// <summary>
    /// Лежит ли окно в памяти. Ничего не заказывает — этим можно щупать
    /// промежуточные положения окна, не поднимая сетевого трафика.
    /// </summary>
    public static bool IsWindowResident(
        IWorldDataStorage? storage,
        IMapDataProvider? mapData,
        Vector2Int gridPosition,
        int width,
        int height) =>
        Probe(storage, mapData, null, gridPosition, width, height);

    /// <summary>
    /// То же самое, но недостающие чанки заказываются у сервера.
    /// </summary>
    public static bool IsWindowResident(
        IWorldDataStorage? storage,
        IMapDataProvider? mapData,
        IConnectionService? connectionService,
        Vector2Int gridPosition,
        int width,
        int height) =>
        Probe(storage, mapData, connectionService, gridPosition, width, height);

    internal static bool IsWindowResident(
        IWorldDataStorage? storage,
        IMapDataProvider? mapData,
        IConnectionService? connectionService,
        Vector2Int gridPosition,
        int width,
        int height,
        FrameCache frameCache) =>
        Probe(storage, mapData, connectionService, gridPosition, width, height, frameCache);

    internal static void TouchWindow(
        IWorldDataStorage? storage,
        IMapDataProvider? mapData,
        Vector2Int gridPosition,
        int width,
        int height,
        FrameCache frameCache) =>
        Probe(storage, mapData, null, gridPosition, width, height, frameCache, refreshLru: true);

    private static bool Probe(
        IWorldDataStorage? storage,
        IMapDataProvider? mapData,
        IConnectionService? connectionService,
        Vector2Int gridPosition,
        int width,
        int height,
        FrameCache? frameCache = null,
        bool refreshLru = false)
    {
        frameCache?.RecordProbe();
        if (storage?.CellLayer is not { } layer || mapData == null)
        {
            return false;
        }

        int worldWidth = mapData.WorldWidth;
        int worldHeight = mapData.WorldHeight;
        int minX = Mathf.Max(0, gridPosition.x - 1);
        int maxX = Mathf.Min(worldWidth - 1, gridPosition.x + width);
        int unityMinY = Mathf.Max(0, gridPosition.y - 1);
        int unityMaxY = Mathf.Min(worldHeight - 1, gridPosition.y + height);
        if (minX > maxX || unityMinY > unityMaxY)
        {
            return true;
        }

        int serverMinY = CoordinateUtils.UnityToServerY(unityMaxY, worldHeight);
        int serverMaxY = CoordinateUtils.UnityToServerY(unityMinY, worldHeight);

        int chunkSize = layer.ChunkSize;
        int firstChunkX = minX / chunkSize;
        int lastChunkX = maxX / chunkSize;
        int firstChunkY = serverMinY / chunkSize;
        int lastChunkY = serverMaxY / chunkSize;
        frameCache?.PrepareLayer(layer, worldWidth, worldHeight);
        bool resident = true;
        bool missing = false;
        for (int chunkX = firstChunkX; chunkX <= lastChunkX; chunkX++)
        {
            for (int chunkY = firstChunkY; chunkY <= lastChunkY; chunkY++)
            {
                // Request cold disk chunks as well: TryGetCell only probes
                // RAM and can wait forever without initiating a load.
                ChunkReadStatus status = frameCache == null
                    ? layer.ReadChunk(chunkY + (chunkX * layer.HeightChunks), touchLru: true).Status
                    : frameCache.ReadStatus(
                        layer,
                        chunkY + (chunkX * layer.HeightChunks),
                        refreshLru);
                resident &= status == ChunkReadStatus.Available;
                missing |= status == ChunkReadStatus.Missing;
            }
        }

        if (missing && connectionService is IWorldRegionRequester requester)
        {
            // Заказ выравнивается по границам чанков и берётся с запасом в
            // чанк во все стороны. Причина — не запас как таковой, а
            // УСТОЙЧИВОСТЬ прямоугольника: пока он совпадает с уже заказанным,
            // повторный запрос не нужен. Точный по окну прямоугольник менялся
            // на каждом шаге камеры, каждый шаг порождал новый запрос, а новый
            // запрос отменяет предыдущий — поток чанков рвался ровно тогда,
            // когда игрок шёл.
            int requestMinX = Mathf.Max(0, ((minX / chunkSize) - 1) * chunkSize);
            int requestMinY = Mathf.Max(0, ((serverMinY / chunkSize) - 1) * chunkSize);
            int requestMaxX = Mathf.Min(worldWidth - 1, (((maxX / chunkSize) + 2) * chunkSize) - 1);
            int requestMaxY = Mathf.Min(worldHeight - 1, (((serverMaxY / chunkSize) + 2) * chunkSize) - 1);
            requester.RequestWorldRegion(
                storage.GetWorldCodeName(),
                new RectInt(
                    requestMinX,
                    requestMinY,
                    requestMaxX - requestMinX + 1,
                    requestMaxY - requestMinY + 1));
        }

        return resident;
    }
}
