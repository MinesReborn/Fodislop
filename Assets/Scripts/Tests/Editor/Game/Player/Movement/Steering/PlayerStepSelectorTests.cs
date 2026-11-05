#nullable enable

using Kern.Game.Player.Movement;

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

// Оракул — нарисованные вручную сетки и выписанные заранее шаги.
// Сетка в серверных координатах: строка — y (0 сверху), столбец — x.
// '.' пусто, '=' дорога (быстрее), '#' порода, '?' не получено, '@' робот.
// Направления — в осях Unity: Up = (0, 1) — это строка выше.
[TestFixture]
public class PlayerStepSelectorTests
{
    private static readonly Vector2Int Up = new(0, 1);
    private static readonly Vector2Int Left = new(-1, 0);
    private static readonly Vector2Int Right = new(1, 0);

    // Удержание дольше задержки включения обхода.
    private const float HeldTime = 10f;

    [Test]
    public void InputToIntent_KeyboardCardinal_IsSingleAxis()
    {
        MovementIntent intent = PlayerDirectionMapping.InputToIntent(new Vector2(1f, 0f));
        Assert.That(intent.Primary, Is.EqualTo(Right));
        Assert.That(intent.IsDiagonal, Is.False);
    }

    [Test]
    public void InputToIntent_KeyboardWPlusA_IsDiagonalUpThenLeft()
    {
        MovementIntent intent = PlayerDirectionMapping.InputToIntent(new Vector2(-0.70710677f, 0.70710677f));
        Assert.That(intent.Primary, Is.EqualTo(Up));
        Assert.That(intent.Secondary, Is.EqualTo(Left));
    }

    [Test]
    public void InputToIntent_StickNearAxis_StaysCardinal()
    {
        // 18° от оси X — сектор «вправо» (до 22.5°).
        MovementIntent intent = PlayerDirectionMapping.InputToIntent(new Vector2(0.95f, 0.31f));
        Assert.That(intent.Primary, Is.EqualTo(Right));
        Assert.That(intent.IsDiagonal, Is.False);
    }

    [Test]
    public void InputToIntent_StickInDiagonalSector_LeadsWithStrongerAxis()
    {
        // 37° — диагональный сектор, X отклонён сильнее.
        MovementIntent intent = PlayerDirectionMapping.InputToIntent(new Vector2(0.8f, 0.6f));
        Assert.That(intent.Primary, Is.EqualTo(Right));
        Assert.That(intent.Secondary, Is.EqualTo(Up));
    }

    [Test]
    public void InputToIntent_NoInput_IsNone()
    {
        Assert.That(PlayerDirectionMapping.InputToIntent(Vector2.zero).IsNone, Is.True);
    }

    [Test]
    public void Diagonal_OpenField_AlternatesAxes()
    {
        var world = new GridWorld(
            ".......",
            ".......",
            ".......",
            "...@...");

        List<Vector2Int> steps = world.Walk(Diagonal(Up, Left), 4, autoDig: false, assist: true);

        Assert.That(steps, Is.EqualTo(new[] { Up, Left, Up, Left }));
    }

    [Test]
    public void Diagonal_ServerCorrection_KeepsTheZigzagPhase()
    {
        var world = new GridWorld(
            ".......",
            ".......",
            "...@...");
        var state = new PlayerStepSelectorState();

        Vector2Int first = world.SelectHeld(state, Diagonal(Up, Left), autoDig: false, assist: true);
        Assert.That(first, Is.EqualTo(Up));
        PlayerStepSelector.NoteStep(state, first);

        // Соседняя коррекция сервера не должна начинать змейку с Primary заново.
        PlayerStepSelector.Resync(state);

        Vector2Int second = world.SelectHeld(state, Diagonal(Up, Left), autoDig: false, assist: true);
        Assert.That(second, Is.EqualTo(Left));
    }

    [Test]
    public void Diagonal_WallAbove_SlidesAlongIt()
    {
        var world = new GridWorld(
            ".......",
            "#######",
            "....@..");

        List<Vector2Int> steps = world.Walk(Diagonal(Up, Left), 3, autoDig: false, assist: true);

        Assert.That(steps, Is.EqualTo(new[] { Left, Left, Left }));
    }

