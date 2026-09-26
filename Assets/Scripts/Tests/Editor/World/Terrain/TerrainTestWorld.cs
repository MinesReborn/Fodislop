#nullable enable

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.World;
using Kern.World.Terrain;
using Kern.World.Terrain.Background;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Kern.Tests.World;

// Детерминированный мир для тестов террейна.
//
// Вся карта — чистая функция от мировой координаты, поэтому один и тот же
// прямоугольник мира читается одинаково сколько угодно раз и в любом порядке.
// Это и делает фикстуру годной как оракул: расхождение результата может прийти
// только из самого террейна, а не из источника данных.
public sealed class TerrainTestWorld
{
    public const int WorldWidth = 256;
    public const int WorldHeight = 256;
    private const int ChunkSize = 32;

    private readonly FakeLayer _layer = new();
    private readonly FakeStorage _storage;
    private readonly FakeMapData _mapData = new();
    private readonly FakeTextures _textures = new();
    private readonly IReadOnlyList<IAtlasDescriptor> _atlases = new IAtlasDescriptor[] { new FakeAtlas() };

    public TerrainTestWorld()
    {
        _storage = new FakeStorage(_layer);
    }

    public IWorldDataStorage Storage => _storage;

    public IMapDataProvider MapData => _mapData;

    public ITextureService Textures => _textures;

    public IReadOnlyList<IAtlasDescriptor> Atlases => _atlases;

    public bool DoorTextureReady
    {
        get => _textures.DoorTextureReady;
        set => _textures.DoorTextureReady = value;
    }

    public bool RoadTextureReady
    {
        get => _textures.RoadTextureReady;
        set => _textures.RoadTextureReady = value;
    }

    // Порода с полостями из хеша координаты: те же клетки, что у соседних
    // тестов заливки фона, чтобы один и тот же мир читался узнаваемо.
    public static CellType CellAt(int worldX, int serverY)
    {
        if ((uint)worldX >= WorldWidth || (uint)serverY >= WorldHeight)
        {
            return CellType.Unloaded;
        }

        uint hash = (uint)((worldX * 374761393) ^ (serverY * 668265263));
        hash = (hash ^ (hash >> 13)) * 1274126177;
        int noise = (int)((hash ^ (hash >> 16)) % 100);
        return noise switch
        {
            < 46 => CellType.Rock,
            < 54 => CellType.RedRock,
            < 60 => CellType.BuildingWall,
            < 63 => CellType.BuildingDoor,
            < 70 => CellType.Road,
            _ => CellType.Empty,
        };
    }

    // Собранный вход сборки клеток для окна (minX, minY) размера w×h:
    // кэш, предрасчёт и заливка фона уже посчитаны, как в LateUpdate.
    public TerrainCellSources BuildSources(
        TerrainCellCache cache,
        TerrainPrecalculator precalc,
        BackgroundFloodFill floodFill,
        int minX,
        int minY,
        int width,
        int height)
    {
        cache.EnsureCapacity(width, height);
        precalc.EnsureCapacity(width, height);
        floodFill.Allocate(width, height);

        cache.PopulateFull(minX, minY, _storage, _mapData, _textures, _atlases);
        precalc.PrecalculateFull(new TerrainPrecalculationInput(
            cache,
            new Vector2Int(width, height),
            new Vector2Int(WorldWidth, WorldHeight)));
        floodFill.ComputeFull(cache);

        return new TerrainCellSources(
            cache,
            precalc,
            floodFill,
            WorldWidth,
            WorldHeight,
            _atlases);
    }

    private sealed class FakeAtlas : IAtlasDescriptor
    {
        public Texture2D? Texture => null;

        public int Size => 512;

        public bool ContainsCell(CellType cellType) => cellType != CellType.Unloaded;

        public bool IsFullyOpaque(CellType cellType) =>
            cellType is CellType.Rock or CellType.RedRock or CellType.BuildingWall;
    }

    private sealed class FakeMapData : IMapDataProvider
    {
        public ushort WorldWidth => TerrainTestWorld.WorldWidth;

        public ushort WorldHeight => TerrainTestWorld.WorldHeight;

        public Camera MainCamera => throw new NotSupportedException();

        public bool IsStandaloneMode => true;

        public Action? OnWorldInitialized { get; set; }

        public Action? OnWorldDataLoaded { get; set; }

