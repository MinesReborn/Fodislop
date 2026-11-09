#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.Audio;
using Kern.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using MinesServer.Networking.Client.Packets.Movement;
using MinesServer.Networking.Client.Packets.Utilities;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.Utilities;
using MinesServer.Networking.Server.Packets.World;
using MinesServer.Networking.Contracts.Packets;
using UnityEngine;

namespace Kern.Networking.Dummy;

internal sealed class DummyGameplayActionResponder
{
    private readonly DummyPlayerSimulationState _playerState;
    private readonly DummyWorldSimulationState _worldState;
    private readonly DummyMovementResponder _movementResponder;
    private readonly DummyMissionRunner _missionRunner;
    private readonly DummyInventoryResponder _inventoryResponder;
    private readonly DummyChatSimulator _chatSimulator;
    private readonly IDummyClock _clock;
    private readonly Action<ServerPacket> _sendPacket;
    private readonly ushort _playerBotId;
    private readonly IAsyncOperationSupervisor _operations;
    private readonly Func<int> _getLifecycleVersion;
    private readonly Func<int, bool> _loopAlive;
    internal DummyGameplayActionResponder(
    DummyPlayerSimulationState playerState,
    DummyWorldSimulationState worldState,
    DummyMovementResponder movementResponder,
    DummyMissionRunner missionRunner,
    DummyInventoryResponder inventoryResponder,
    DummyChatSimulator chatSimulator,
    IDummyClock clock,
    Action<ServerPacket> sendPacket,
    ushort playerBotId,
    IAsyncOperationSupervisor operations,
    Func<int> getLifecycleVersion,
    Func<int, bool> loopAlive)
    {
        _playerState = playerState;
        _worldState = worldState;
        _movementResponder = movementResponder;
        _missionRunner = missionRunner;
        _inventoryResponder = inventoryResponder;
        _chatSimulator = chatSimulator;
        _clock = clock;
        _sendPacket = sendPacket;
        _playerBotId = playerBotId;
        _operations = operations;
        _getLifecycleVersion = getLifecycleVersion;
        _loopAlive = loopAlive;
    }

    private const ushort SpawnX = 25;
    private const ushort SpawnY = 50;
    private const int MilitaryBlockAssemblyDelayMilliseconds = 5000;

    private readonly Dictionary<(ushort X, ushort Y), long> _militaryBuildVersions = new();
    private long _nextMilitaryBuildVersion;