    [Test]
    public void Diagonal_AutoDig_KeepsStrictZigzagIntoRock()
    {
        var world = new GridWorld(
            ".....",
            "#####",
            "..@..");
        var state = new PlayerStepSelectorState();

        Vector2Int step = world.SelectHeld(state, Diagonal(Up, Left), autoDig: true, assist: true);

        Assert.That(step, Is.EqualTo(Up), "With auto-dig the robot digs the next zigzag cell.");
    }

    [Test]
    public void Diagonal_AtWorldEdge_TakesTheAxisInsideTheWorld()
    {
        var world = new GridWorld(
            "...@...",
            ".......");
        var state = new PlayerStepSelectorState();

        Vector2Int step = world.SelectHeld(state, Diagonal(Up, Left), autoDig: true, assist: false);

        Assert.That(step, Is.EqualTo(Left));
    }

    [Test]
    public void Straight_SingleRock_SidestepsAndContinuesForward()
    {
        var world = new GridWorld(
            ".........",
            ".........",
            "....#....",
            "....@....",
            ".........");

        List<Vector2Int> steps = world.Walk(Straight(Up), 3, autoDig: false, assist: true);

        Assert.That(steps[0].x, Is.Not.EqualTo(0), "The first step goes around the rock.");
        Assert.That(steps[1], Is.EqualTo(Up));
        Assert.That(steps[2], Is.EqualTo(Up));
    }

    [Test]
    public void Straight_PicksTheNarrowSideOfAnObstacle()
    {
        var world = new GridWorld(
            ".........",
            ".........",
            ".#####...",
            "..@......",
            ".........");

        List<Vector2Int> steps = world.Walk(Straight(Up), 3, autoDig: false, assist: true);

        Assert.That(steps, Is.EqualTo(new[] { Left, Left, Up }));
    }

    [Test]
    public void Straight_PrefersTheFasterRoadSide()
    {
        var world = new GridWorld(
            "...=.....",
            "...=.....",
            "...=#....",
            "...=@....",
            ".........");

        List<Vector2Int> steps = world.Walk(Straight(Up), 2, autoDig: false, assist: true);

        Assert.That(steps, Is.EqualTo(new[] { Left, Up }));
    }

    [Test]
    public void Straight_WallWiderThanWindow_FacesTheWall()
    {
        var world = new GridWorld(
            "...............",
            "###############",
            ".......@.......");
        var state = new PlayerStepSelectorState();

        Vector2Int step = world.SelectHeld(state, Straight(Up), autoDig: false, assist: true);

        Assert.That(step, Is.EqualTo(Up));
    }

    [Test]
    public void Straight_PocketOpenOnlyBehind_DoesNotRetreat()
    {
        var world = new GridWorld(
            ".......",
            "..###..",
            "..#@#..",
            ".......");
        var state = new PlayerStepSelectorState();

        Vector2Int step = world.SelectHeld(state, Straight(Up), autoDig: false, assist: true);

        Assert.That(step, Is.EqualTo(Up));
    }

    [Test]
    public void Straight_UnloadedCellsAreNotUsedForDetour()
    {
        var world = new GridWorld(
            "?????",
            "??#??",
            "??@??");
        var state = new PlayerStepSelectorState();

        Vector2Int step = world.SelectHeld(state, Straight(Up), autoDig: false, assist: true);

        Assert.That(step, Is.EqualTo(Up));
    }

    [Test]
    public void Straight_UnloadedCellAhead_IsLeftToTheServer()
    {
        var world = new GridWorld(
            ".....",
            "..?..",
            "..@..");
        var state = new PlayerStepSelectorState();

        Vector2Int step = world.SelectHeld(state, Straight(Up), autoDig: false, assist: true);

        Assert.That(step, Is.EqualTo(Up));
    }