        public CellConfigurationPacket GetCellConfig(CellType type)
        {
            CellConfigProperties properties = type switch
            {
                CellType.Empty or CellType.Road => CellConfigProperties.Passable,
                CellType.BuildingDoor => CellConfigProperties.Passable,
                _ => CellConfigProperties.DropsShadow,
            };
            CellDistortionType distortion = type switch
            {
                CellType.Rock => CellDistortionType.Cause,
                CellType.BuildingWall or CellType.BuildingDoor => CellDistortionType.Block,
                _ => CellDistortionType.Neutral,
            };
            byte reliefGroup = type switch
            {
                CellType.Rock => 1,
                CellType.RedRock => 4,
                _ => 0,
            };
            return new CellConfigurationPacket(
                properties,
                distortion,
                CellAnimationType.None,
                AnimationSpeed: 0,
                FrameOffset: 0,
                Color: unchecked((int)0xFF204060),
                reliefGroup);
        }

        public float GetMoveCooldown(CellType cellType) => 0f;

        public bool TryGetTileGroup(CellType type, out int groupID)
        {
            if (type == CellType.Rock)
            {
                groupID = 1;
                return true;
            }

            groupID = 0;
            return false;
        }

        public Color GetCellMinimapColor(CellType type) => new(0.25f, 0.35f, 0.45f, 1f);

        public Color32 GetCellMinimapColor32(CellType type) => new(64, 89, 115, 255);

        public void UpdateMovementSpeeds(MovementSpeedPacket packet) =>
            throw new NotSupportedException();

        public void LoadWorldInit(WorldInitPacket packet) => throw new NotSupportedException();

        public void ResetWorldState() => throw new NotSupportedException();
    }

    private sealed class FakeTextures : ITextureService
    {
        public bool DoorTextureReady { get; set; } = true;
        public bool RoadTextureReady { get; set; } = true;

        public event Action<string, Texture2D>? OnTextureLoaded
        {
            add { }
            remove { }
        }

        public int PendingCellTextureRequests => 0;

        public Texture2D? PrismaticFlowMapTexture => null;

        public Texture2D? FlowMapTexture => null;

        public Texture2D? TerrainDecalAtlasTexture => null;
        public Texture2D? TerrainDecalStoneAtlasTexture => null;

        public void RequestTexture(CellType cellType)
        {
        }

        public AtlasCoordinate GetCellTextureCoordinate(CellType cellType) => default;

        // Каждый тип получает свой прямоугольник в атласе: тексели клеток
        // разных типов обязаны отличаться, иначе тест ничего не докажет.
        public Vector4 GetCellFrameRect(CellType cellType)
        {
            if ((cellType == CellType.BuildingDoor && !DoorTextureReady) ||
                (cellType == CellType.Road && !RoadTextureReady))
            {
                return Vector4.zero;
            }

            int id = (int)cellType;
            return new Vector4(
                (id % 16) / 16f,
                ((id / 16) % 16) / 16f,
                1f / 16f,
                1f / 16f);
        }

        public int GetAnimationFrameCount(CellType cellType) => 1;

        public int GetFrameSize(CellType cellType) => 32;

        public float GetAnimationSpeedForCell(CellType cellType) => 0f;

        public UniTask<AtlasCoordinate> GetCellTextureCoordinate(
            CellType cellType,
            int globalX,
            int globalY) => throw new NotSupportedException();

        public IReadOnlyList<IAtlasDescriptor> GetAllAtlases() =>
            throw new NotSupportedException();

        public string GetCacheStats() => string.Empty;

        public void FlushDirtyAtlases()
        {
        }

        public void Clear()
        {
        }
    }

    private sealed class FakeStorage(FakeLayer layer) : IWorldDataStorage
    {
        public event Action<int, int>? CellChanged
        {
            add { }
            remove { }
        }

        public event Action<int, int, int, int>? RegionChanged
        {
            add { }
            remove { }
        }

        public bool IsReady => true;

        public long Revision => 1;

        public IWorldLayer<CellType>? CellLayer => layer;

        public CellType GetCell(int x, int y) => CellAt(x, y);

        public bool TryGetCell(int x, int y, out CellType cellType)
        {
            cellType = CellAt(x, y);
            return cellType != CellType.Unloaded;
        }

        public string GetWorldCodeName() => "test";

        public bool IsInitialized() => true;

        public void SetCell(int x, int y, CellType type) => throw new NotSupportedException();

        public void SetRegion(int startX, int startY, int width, int height, CellType[] cells) =>
            throw new NotSupportedException();

