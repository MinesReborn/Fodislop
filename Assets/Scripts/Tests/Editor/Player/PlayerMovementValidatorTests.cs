#nullable enable

namespace Kern.Tests.Player;

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.Player.Logic;
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
    public void CalculateMoveCooldown_WithCtrlPressed_UsesEmptyTileCooldown()
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
    public void CalculateMoveCooldown_UnloadedCell_UsesEmptyTileCooldown()
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
    public void IsPassable_EmptyCell_AlwaysReturnsTrue()
    {
        var nonPassableConfig = new CellConfigurationPacket(CellConfigProperties.None, (CellDistortionType)0, CellAnimationType.None, 0, 0, 0, 0);
        Assert.IsTrue(PlayerMovementValidator.IsPassable(CellType.Empty, nonPassableConfig));
    }

    [Test]
    public void IsPassable_UnloadedCell_ReturnsFalse()
    {
        var passableConfig = new CellConfigurationPacket(CellConfigProperties.Passable, (CellDistortionType)0, CellAnimationType.None, 0, 0, 0, 0);
        Assert.IsFalse(PlayerMovementValidator.IsPassable(CellType.Unloaded, passableConfig));
    }

    [Test]
    public void IsPassable_ConfiguredPassable_ReturnsTrue()
    {
        var passableConfig = new CellConfigurationPacket(CellConfigProperties.Passable, (CellDistortionType)0, CellAnimationType.None, 0, 0, 0, 0);
        Assert.IsTrue(PlayerMovementValidator.IsPassable(CellType.BuildingDoor, passableConfig));
    }

    [Test]
    public void IsPassable_SolidTile_ReturnsFalse()
    {
        var solidConfig = new CellConfigurationPacket(CellConfigProperties.None, (CellDistortionType)0, CellAnimationType.None, 0, 0, 0, 0);
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

    private sealed class StubMapDataProvider(
        ushort width = 100,
        ushort height = 100,
        float normalCooldown = 0.3f,
        float emptyCooldown = 0.1f) : IMapDataProvider
    {
        public ushort WorldWidth => width;
        public ushort WorldHeight => height;
        public Camera MainCamera => null!;
        public bool IsStandaloneMode => false;

        public CellConfigurationPacket GetCellConfig(CellType type) =>
            new(type == CellType.Empty ? CellConfigProperties.Passable : CellConfigProperties.None,
                (CellDistortionType)0, CellAnimationType.None, 0, 0, 0, 0);

        public float GetMoveCooldown(CellType cellType) =>
            cellType == CellType.Empty ? emptyCooldown : normalCooldown;

        public bool TryGetTileGroup(CellType type, out int groupID)
        {
            groupID = 0;
            return false;
        }

        public Color GetCellMinimapColor(CellType type) => Color.white;
        public void UpdateMovementSpeeds(MovementSpeedPacket packet) { }
        public void LoadWorldInit(WorldInitPacket packet) { }
        public Action? OnWorldInitialized { get; set; }
        public Action? OnWorldDataLoaded { get; set; }
        public void ResetWorldState() { }
    }

    private sealed class StubWorldStorage(bool cellLayerAvailable, CellType defaultCell) : IWorldDataStorage
    {
        public event Action<int, int>? CellChanged { add { } remove { } }
        public event Action<int, int, int, int>? RegionChanged { add { } remove { } }
        public bool IsReady => true;
        public long Revision => 0;
        public IWorldLayer<CellType>? CellLayer => cellLayerAvailable ? new StubWorldLayer() : null;

        public void SetCell(int x, int y, CellType type) { }
        public void SetRegion(int startX, int startY, int width, int height, CellType[] cells) { }
        public void SetRegion(int startX, int startY, int width, int height, ReadOnlySpan<CellType> cells) { }
        public CellType GetCell(int x, int y) => defaultCell;
        public bool TryGetCell(int x, int y, out CellType cellType)
        {
            cellType = defaultCell;
            return cellLayerAvailable;
        }
        public void InitWorld(string worldCodeName, int width, int height) { }
        public void Dispose() { }
        public UniTask DisposeAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;
        public void Flush() { }
        public UniTask FlushAsync(bool durable, CancellationToken cancellationToken = default) => UniTask.CompletedTask;
        public bool IsInitialized() => true;
        public string GetWorldCodeName() => "test";
        public void EnsureEditorInitialized() { }
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
        public IEnumerable<int> GetLoadedChunkIndices() => [];
        public int GetLoadedCount() => 0;
        public int GetDirtyCount() => 0;
        public CellType GetCell(int x, int y, bool touchLru = true) => CellType.Empty;
        public CellType GetCellSync(int x, int y, bool touchLru = true) => CellType.Empty;
        public bool TryGetCell(int x, int y, out CellType value)
        {
            value = CellType.Empty;
            return true;
        }

        public void SetCell(int x, int y, CellType value) { }
        public int SetRegion(int startX, int startY, int width, int height, CellType[] cells, int cellsOffset = 0) => 0;
        public int SetRegion(int startX, int startY, int width, int height, ReadOnlySpan<CellType> cells, int cellsOffset = 0) => 0;
        public CellType[] GetOrCreateChunk(int chunkIndex, bool touchLru = true) => [];
        public ChunkReadResult<CellType> ReadChunk(int chunkIndex, bool touchLru = true) => new(ChunkReadStatus.Available, [], null);
        public void Flush(bool flushToDisk = false) { }
        public bool GetChunkIndexAndLocal(int x, int y, out int chunkIndex, out int localIndex) { chunkIndex = 0; localIndex = 0; return true; }
        public void Dispose() { }
    }
}