    [TestCase(false, false, TestName = "Straight_AssistOff_FacesTheRock")]
    [TestCase(true, true, TestName = "Straight_AutoDigOn_FacesTheRockToDig")]
    public void Straight_WithoutActiveAssist_KeepsOldBehaviour(bool autoDig, bool assist)
    {
        var world = new GridWorld(
            ".....",
            "..#..",
            "..@..");
        var state = new PlayerStepSelectorState();

        Vector2Int step = world.SelectHeld(state, Straight(Up), autoDig, assist);

        Assert.That(step, Is.EqualTo(Up));
    }

    [Test]
    public void Straight_ShortTapTowardsRock_OnlyFacesIt()
    {
        var world = new GridWorld(
            ".....",
            "..#..",
            "..@..");
        var state = new PlayerStepSelectorState();

        Vector2Int tap = world.Select(state, Straight(Up), autoDig: false, assist: true, time: 0f);
        Vector2Int held = world.Select(
            state, Straight(Up), autoDig: false, assist: true, time: PlayerStepSelector.AssistHoldDelay + 0.01f);

        Assert.That(tap, Is.EqualTo(Up), "A tap turns the robot to the rock for manual digging.");
        Assert.That(held.x, Is.Not.EqualTo(0), "Holding the key walks around the rock.");
    }

    [Test]
    public void Planner_SymmetricObstacle_KeepsThePreviouslyChosenSide()
    {
        var world = new GridWorld(
            ".........",
            ".........",
            "....#....",
            "....@....",
            ".........");
        var serverUp = new Vector2Int(0, -1);

        Assert.That(
            ObstacleDetourPlanner.TryPlanFirstStep(
                world.Robot, serverUp, Vector2Int.zero, new Vector2Int(-1, 0), world, world, out Vector2Int keepLeft),
            Is.True);
        Assert.That(
            ObstacleDetourPlanner.TryPlanFirstStep(
                world.Robot, serverUp, Vector2Int.zero, new Vector2Int(1, 0), world, world, out Vector2Int keepRight),
            Is.True);

        Assert.That(keepLeft, Is.EqualTo(new Vector2Int(-1, 0)));
        Assert.That(keepRight, Is.EqualTo(new Vector2Int(1, 0)));
    }

    [Test]
    public void Diagonal_InnerCorner_AssistFindsAWayToKeepProgressing()
    {
        var world = new GridWorld(
            "........",
            "........",
            ".####...",
            ".#@.....",
            "........");
        Vector2Int start = world.Robot;

        world.Walk(Diagonal(Up, Left), 8, autoDig: false, assist: true);

        Vector2Int moved = world.Robot - start;
        int progress = -moved.y + -moved.x;   // вверх (y−) плюс влево (x−)
        Assert.That(progress, Is.GreaterThanOrEqualTo(1), $"The robot is stuck at {world.Robot}.");
    }

    private static MovementIntent Straight(Vector2Int direction) => new(direction, Vector2Int.zero);

    private static MovementIntent Diagonal(Vector2Int primary, Vector2Int secondary) => new(primary, secondary);

    private sealed class GridWorld : IWorldDataStorage, IMapDataProvider
    {
        private const float EmptyCooldown = 0.1f;
        private const float RoadCooldown = 0.05f;
        private readonly CellType[,] _cells;
        private readonly int _width;
        private readonly int _height;