        public void SetRegion(int startX, int startY, int width, int height, ReadOnlySpan<CellType> cells) =>
            throw new NotSupportedException();

        public void InitWorld(string worldCodeName, int width, int height) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }

        public UniTask DisposeAsync(System.Threading.CancellationToken cancellationToken = default) =>
            UniTask.CompletedTask;

        public void Flush()
        {
        }

        public UniTask FlushAsync(bool durable, System.Threading.CancellationToken cancellationToken = default) =>
            UniTask.CompletedTask;

#if UNITY_EDITOR
        public void EnsureEditorInitialized()
        {
        }
#endif
    }

    private sealed class FakeLayer : IWorldLayer<CellType>
    {
        private readonly Dictionary<int, CellType[]> _chunks = [];

        public event Action<int, int, int, int>? ChunkLoaded
        {
            add { }
            remove { }
        }

        public int ChunkSize => TerrainTestWorld.ChunkSize;

        public int WidthChunks => WorldWidth / TerrainTestWorld.ChunkSize;

        public int HeightChunks => WorldHeight / TerrainTestWorld.ChunkSize;

        public int MaxChunksInMemory => int.MaxValue;

        public bool HasDirtyChunks => false;

        public CellType this[int x, int y]
        {
            get => CellAt(x, y);
            set => throw new NotSupportedException();
        }

        public bool GetChunkIndexAndLocal(int x, int y, out int chunkIndex, out int localIndex)
        {
            if ((uint)x >= WorldWidth || (uint)y >= WorldHeight)
            {
                chunkIndex = -1;
                localIndex = -1;
                return false;
            }

            int chunkX = x / TerrainTestWorld.ChunkSize;
            int chunkY = y / TerrainTestWorld.ChunkSize;
            chunkIndex = chunkY + (chunkX * HeightChunks);
            localIndex = ((x % TerrainTestWorld.ChunkSize) * TerrainTestWorld.ChunkSize) +
                (y % TerrainTestWorld.ChunkSize);
            return true;
        }

        public ChunkReadResult<CellType> ReadChunk(int chunkIndex, bool touchLru = true)
        {
            if (chunkIndex < 0)
            {
                return new ChunkReadResult<CellType>(ChunkReadStatus.Missing, null, null);
            }

            if (!_chunks.TryGetValue(chunkIndex, out CellType[]? chunk))
            {
                chunk = new CellType[TerrainTestWorld.ChunkSize * TerrainTestWorld.ChunkSize];
                int chunkX = chunkIndex / HeightChunks;
                int chunkY = chunkIndex % HeightChunks;
                for (int localX = 0; localX < TerrainTestWorld.ChunkSize; localX++)
                {
                    for (int localY = 0; localY < TerrainTestWorld.ChunkSize; localY++)
                    {
                        chunk[(localX * TerrainTestWorld.ChunkSize) + localY] = CellAt(
                            (chunkX * TerrainTestWorld.ChunkSize) + localX,
                            (chunkY * TerrainTestWorld.ChunkSize) + localY);
                    }
                }

                _chunks.Add(chunkIndex, chunk);
            }

            return new ChunkReadResult<CellType>(ChunkReadStatus.Available, chunk, null);
        }

        public CellType GetCell(int x, int y, bool touchLru = true) => CellAt(x, y);

        public CellType GetCellSync(int x, int y, bool touchLru = true) => CellAt(x, y);

        public bool TryGetCell(int x, int y, out CellType value)
        {
            value = CellAt(x, y);
            return value != CellType.Unloaded;
        }

        public CellType[] GetOrCreateChunk(int chunkIndex, bool touchLru = true) =>
            ReadChunk(chunkIndex, touchLru).Data!;

        public IEnumerable<int> GetLoadedChunkIndices() => _chunks.Keys;

        public int GetLoadedCount() => _chunks.Count;

        public int GetDirtyCount() => 0;

        public void NotifyRegionLoaded(int startX, int startY, int width, int height)
        {
        }

        public void SetCell(int x, int y, CellType value) => throw new NotSupportedException();

        public int SetRegion(int startX, int startY, int width, int height, CellType[] cells, int cellsOffset = 0) =>
            throw new NotSupportedException();

        public int SetRegion(int startX, int startY, int width, int height, ReadOnlySpan<CellType> cells, int cellsOffset = 0) =>
            throw new NotSupportedException();

        public void Flush(bool flushToDisk = false)
        {
        }

        public void Dispose()
        {
        }
    }
}
