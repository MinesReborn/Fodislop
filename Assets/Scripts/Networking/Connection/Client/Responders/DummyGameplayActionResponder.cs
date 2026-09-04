#nullable enable

using System;
using Fodinae.Audio;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.Movement;
using MinesServer.Networking.Client.Packets.Utilities;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.Utilities;
using MinesServer.Networking.Server.Packets.World;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyGameplayActionResponder
{
    private const ushort SpawnX = 25;
    private const ushort SpawnY = 50;
    private readonly DummyPlayerSimulationState _playerState;
    private readonly DummyWorldSimulationState _worldState;
    private readonly DummyMovementResponder _movementResponder;
    private readonly DummyMissionRunner _missionRunner;
    private readonly DummyInventoryResponder _inventoryResponder;
    private readonly DummyChatSimulator _chatSimulator;
    private readonly Action<ServerPacket> _sendPacket;
    private readonly ushort _playerBotId;

    public DummyGameplayActionResponder(
        DummyPlayerSimulationState playerState,
        DummyWorldSimulationState worldState,
        DummyMovementResponder movementResponder,
        DummyMissionRunner missionRunner,
        DummyInventoryResponder inventoryResponder,
        DummyChatSimulator chatSimulator,
        Action<ServerPacket> sendPacket,
        ushort playerBotId)
    {
        _playerState = playerState ?? throw new ArgumentNullException(nameof(playerState));
        _worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
        _movementResponder = movementResponder ??
            throw new ArgumentNullException(nameof(movementResponder));
        _missionRunner = missionRunner ?? throw new ArgumentNullException(nameof(missionRunner));
        _inventoryResponder = inventoryResponder ??
            throw new ArgumentNullException(nameof(inventoryResponder));
        _chatSimulator = chatSimulator ?? throw new ArgumentNullException(nameof(chatSimulator));
        _sendPacket = sendPacket ?? throw new ArgumentNullException(nameof(sendPacket));
        _playerBotId = playerBotId;
    }

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
            case ToggleAgressionPacket:
                _sendPacket(new ServerPacket(
                    new AggressionStatePacket(_playerState.ToggleAggression())));
                break;
            case BzPacket:
                HandleDig(packet.X, packet.Y);
                break;
            case SuicidePacket:
                HandleSuicide();
                break;
            case GeoPacket:
                HandleGeology();
                break;
            case HealPacket:
                _sendPacket(new ServerPacket(new HealthPacket(_playerState.Heal(50), 500)));
                break;
            case BuildCyanPacket:
                HandleBuild(CellType.MilitaryBlock);
                break;
            case BuildGrayPacket:
                HandleRoadBuild();
                break;
            case BuildGreenPacket:
                HandleUpgradeBuild(
                [
                    (CellType.Empty, CellType.GreenBlock),
                    (CellType.GreenBlock, CellType.YellowBlock),
                    (CellType.YellowBlock, CellType.RedBlock),
                ]);
                break;
            case BuildWhitePacket:
                HandleUpgradeBuild(
                [
                    (CellType.Empty, CellType.Support),
                    (CellType.Support, CellType.QuadBlock),
                ]);
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

    private void HandleDig(ushort cellX, ushort cellY)
    {
        SendAudio(SFX.Bz, cellX, cellY);
        if (_worldState.HasLayer)
        {
            CellType cellType = _worldState.GetCell(cellX, cellY);
            if (cellType == CellType.Empty)
            {
                return;
            }

            CellConfigurationPacket? cellConfig = _worldState.GetCellConfig(cellType);
            bool isBreakable = cellConfig.HasValue &&
                ((CellConfigProperties)cellConfig.Value.Properties)
                    .HasFlag(CellConfigProperties.Breakable);
            if (!isBreakable)
            {
                return;
            }

            _worldState.SetCell(cellX, cellY, CellType.Empty);
            AddCrystalToBasket(cellType);
            _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
            {
                new MapRegionPacket(cellX, cellY, 0, 0, new[] { CellType.Empty }),
                CreateAudioPacket(SFX.Destroy, cellX, cellY),
            })));
        }

        _missionRunner.OnBlockMined(_inventoryResponder.Items);
        _chatSimulator.SendMiningReaction();
    }

    private void AddCrystalToBasket(CellType cellType)
    {
        int basketIndex = DummyCellConfigurationUtilities.GetCrystalBasketIndex(cellType);
        if (basketIndex < 0)
        {
            return;
        }

        long[]? contents = _playerState.AddToBasket(
            basketIndex,
            UnityEngine.Random.Range(1, 101));
        if (contents != null)
        {
            _sendPacket(new ServerPacket(new BasketPacket(50000, contents)));
        }
    }

    private void HandleSuicide()
    {
        ushort effectX = _playerState.X;
        ushort effectY = _playerState.Y;
        _playerState.Respawn(SpawnX, SpawnY);
        _movementResponder.CancelPath();
        _worldState.SendChunksAround(_playerState.X, _playerState.Y, _sendPacket);
        _sendPacket(new ServerPacket(new HealthPacket(500, 500)));
        _sendPacket(new ServerPacket(new TeleportPacket(SpawnX, SpawnY, false)));
        _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
        {
            new RobotPositionPacket(
                _playerBotId,
                SpawnX,
                SpawnY,
                (byte)_playerState.Direction),
            CreateAudioPacket(SFX.Death, effectX, effectY),
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
        bool isBreakable = cellConfig.HasValue &&
            ((CellConfigProperties)cellConfig.Value.Properties)
                .HasFlag(CellConfigProperties.Breakable);
        if (cellType != CellType.Empty && isBreakable)
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
        _worldState.SetCell(x, y, mapCell);
        _sendPacket(new ServerPacket(new GeologyPacket(
            _playerState.GeologyCount,
            10,
            reportedCell,
            reportedCell.ToString())));
        _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
        {
            new MapRegionPacket(x, y, 0, 0, new[] { mapCell }),
            CreateAudioPacket(SFX.Geology, x, y),
        })));
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

    private void HandleRoadBuild()
    {
        if (!TryGetFrontCell(out ushort frontX, out ushort frontY))
        {
            return;
        }

        if (_worldState.HasLayer && _worldState.GetCell(frontX, frontY) == CellType.Road)
        {
            _worldState.SetCell(frontX, frontY, CellType.Empty);
            _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
            {
                new MapRegionPacket(frontX, frontY, 0, 0, new[] { CellType.Empty }),
            })));
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

    private void SendAudio(SFX effect, ushort x, ushort y)
    {
        _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
        {
            CreateAudioPacket(effect, x, y),
        })));
    }

    private AudioPacket CreateAudioPacket(SFX effect, ushort x, ushort y)
    {
        return new AudioPacket(
            effect,
            _playerBotId,
            x,
            y,
            Array.Empty<StringPairPacket>());
    }
}
