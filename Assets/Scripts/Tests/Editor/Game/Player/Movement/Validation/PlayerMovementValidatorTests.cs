#nullable enable

using Kern.Game.Player.Movement;
using Kern.Game.Player.Pathfinding;

namespace Kern.Tests.Player;

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.World;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class PlayerMovementValidatorTests
{
    [Test]
    [TestCase(0, 0, 100, 100, true)]
    [TestCase(99, 99, 100, 100, true)]
    [TestCase(-1, 50, 100, 100, false)]
    [TestCase(50, -1, 100, 100, false)]
    [TestCase(100, 50, 100, 100, false)]
    [TestCase(50, 100, 100, 100, false)]
    [TestCase(0, 0, 0, 100, false)]
    [TestCase(0, 0, 100, 0, false)]
    public void IsWithinWorldBounds_CorrectlyValidatesCoordinates(
        int x,
        int y,
        int width,
        int height,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            PlayerMovementValidator.IsWithinWorldBounds(new Vector2Int(x, y), width, height));
    }

    [Test]
    public void CalculateMoveCooldown_NormalMovement_ReturnsTileCooldown()
    {
        var mapProvider = new StubMapDataProvider(normalCooldown: 0.5f, emptyCooldown: 0.1f);

        float cooldown = PlayerMovementValidator.CalculateMoveCooldown(
            mapProvider,
            CellType.Rock,
            isCtrlPressed: false,
            ignoreCollision: false);

        Assert.AreEqual(0.5f, cooldown);
    }

    [Test]
    public void CalculateMoveCooldown_WithCtrlPressed_UsesMinServerCooldown()
    {
        var mapProvider = new StubMapDataProvider(normalCooldown: 0.5f, emptyCooldown: 0.1f);

        float cooldown = PlayerMovementValidator.CalculateMoveCooldown(
            mapProvider,
            CellType.Rock,
            isCtrlPressed: true,
            ignoreCollision: false);

        Assert.AreEqual(0.1f, cooldown);
    }

    [Test]
    public void CalculateMoveCooldown_WithIgnoreCollision_ScalesDownWithMinimum()
    {
        var mapProvider = new StubMapDataProvider(normalCooldown: 0.5f, emptyCooldown: 0.05f);

        float cooldown = PlayerMovementValidator.CalculateMoveCooldown(
            mapProvider,
            CellType.Rock,
            isCtrlPressed: false,
            ignoreCollision: true);

        Assert.AreEqual(0.05f, cooldown, 0.001f);

        float tinyCooldown = PlayerMovementValidator.CalculateMoveCooldown(
            mapProvider,
            CellType.Empty,
            isCtrlPressed: true,
            ignoreCollision: true);

        Assert.AreEqual(0.01f, tinyCooldown, 0.001f);
    }

    [Test]
    public void CalculateMoveCooldown_UnloadedCell_UsesMinServerCooldown()
    {
        var mapProvider = new StubMapDataProvider(normalCooldown: 0.5f, emptyCooldown: 0.1f);

        float cooldown = PlayerMovementValidator.CalculateMoveCooldown(
            mapProvider,
            CellType.Unloaded,
            isCtrlPressed: false,
            ignoreCollision: false);

        Assert.AreEqual(0.1f, cooldown);
    }

    [Test]
    public void CalculateMoveCooldown_PregenerCell_UsesMinServerCooldown()
    {
        var mapProvider = new StubMapDataProvider(normalCooldown: 0.5f, emptyCooldown: 0.1f);

        float cooldown = PlayerMovementValidator.CalculateMoveCooldown(
            mapProvider,
            CellType.Pregener,
            isCtrlPressed: false,
            ignoreCollision: false);

        Assert.AreEqual(0.1f, cooldown);
    }

    [Test]
    public void IsPassable_EmptyCell_FollowsServerFlag()
    {
        var nonPassableConfig = CellPacket.Create();
        Assert.IsFalse(PlayerMovementValidator.IsPassable(CellType.Empty, nonPassableConfig));
    }

    [Test]
    public void IsPassable_UnloadedCell_ReturnsFalse()
    {
        var passableConfig = CellPacket.Create(properties: CellConfigProperties.Passable);
        Assert.IsFalse(PlayerMovementValidator.IsPassable(CellType.Unloaded, passableConfig));
    }

    [Test]
    public void IsPassable_PregenerCell_ReturnsFalse()
    {
        var passableConfig = CellPacket.Create(properties: CellConfigProperties.Passable);
        Assert.IsFalse(PlayerMovementValidator.IsPassable(CellType.Pregener, passableConfig));
    }

    [Test]
    public void IsPassable_ConfiguredPassable_ReturnsTrue()
    {
        var passableConfig = CellPacket.Create(properties: CellConfigProperties.Passable);
        Assert.IsTrue(PlayerMovementValidator.IsPassable(CellType.BuildingDoor, passableConfig));
    }

    [Test]
    public void IsPassable_SolidTile_ReturnsFalse()
    {
        var solidConfig = CellPacket.Create();
        Assert.IsFalse(PlayerMovementValidator.IsPassable(CellType.BuildingWall, solidConfig));
    }

    [Test]
    public void TryEvaluateStep_CellLayerNull_ReturnsFalse()
    {
        var storage = new StubWorldStorage(cellLayerAvailable: false, defaultCell: CellType.Empty);
        var mapProvider = new StubMapDataProvider(100, 100);

        bool evaluated = PlayerMovementValidator.TryEvaluateStep(
            new Vector2Int(10, 10),
            Vector2Int.right,
            mapProvider,
            storage,
            out Vector2Int targetPosition,
            out CellType cellType,
            out bool isPassable);

        Assert.IsFalse(evaluated);
    }

    [Test]
    public void TryEvaluateStep_OutOfBounds_ReturnsFalse()
    {
        var storage = new StubWorldStorage(cellLayerAvailable: true, defaultCell: CellType.Empty);
        var mapProvider = new StubMapDataProvider(10, 10);

        bool evaluated = PlayerMovementValidator.TryEvaluateStep(
            new Vector2Int(9, 5),
            Vector2Int.right,
            mapProvider,
            storage,
            out Vector2Int targetPosition,
            out CellType cellType,
            out bool isPassable);

        Assert.IsFalse(evaluated);
        Assert.AreEqual(new Vector2Int(10, 5), targetPosition);
    }

    [Test]
    public void TryEvaluateStep_InsideBounds_ReturnsPassability()
    {
        var storage = new StubWorldStorage(cellLayerAvailable: true, defaultCell: CellType.Empty);
        var mapProvider = new StubMapDataProvider(100, 100);

        bool evaluated = PlayerMovementValidator.TryEvaluateStep(
            new Vector2Int(10, 10),
            Vector2Int.right,
            mapProvider,
            storage,
            out Vector2Int targetPosition,
            out CellType cellType,
            out bool isPassable);

        Assert.IsTrue(evaluated);
        Assert.AreEqual(new Vector2Int(11, 10), targetPosition);
        Assert.AreEqual(CellType.Empty, cellType);
        Assert.IsTrue(isPassable);
    }

    [Test]
    public void TryEvaluateStep_UnloadedCell_ReturnsFalse()
    {
        var storage = new StubWorldStorage(cellLayerAvailable: true, defaultCell: CellType.Unloaded);
        var mapProvider = new StubMapDataProvider(100, 100);

        bool evaluated = PlayerMovementValidator.TryEvaluateStep(
            new Vector2Int(10, 10),
            Vector2Int.right,
            mapProvider,
            storage,
            out Vector2Int targetPosition,
            out CellType cellType,
            out bool isPassable);

        Assert.IsFalse(evaluated);
        Assert.AreEqual(CellType.Unloaded, cellType);
        Assert.IsFalse(isPassable);
    }

    [Test]
    public void TryEvaluateStep_PregenerCell_ReturnsFalse()
    {
        var storage = new StubWorldStorage(cellLayerAvailable: true, defaultCell: CellType.Pregener);
        var mapProvider = new StubMapDataProvider(100, 100);

        bool evaluated = PlayerMovementValidator.TryEvaluateStep(
            new Vector2Int(10, 10),
            Vector2Int.right,
            mapProvider,
            storage,
            out Vector2Int targetPosition,
            out CellType cellType,
            out bool isPassable);

        Assert.IsFalse(evaluated);
        Assert.AreEqual(CellType.Unloaded, cellType);
        Assert.IsFalse(isPassable);
    }

    [TestCase(CellType.Rock)]
    [TestCase(CellType.Empty)]
    [TestCase(CellType.BlackRock)]
    [TestCase(CellType.BuildingWall)]
    public void ClickPath_DoesNotFilterCellTypes(CellType cell)
    {
        var storage = new StubWorldStorage(true, cell);
        var map = new StubMapDataProvider(width: 3, height: 1);
        var target = new Vector2Int(2, 0);
        List<Vector2Int>? path = ClickPathfinder.FindPath(storage, map, Vector2Int.zero, target);
        Assert.That(path, Is.EqualTo(new[] { new Vector2Int(1, 0), target }));
    }

    [TestCase(0.5f, false)]
    [TestCase(0.01f, true)]
    public void ClickPath_SelectsRouteByServerMovementCooldown(float solidCooldown, bool crossesSolid)
    {
        var storage = new StubWorldStorage(true, CellType.Empty,
            (x, y) => y == 1 && x > 0 && x < 4 ? CellType.Rock : CellType.Empty);
        var map = new StubMapDataProvider(width: 5, height: 3,
            normalCooldown: solidCooldown, emptyCooldown: 0.1f);
        var target = new Vector2Int(4, 1);

        List<Vector2Int>? path = ClickPathfinder.FindPath(storage, map, new Vector2Int(0, 1), target);

        Assert.That(path, Is.Not.Null);
        Assert.That(path![path.Count - 1], Is.EqualTo(target));
        Assert.That(path.Exists(point => storage.GetCell(point.x, point.y) == CellType.Rock),
            Is.EqualTo(crossesSolid));
        Assert.That(path.Count, Is.EqualTo(crossesSolid ? 4 : 6));
    }

    private sealed class StubMapDataProvider : IMapDataProvider
    {
        private readonly ushort _width;
        private readonly ushort _height;
        private readonly float _normalCooldown;
        private readonly float _emptyCooldown;

        public StubMapDataProvider(ushort width = 100, ushort height = 100,
            float normalCooldown = 0.3f, float emptyCooldown = 0.1f)
        {
            _width = width;
            _height = height;
            _normalCooldown = normalCooldown;
            _emptyCooldown = emptyCooldown;
        }

        public ushort WorldWidth => _width;
        public ushort WorldHeight => _height;
        public Camera MainCamera => null!;
        public bool IsStandaloneMode => false;

        public CellConfigurationPacket GetCellConfig(CellType type) =>
            CellPacket.Create(properties: type == CellType.Empty
                ? CellConfigProperties.Passable
                : CellConfigProperties.None);

        public float GetMoveCooldown(CellType cellType) =>
            cellType == CellType.Empty ? _emptyCooldown : _normalCooldown;

        public float GetMinMoveCooldown() => Mathf.Min(_emptyCooldown, _normalCooldown);

        public bool TryGetTileGroup(CellType type, out int groupId)
        {
            groupId = 0;
            return false;
        }

        public Color GetCellMinimapColor(CellType type) => Color.white;

        public Color32 GetCellMinimapColor32(CellType type) => Color.white;
        public void UpdateMovementSpeeds(MovementSpeedPacket packet) { }
        public void LoadWorldInit(WorldInitPacket packet) { }
        public Action? OnWorldInitialized { get; set; }
        public Action? OnWorldDataLoaded { get; set; }
        public void ResetWorldState() { }
    }

    private sealed class StubWorldStorage : IWorldDataStorage
    {
        private readonly bool _cellLayerAvailable;
        private readonly CellType _defaultCell;
        private readonly Func<int, int, CellType>? _cellAt;

        public StubWorldStorage(bool cellLayerAvailable, CellType defaultCell, Func<int, int, CellType>? cellAt = null)
        {
            _cellLayerAvailable = cellLayerAvailable;
            _defaultCell = defaultCell;
            _cellAt = cellAt;
        }

        public event Action<int, int>? CellChanged { add { } remove { } }
        public event Action<int, int, int, int>? RegionChanged { add { } remove { } }
        public bool IsReady => true;
        public long Revision => 0;
        public IWorldLayer<CellType>? CellLayer => _cellLayerAvailable ? new StubWorldLayer() : null;

        public void SetCell(int x, int y, CellType type) { }
        public void SetRegion(int startX, int startY, int width, int height, CellType[] cells) { }
        public void SetRegion(int startX, int startY, int width, int height, ReadOnlySpan<CellType> cells) { }
        public CellType GetCell(int x, int y) => _cellAt?.Invoke(x, y) ?? _defaultCell;
        public bool TryGetCell(int x, int y, out CellType cellType)
        {
            cellType = GetCell(x, y);
            return _cellLayerAvailable;
        }
        public void InitWorld(string worldCodeName, int width, int height) { }
        public void Dispose() { }
        public UniTask DisposeAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;
        public void Flush() { }
        public UniTask FlushAsync(bool durable, CancellationToken cancellationToken = default) => UniTask.CompletedTask;
        public bool IsInitialized() => true;
        public string GetWorldCodeName() => "test";
        public void EnsureEditorInitialized() { }
        public void BeginRegionBatch() { }
        public void EndRegionBatch() { }
    }

    private sealed class StubWorldLayer : IWorldLayer<CellType>
    {
        public event Action<int, int, int, int>? ChunkLoaded { add { } remove { } }
        public int ChunkSize => 16;
        public int WidthChunks => 10;
        public int HeightChunks => 10;
        public int MaxChunksInMemory => 100;
        public bool HasDirtyChunks => false;
        public CellType this[int x, int y] { get => CellType.Empty; set { } }

        public void NotifyRegionLoaded(int startX, int startY, int width, int height) { }
        public IEnumerable<int> GetLoadedChunkIndices() => Array.Empty<int>();
        public int GetLoadedCount() => 0;
        public int GetDirtyCount() => 0;
        public CellType GetCell(int x, int y, bool touchLRU = true) => CellType.Empty;
        public CellType GetCellSync(int x, int y, bool touchLRU = true) => CellType.Empty;
        public bool TryGetCell(int x, int y, out CellType value)
        {
            value = CellType.Empty;
            return true;
        }

        public void SetCell(int x, int y, CellType value) { }
        public int SetRegion(int startX, int startY, int width, int height, CellType[] cells, int cellsOffset = 0) => 0;
        public int SetRegion(int startX, int startY, int width, int height, ReadOnlySpan<CellType> cells, int cellsOffset = 0) => 0;
        public CellType[] GetOrCreateChunk(int chunkIndex, bool touchLRU = true) => Array.Empty<CellType>();
        public ChunkReadResult<CellType> ReadChunk(int chunkIndex, bool touchLRU = true) =>
            new(ChunkReadStatus.Available, Array.Empty<CellType>(), null);
        public void Flush(bool flushToDisk = false) { }
        public bool GetChunkIndexAndLocal(int x, int y, out int chunkIndex, out int localIndex) { chunkIndex = 0; localIndex = 0; return true; }
        public void Dispose() { }
    }
}