        public GridWorld(params string[] rows)
        {
            _height = rows.Length;
            _width = rows[0].Length;
            _cells = new CellType[_width, _height];
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    char c = rows[y][x];
                    if (c == '@')
                    {
                        Robot = new Vector2Int(x, y);
                    }

                    _cells[x, y] = c switch
                    {
                        '.' or '@' => CellType.Empty,
                        '=' => CellType.Road,
                        '#' => CellType.Rock,
                        '?' => CellType.Unloaded,
                        _ => throw new ArgumentException($"Unknown grid cell '{c}'."),
                    };
                }
            }
        }

        public Vector2Int Robot { get; private set; }

        public Vector2Int Select(PlayerStepSelectorState state, MovementIntent intent, bool autoDig, bool assist, float time) =>
            PlayerStepSelector.Select(state, intent, Robot, this, this, autoDig, assist, ignoreCollision: false, time);

        // Нажатие в момент 0 и выбор, когда клавиша удерживается уже долго.
        public Vector2Int SelectHeld(PlayerStepSelectorState state, MovementIntent intent, bool autoDig, bool assist)
        {
            Select(state, intent, autoDig, assist, time: 0f);
            return Select(state, intent, autoDig, assist, HeldTime);
        }

        // Идёт, пока шаги проходимы; упор в непроходимое записывается и
        // останавливает прогулку.
        public List<Vector2Int> Walk(MovementIntent intent, int maxSteps, bool autoDig, bool assist)
        {
            var state = new PlayerStepSelectorState();
            var steps = new List<Vector2Int>();
            for (int i = 0; i < maxSteps; i++)
            {
                Vector2Int step = i == 0
                    ? SelectHeld(state, intent, autoDig, assist)
                    : Select(state, intent, autoDig, assist, HeldTime);
                steps.Add(step);
                Vector2Int target = Robot + PlayerDirectionMapping.MovementToDeltaServer(step);
                if (!IsInside(target) || !IsOpen(_cells[target.x, target.y]))
                {
                    break;
                }

                PlayerStepSelector.NoteStep(state, step);
                Robot = target;
            }

            return steps;
        }

        private bool IsInside(Vector2Int p) => p.x >= 0 && p.x < _width && p.y >= 0 && p.y < _height;

        private static bool IsOpen(CellType cell) => cell is CellType.Empty or CellType.Road;

        public ushort WorldWidth => (ushort)_width;
        public ushort WorldHeight => (ushort)_height;
        public Camera MainCamera => null!;
        public bool IsStandaloneMode => false;

        public CellConfigurationPacket GetCellConfig(CellType type) =>
            CellPacket.Create(passable: IsOpen(type));

        public float GetMoveCooldown(CellType cellType) =>
            cellType == CellType.Road ? RoadCooldown : EmptyCooldown;

        public float GetMinMoveCooldown() => RoadCooldown;

        public bool TryGetTileGroup(CellType type, out int groupId)
        {
            groupId = 0;
            return false;
        }

        public Color GetCellMinimapColor(CellType type) => Color.white;
        public Color32 GetCellMinimapColor32(CellType type) => Color.white;

        public bool TryGetAudioBanks(out IReadOnlyList<AssetsDefinitionPacket> banks)
        {
            banks = Array.Empty<AssetsDefinitionPacket>();
            return false;
        }

        public void UpdateMovementSpeeds(MovementSpeedPacket packet) { }
        public void LoadWorldInit(WorldInitPacket packet) { }
        public Action? OnWorldInitialized { get; set; }
        public Action? OnWorldDataLoaded { get; set; }
        public bool IsSameWorld(WorldInitPacket packet) => false;
        public void ApplyWorldContent(WorldInitPacket packet) { }
        public void ResetWorldState() { }

        public event Action<int, int>? CellChanged { add { } remove { } }
        public event Action<int, int, int, int>? RegionChanged { add { } remove { } }
        public bool IsReady => true;
        public long Revision => 0;
        public IWorldLayer<CellType>? CellLayer { get; } = new EmptyLayer();

        public void SetCell(int x, int y, CellType type) { }
        public void SetRegion(int startX, int startY, int width, int height, CellType[] cells) { }
        public void SetRegion(int startX, int startY, int width, int height, ReadOnlySpan<CellType> cells) { }
        public CellType GetCell(int x, int y) => _cells[x, y];

        public bool TryGetCell(int x, int y, out CellType cellType)
        {
            cellType = _cells[x, y];
            return cellType != CellType.Unloaded;
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

    private sealed class EmptyLayer : IWorldLayer<CellType>
    {
        public event Action<int, int, int, int>? ChunkLoaded { add { } remove { } }
        public int ChunkSize => 16;
        public int WidthChunks => 1;
        public int HeightChunks => 1;
        public int MaxChunksInMemory => 1;
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
