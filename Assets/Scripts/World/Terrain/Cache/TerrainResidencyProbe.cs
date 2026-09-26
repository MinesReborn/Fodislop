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
/// Полная резидентность (<see cref="IsWindowResident"/>) — строгий критерий
/// «окно готово целиком». Для старта прогрузки достаточно хоть одного
/// доступного чанка (<see cref="HasAnyResidentData"/>): недогруженные места
/// остаются пустыми и зарастают по мере прихода данных (ChunkLoaded → dirty →
/// TryPatch), поэтому ждать «всё или ничего» для первого билда больше не нужно.
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

    /// <summary>
    /// Есть ли в окне хоть один доступный чанк. Ничего не заказывает.
    /// </summary>
    ///
    /// Позволяет начать строить окно с первого пришедшего пакета любого размера:
    /// пока данных нет вовсе, планировщик продолжает ждать; как только пришла
    /// первая пачка чанков — окно строится, а недогруженные области остаются
    /// пустыми и заполняются по мере прихода следующих регионов.
    public static bool HasAnyResidentData(
        IWorldDataStorage? storage,
        IMapDataProvider? mapData,
        Vector2Int gridPosition,
        int width,
        int height)
    {
        WindowChunkGeom geom = WindowChunkGeom.Compute(storage, mapData, gridPosition, width, height);
        if (geom.Layer == null)
        {
            return false;
        }

        if (geom.OutOfWorld)
        {
            return true;
        }

        for (int chunkX = geom.FirstChunkX; chunkX <= geom.LastChunkX; chunkX++)
        {
            for (int chunkY = geom.FirstChunkY; chunkY <= geom.LastChunkY; chunkY++)
            {
                if (geom.ReadChunk(chunkX, chunkY, touchLru: true).Status == ChunkReadStatus.Available)
                {
                    return true;
                }
            }
        }

        return false;
    }

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
        WindowChunkGeom geom = WindowChunkGeom.Compute(storage, mapData, gridPosition, width, height);
        if (geom.Layer == null)
        {
            return false;
        }

        if (geom.OutOfWorld)
        {
            return true;
        }

        frameCache?.PrepareLayer(geom.Layer, geom.WorldWidth, geom.WorldHeight);
        bool resident = true;
        bool missing = false;
        for (int chunkX = geom.FirstChunkX; chunkX <= geom.LastChunkX; chunkX++)
        {
            for (int chunkY = geom.FirstChunkY; chunkY <= geom.LastChunkY; chunkY++)
            {
                int chunkIndex = chunkY + (chunkX * geom.Layer.HeightChunks);
                ChunkReadStatus status = frameCache == null
                    ? geom.ReadChunk(chunkX, chunkY, touchLru: true).Status
                    : frameCache.ReadStatus(geom.Layer, chunkIndex, refreshLru);
                resident &= status == ChunkReadStatus.Available;
                missing |= status == ChunkReadStatus.Missing;
            }
        }

        if (missing && connectionService is IWorldRegionRequester requester)
        {
            // Заказ выравнивается по границам чанков и берётся с запасом в
            // чанк во все стороны, поэтому повторный запрос не сбрасывает
            // ещё идущую загрузку на каждом шаге камеры.
            int chunkSize = geom.Layer.ChunkSize;
            int requestMinX = Mathf.Max(0, (geom.FirstChunkX - 1) * chunkSize);
            int requestMinY = Mathf.Max(0, (geom.FirstChunkY - 1) * chunkSize);
            int requestMaxX = Mathf.Min(geom.WorldWidth - 1, ((geom.LastChunkX + 2) * chunkSize) - 1);
            int requestMaxY = Mathf.Min(geom.WorldHeight - 1, ((geom.LastChunkY + 2) * chunkSize) - 1);
            requester.RequestWorldRegion(
                geom.WorldCodeName,
                new RectInt(
                    requestMinX,
                    requestMinY,
                    requestMaxX - requestMinX + 1,
                    requestMaxY - requestMinY + 1));
        }

        return resident;
    }

    /// <summary>
    /// Геометрия окна в терминах покрывающих чанков и границ мира. Общая для
    /// всех проверок резидентности, чтобы математика (и запроса региона, и
    /// проверки «есть данные») не расходилась.
    /// </summary>
    private readonly struct WindowChunkGeom
    {
        public readonly IWorldLayer<CellType>? Layer;
        public readonly string WorldCodeName;
        public readonly int WorldWidth;
        public readonly int WorldHeight;
        public readonly bool OutOfWorld;
        public readonly int FirstChunkX;
        public readonly int LastChunkX;
        public readonly int FirstChunkY;
        public readonly int LastChunkY;

        private WindowChunkGeom(
            IWorldLayer<CellType>? layer,
            string worldCodeName,
            int worldWidth,
            int worldHeight,
            bool outOfWorld,
            int firstChunkX,
            int lastChunkX,
            int firstChunkY,
            int lastChunkY)
        {
            Layer = layer;
            WorldCodeName = worldCodeName;
            WorldWidth = worldWidth;
            WorldHeight = worldHeight;
            OutOfWorld = outOfWorld;
            FirstChunkX = firstChunkX;
            LastChunkX = lastChunkX;
            FirstChunkY = firstChunkY;
            LastChunkY = lastChunkY;
        }

        public static WindowChunkGeom Compute(
            IWorldDataStorage? storage,
            IMapDataProvider? mapData,
            Vector2Int gridPosition,
            int width,
            int height)
        {
            if (storage?.CellLayer is not { } layer || mapData == null)
            {
                return new WindowChunkGeom(null, string.Empty, 0, 0, false, 0, 0, 0, 0);
            }

            int worldWidth = mapData.WorldWidth;
            int worldHeight = mapData.WorldHeight;
            int minX = Mathf.Max(0, gridPosition.x - 1);
            int maxX = Mathf.Min(worldWidth - 1, gridPosition.x + width);
            int unityMinY = Mathf.Max(0, gridPosition.y - 1);
            int unityMaxY = Mathf.Min(worldHeight - 1, gridPosition.y + height);
            if (minX > maxX || unityMinY > unityMaxY)
            {
                return new WindowChunkGeom(layer, storage.GetWorldCodeName(), worldWidth, worldHeight, true, 0, 0, 0, 0);
            }

            int serverMinY = CoordinateUtils.UnityToServerY(unityMaxY, worldHeight);
            int serverMaxY = CoordinateUtils.UnityToServerY(unityMinY, worldHeight);

            int chunkSize = layer.ChunkSize;
            return new WindowChunkGeom(
                layer,
                storage.GetWorldCodeName(),
                worldWidth,
                worldHeight,
                false,
                minX / chunkSize,
                maxX / chunkSize,
                serverMinY / chunkSize,
                serverMaxY / chunkSize);
        }

        public ChunkReadResult<CellType> ReadChunk(int chunkX, int chunkY, bool touchLru) =>
            Layer!.ReadChunk(chunkY + (chunkX * Layer.HeightChunks), touchLru);
    }
}
