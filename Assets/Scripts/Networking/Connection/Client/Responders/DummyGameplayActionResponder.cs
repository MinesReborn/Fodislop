#nullable enable

using System;
using Kern.Audio;
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

internal sealed class DummyGameplayActionResponder(
    DummyPlayerSimulationState playerState,
    DummyWorldSimulationState worldState,
    DummyMovementResponder movementResponder,
    DummyMissionRunner missionRunner,
    DummyInventoryResponder inventoryResponder,
    DummyChatSimulator chatSimulator,
    IDummyClock clock,
    Action<ServerPacket> sendPacket,
    ushort playerBotID)
{
    private const ushort SpawnX = 25;
    private const ushort SpawnY = 50;

    public void Handle(ActionClientPacket packet)
    {
        switch (packet.Payload)
        {
            case MovePacket move:
                movementResponder.HandleMove(move);
                break;
            case RotatePacket rotate:
                movementResponder.HandleRotate(rotate);
                break;
            case UnmappedKeyPacket:
                break;
            case ToggleAutoDigPacket:
                sendPacket(new ServerPacket(
                    new AutoMineStatePacket(playerState.ToggleAutoDig())));
                break;
            case ToggleAgressionPacket:
                sendPacket(new ServerPacket(
                    new AggressionStatePacket(playerState.ToggleAggression())));
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
                sendPacket(new ServerPacket(new HealthPacket(playerState.Heal(50), 500)));
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
                movementResponder.HandleClick(click);
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
        if (worldState.HasLayer)
        {
            CellType cellType = worldState.GetCell(cellX, cellY);
            if (cellType == CellType.Empty)
            {
                return;
            }

            CellConfigurationPacket? cellConfig = worldState.GetCellConfig(cellType);
            bool isBreakable = cellConfig.HasValue &&
                ((CellConfigProperties)cellConfig.Value.Properties)
                    .HasFlag(CellConfigProperties.Breakable);
            if (!isBreakable)
            {
                return;
            }

            AddCrystalToBasket(cellType);
            SendCellUpdate(cellX, cellY, CellType.Empty, SFX.Destroy);
        }

        missionRunner.OnBlockMined(inventoryResponder.Items);
        chatSimulator.SendMiningReaction();
    }

    private void AddCrystalToBasket(CellType cellType)
    {
        int basketIndex = DummyCellConfigurationUtilities.GetCrystalBasketIndex(cellType);
        if (basketIndex < 0)
        {
            return;
        }

        long[]? contents = playerState.AddToBasket(
            basketIndex,
            clock.Random.Next(1, 101));
        if (contents != null)
        {
            sendPacket(new ServerPacket(new BasketPacket(50000, contents)));
        }
    }

    private void HandleSuicide()
    {
        ushort effectX = playerState.X;
        ushort effectY = playerState.Y;
        playerState.Respawn(SpawnX, SpawnY);
        movementResponder.CancelPath();
        worldState.QueueChunksAround(playerState.X, playerState.Y, sendPacket);
        sendPacket(new ServerPacket(new HealthPacket(500, 500)));
        sendPacket(new ServerPacket(new TeleportPacket(SpawnX, SpawnY, false)));
        sendPacket(new ServerPacket(new HBPacket([
            new RobotPositionPacket(
                playerBotID,
                SpawnX,
                SpawnY,
                (byte)playerState.Direction),
            CreateAudioPacket(SFX.Death, effectX, effectY),
        ])));
    }

    private void HandleGeology()
    {
        if (!TryGetFrontCell(out ushort frontX, out ushort frontY))
        {
            return;
        }

        if (!worldState.HasLayer)
        {
            return;
        }

        CellType cellType = worldState.GetCell(frontX, frontY);
        CellConfigurationPacket? cellConfig = worldState.GetCellConfig(cellType);
        bool isBreakable = cellConfig.HasValue &&
            ((CellConfigProperties)cellConfig.Value.Properties)
                .HasFlag(CellConfigProperties.Breakable);
        if (cellType != CellType.Empty && isBreakable)
        {
            playerState.PushGeology(cellType);
            SetGeologyCell(frontX, frontY, CellType.Empty, cellType);
        }
        else if (playerState.TryPopGeology(out CellType placeType))
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
        sendPacket(new ServerPacket(new GeologyPacket(
            playerState.GeologyCount,
            10,
            reportedCell,
            reportedCell.ToString())));
        SendCellUpdate(x, y, mapCell, SFX.Geology);
    }

    private void HandleBuild(CellType cellType)
    {
        if (!TryGetFrontCell(out ushort frontX, out ushort frontY))
        {
            return;
        }

        DummyBuildHandler.TryBuild(
            worldState.Layer,
            worldState.GetCell,
            worldState.SetCell,
            sendPacket,
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

        if (worldState.HasLayer && worldState.GetCell(frontX, frontY) == CellType.Road)
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
            worldState.Layer,
            worldState.GetCell,
            worldState.SetCell,
            sendPacket,
            frontX,
            frontY,
            upgrades);
    }

    private bool TryGetFrontCell(out ushort frontX, out ushort frontY)
    {
        Vector2Int offset = playerState.Direction switch
        {
            Direction.Down => new Vector2Int(0, 1),
            Direction.Up => new Vector2Int(0, -1),
            Direction.Left => new Vector2Int(-1, 0),
            Direction.Right => new Vector2Int(1, 0),
            _ => Vector2Int.zero,
        };

        int targetX = (int)playerState.X + offset.x;
        int targetY = (int)playerState.Y + offset.y;

        if (targetX < 0 || targetY < 0)
        {
            frontX = 0;
            frontY = 0;
            return false;
        }

        if (worldState.Layer is { } layer)
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

    private void SendCellUpdate(ushort x, ushort y, CellType cell, SFX? effect = null)
    {
        worldState.SetCell(x, y, cell);
        IHBPacket[] packets = effect.HasValue
            ? [new MapRegionPacket(x, y, 0, 0, [cell]), CreateAudioPacket(effect.Value, x, y)]
            : [new MapRegionPacket(x, y, 0, 0, [cell])];
        sendPacket(new ServerPacket(new HBPacket(packets)));
    }

    private void SendAudio(SFX effect, ushort x, ushort y) =>
        sendPacket(new ServerPacket(new HBPacket([CreateAudioPacket(effect, x, y)])));

    private AudioPacket CreateAudioPacket(SFX effect, ushort x, ushort y) =>
        new(effect, playerBotID, x, y, []);
}