    public void Handle(ActionClientPacket packet)
    {
        switch (packet.Payload)
        {
            case MovePacket move:
                _movementResponder.HandleMove(move);
                break;
            case RotatePacket rotate:
                _movementResponder.HandleRotate(rotate);
                break;
            case UnmappedKeyPacket:
                break;
            case ToggleAutoDigPacket:
                _sendPacket(new ServerPacket(
                    new AutoMineStatePacket(_playerState.ToggleAutoDig())));
                break;
            case BzPacket:
                HandleDig(packet.X, packet.Y);
                break;
            case SuicidePacket:
                OpenRespawnWindow();
                break;
            case GeoPacket:
                HandleGeology();
                break;
            case HealPacket:
                _sendPacket(new ServerPacket(new HealthPacket(_playerState.Heal(50), 500)));
                _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
                {
                    new VisualPacket(VisualEffect.Heal, _playerBotId, _playerState.X, _playerState.Y,
                        Array.Empty<StringPairPacket>()),
                    CreateAudioPacket(AudioEventType.Heal, _playerState.X, _playerState.Y),
                })));
                break;
            case BuildCyanPacket:
                HandleMilitaryBlockBuild();
                break;
            case BuildGrayPacket:
                HandleRoadBuild();
                break;
            case BuildGreenPacket:
                HandleUpgradeBuild(new[]
                {
                    (CellType.Empty, CellType.GreenBlock),
                    (CellType.GreenBlock, CellType.YellowBlock),
                    (CellType.YellowBlock, CellType.RedBlock),
                });
                break;
            case BuildWhitePacket:
                HandleUpgradeBuild(new[]
                {
                    (CellType.Empty, CellType.Support),
                    (CellType.Support, CellType.QuadBlock),
                });
                break;
            case ClickCellPacket click:
                _movementResponder.HandleClick(click);
                break;
            default:
                // Заглушка — штатный транспорт, а не отладочный: необработанное
                // действие означает, что клиент шлёт то, чего сервер-заглушка
                // не умеет, и без этой ветки оно исчезало бы бесследно.
                Debug.LogError(
                    "[DummyGameplayActionResponder] Действие " +
                    $"'{packet.Payload?.GetType().Name ?? "null"}' не обработано: " +
                    "добавьте ветку сюда либо уберите отправку на клиенте.");
                break;
        }
    }

    internal void HandleGunHit()
    {
        SendAudio(AudioEventType.Hurt, _playerState.X, _playerState.Y);
        _playerState.SetHealth(_playerState.Health - 10);
        if (_playerState.Health == 0)
        {
            _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
            {
                new VisualPacket(VisualEffect.Death, _playerBotId, _playerState.X, _playerState.Y,
                    Array.Empty<StringPairPacket>()),
            })));
            HandleSuicide();
            return;
        }

        _sendPacket(new ServerPacket(new HealthPacket(_playerState.Health, 500)));
        _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
        {
            new VisualPacket(VisualEffect.Hurt, _playerBotId, _playerState.X, _playerState.Y,
                Array.Empty<StringPairPacket>()),
        })));
    }

    private void HandleDig(ushort cellX, ushort cellY)
    {
        SendAudio(AudioEventType.Bz, cellX, cellY);
        _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
        {
            new VisualPacket(
                VisualEffect.Bz,
                _playerBotId,
                cellX,
                cellY,
                Array.Empty<StringPairPacket>())
        })));

        if (_worldState.HasLayer)
        {
            CellType cellType = _worldState.GetCell(cellX, cellY);
            if (cellType == CellType.Empty)
            {
                return;
            }

            CellConfigurationPacket? cellConfig = _worldState.GetCellConfig(cellType);
            bool canMine = cellConfig.HasValue && CellInteractionRules.CanMine(cellConfig.Value);
            if (!canMine)
            {
                return;
            }

            AddCrystalToBasket(cellType, cellX, cellY);
            SendCellUpdate(cellX, cellY, CellType.Empty, AudioEventType.Destroy);
        }

        _missionRunner.OnBlockMined(_inventoryResponder.Items);
        _chatSimulator.SendMiningReaction();
    }

    private void AddCrystalToBasket(CellType cellType, ushort cellX, ushort cellY)
    {
        CrystalType crystal = DummyMiningRewards.GetMinedCrystal(cellType);
        if (crystal == CrystalType.Unknown)
        {
            return;
        }

        int count = _clock.Random.Next(1, 101);
        long[]? contents = _playerState.AddToBasket((int)crystal, count);
        if (contents != null)
        {
            _sendPacket(new ServerPacket(new BasketPacket(50000, contents)));
            string color = crystal switch
            {
                CrystalType.Blue => "b",
                CrystalType.Red => "r",
                CrystalType.Violet => "v",
                CrystalType.White => "w",
                CrystalType.Cyan => "c",
                _ => "g",
            };
            _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
            {
                new VisualPacket(VisualEffect.Crystal, _playerBotId, cellX, cellY, new[]
                {
                    new StringPairPacket("color", color),
                    new StringPairPacket("count", count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                }),
            })));
        }
    }

    private bool _respawnWindowOpen;
    private int _respawnConfirmIndex;
    private int _respawnCancelIndex;

    private void OpenRespawnWindow()
    {
        var cancel = new TextPacket
        {
            Text = "Остаться",
            OnClickContext = ".",
            AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Bottom") },
            Style = new GUIStylePacket { Padding = new Margins(12, 12, 12, 12) },
        };
        var confirm = new TextPacket
        {
            Text = "Вернуться",
            OnClickContext = ".",
            AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Bottom") },
            Style = new GUIStylePacket { Padding = new Margins(12, 12, 12, 12) },
        };
        var root = new DockPanelPacket
        {
            Children = new IGUIComponentPacket[]
            {
                new TextPacket
                {
                    Text = "Возвращение на респаун\nВы потеряете 10% груза, остальной груз упакуется в бокс.",
                    AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Top") },
                },
                cancel,
                confirm,
            },
        };
        _respawnCancelIndex = DummyWindowElements.IndexOf(root, cancel);
        _respawnConfirmIndex = DummyWindowElements.IndexOf(root, confirm);
        _respawnWindowOpen = true;
        _sendPacket(new ServerPacket(new OpenWindowPacket("respawn", 460, 220, root)));
    }

    public void HandleRespawnWindow(ElementClickPacket packet)
    {
        if (!_respawnWindowOpen ||
            (packet.ElementIndex != _respawnCancelIndex && packet.ElementIndex != _respawnConfirmIndex))
        {
            return;
        }

        _respawnWindowOpen = false;
        _sendPacket(new ServerPacket(new CloseWindowPacket()));
        if (packet.ElementIndex == _respawnConfirmIndex)
        {
            HandleSuicide();
        }
    }

    private void HandleSuicide()
    {
        ushort effectX = _playerState.X;
        ushort effectY = _playerState.Y;
        _playerState.Respawn(SpawnX, SpawnY);
        _movementResponder.CancelPath();
        _worldState.QueueChunksAround(_playerState.X, _playerState.Y, _sendPacket);
        _sendPacket(new ServerPacket(new HealthPacket(500, 500)));
        _sendPacket(new ServerPacket(new TeleportPacket(SpawnX, SpawnY)));
        _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
        {
            new RobotPositionPacket(
                _playerBotId,
                SpawnX,
                SpawnY,
                (byte)_playerState.Direction),
            CreateAudioPacket(AudioEventType.Death, effectX, effectY),
        })));
    }

    private void HandleGeology()
    {
        if (!TryGetFrontCell(out ushort frontX, out ushort frontY))
        {
            return;
        }

        if (!_worldState.HasLayer)
        {
            return;
        }

        CellType cellType = _worldState.GetCell(frontX, frontY);
        CellConfigurationPacket? cellConfig = _worldState.GetCellConfig(cellType);
        bool canMine = cellConfig.HasValue && CellInteractionRules.CanMine(cellConfig.Value);
        if (cellType != CellType.Empty && canMine)
        {
            _playerState.PushGeology(cellType);
            SetGeologyCell(frontX, frontY, CellType.Empty, cellType);
        }
        else if (_playerState.TryPopGeology(out CellType placeType))
        {
            SetGeologyCell(frontX, frontY, placeType, placeType);
        }
    }

    private void SetGeologyCell(
        ushort x,
        ushort y,
        CellType mapCell,
        CellType reportedCell)
    {
        _sendPacket(new ServerPacket(new GeologyPacket(
            _playerState.GeologyCount,
            10,
            reportedCell,
            reportedCell.ToString())));
        SendCellUpdate(x, y, mapCell, AudioEventType.Geology);
    }

    private void HandleBuild(CellType cellType)
    {
        if (!TryGetFrontCell(out ushort frontX, out ushort frontY))
        {
            return;
        }

        DummyBuildHandler.TryBuild(
            _worldState.Layer,
            _worldState.GetCell,
            _worldState.SetCell,
            _sendPacket,
            frontX,
            frontY,
            cellType);
    }

    private void HandleMilitaryBlockBuild()
    {
        if (!TryGetFrontCell(out ushort frontX, out ushort frontY) ||
            !DummyBuildHandler.TryBuild(
                _worldState.Layer,
                _worldState.GetCell,
                _worldState.SetCell,
                _sendPacket,
                frontX,
                frontY,
                CellType.MilitaryBlockFrame))
        {
            return;
        }

        long buildVersion = ++_nextMilitaryBuildVersion;
        (ushort X, ushort Y) position = (frontX, frontY);
        _militaryBuildVersions[position] = buildVersion;
        int lifecycleVersion = _getLifecycleVersion();
        _operations.Run(
            "dummy_military_block_assembly",
            cancellationToken => CompleteMilitaryBlockAssemblyAsync(
                frontX,
                frontY,
                buildVersion,
                lifecycleVersion,
                cancellationToken));
    }

    private async UniTask CompleteMilitaryBlockAssemblyAsync(
        ushort x,
        ushort y,
        long buildVersion,
        int lifecycleVersion,
        CancellationToken cancellationToken)
    {
        await _clock.Delay(MilitaryBlockAssemblyDelayMilliseconds, cancellationToken);

        (ushort X, ushort Y) position = (x, y);
        if (!_militaryBuildVersions.TryGetValue(position, out long currentBuildVersion) ||
            currentBuildVersion != buildVersion)
        {
            return;
        }

        _militaryBuildVersions.Remove(position);
        if (!_loopAlive(lifecycleVersion) ||
            !_worldState.TryGetCell(x, y, out CellType cellType) ||
            cellType != CellType.MilitaryBlockFrame)
        {
            return;
        }

        SendCellUpdate(x, y, CellType.MilitaryBlock);
    }

    private void HandleRoadBuild()
    {
        if (!TryGetFrontCell(out ushort frontX, out ushort frontY))
        {
            return;
        }

        if (_worldState.HasLayer && _worldState.GetCell(frontX, frontY) == CellType.Road)
        {
            SendCellUpdate(frontX, frontY, CellType.Empty);
            return;
        }

        HandleBuild(CellType.Road);
    }

    private void HandleUpgradeBuild((CellType From, CellType To)[] upgrades)
    {
        if (!TryGetFrontCell(out ushort frontX, out ushort frontY))
        {
            return;
        }

        DummyBuildHandler.TryUpgradeBuild(
            _worldState.Layer,
            _worldState.GetCell,
            _worldState.SetCell,
            _sendPacket,
            frontX,
            frontY,
            upgrades);
    }

    private bool TryGetFrontCell(out ushort frontX, out ushort frontY)
    {
        Vector2Int offset = _playerState.Direction switch
        {
            Direction.Down => new Vector2Int(0, 1),
            Direction.Up => new Vector2Int(0, -1),
            Direction.Left => new Vector2Int(-1, 0),
            Direction.Right => new Vector2Int(1, 0),
            _ => Vector2Int.zero,
        };

        int targetX = (int)_playerState.X + offset.x;
        int targetY = (int)_playerState.Y + offset.y;

        if (targetX < 0 || targetY < 0)
        {
            frontX = 0;
            frontY = 0;
            return false;
        }

        if (_worldState.Layer is { } layer)
        {
            int worldWidth = layer.WidthChunks * layer.ChunkSize;
            int worldHeight = layer.HeightChunks * layer.ChunkSize;
            if (targetX >= worldWidth || targetY >= worldHeight)
            {
                frontX = 0;
                frontY = 0;
                return false;
            }
        }

        frontX = (ushort)targetX;
        frontY = (ushort)targetY;
        return true;
    }

    private void SendCellUpdate(ushort x, ushort y, CellType cell, AudioEventType? effect = null)
    {
        _worldState.SetCell(x, y, cell);
        IHBPacket[] packets = effect.HasValue
            ? new IHBPacket[] { new MapRegionPacket(x, y, 0, 0, new[] { cell }), CreateAudioPacket(effect.Value, x, y) }
            : new IHBPacket[] { new MapRegionPacket(x, y, 0, 0, new[] { cell }) };
        _sendPacket(new ServerPacket(new HBPacket(packets)));
    }

    private void SendAudio(AudioEventType effect, ushort x, ushort y) =>
        _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[] { CreateAudioPacket(effect, x, y) })));

    private AudioPacket CreateAudioPacket(AudioEventType effect, ushort x, ushort y) =>
        new(effect, _playerBotId, x, y, Array.Empty<StringPairPacket>());
}
