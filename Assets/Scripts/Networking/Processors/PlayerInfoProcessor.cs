#nullable enable

using System;
using Kern.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Kern.Networking.Processors;

public sealed class PlayerInfoProcessor(
    IRobotService robotManager,
    IPlayerStats playerStats,
    IMapDataProvider mapDataProvider,
    ILocalPlayerState localPlayer) :
    IPacketProcessor<PlayerInfoPacket>,
    IPacketProcessor<MovementSpeedPacket>,
    IPacketProcessor<TeleportPacket>,
    IPacketProcessor<RobotInfoPacket>,
    IPacketProcessor<RobotPositionPacket>,
    IPacketProcessor<AutoMineStatePacket>,
    IPacketProcessor<AggressionStatePacket>
{
    public void Process(PlayerInfoPacket packet)
    {
        robotManager.SetLocalPlayerBotID(packet.BotId);
        playerStats.SetNickname(packet.Nickname);

        var player = localPlayer.Current;
        if (player != null)
        {
            if (player.TryGetComponent<IRobotView>(out var robot))
            {
                robot.Initialize(packet.BotId);
            }

            player.Initialize(packet.BotId);
        }
    }

    public void Process(MovementSpeedPacket packet) =>
        mapDataProvider.UpdateMovementSpeeds(packet);

    public void Process(TeleportPacket packet)
    {
        UnityEngine.Debug.Log($"[Probe] Teleport {UnityEngine.Time.realtimeSinceStartup:F3}");
        var player = localPlayer.Current;
        if (player == null)
        {
            throw new InvalidOperationException("[PlayerInfoProcessor] Teleport received before local player was spawned");
        }

        player.UpdateServerPosition(new Vector2Int(packet.X, packet.Y));
        player.ResetDirection();
    }

    public void Process(RobotInfoPacket packet)
    {
        var metadata = new RobotMetadata(
            packet.PlayerId,
            packet.ClanId,
            packet.Name,
            packet.Skin,
            packet.Tail);
        robotManager.UpdateRobotMetadata(packet.BotId, metadata);
    }

    public void Process(RobotPositionPacket packet)
    {
        robotManager.UpdateRobotPosition(packet.BotId, packet.X, packet.Y, packet.Rotation);
        if (packet.BotId != 0 && packet.BotId == robotManager.LocalPlayerBotID)
        {
            localPlayer.Current?.UpdateServerPosition(new Vector2Int(packet.X, packet.Y));
        }
    }

    public void Process(AutoMineStatePacket packet)
    {
        var player = localPlayer.Current;
        if (player != null)
        {
            player.AutoDig = packet.Enabled;
        }
    }

    public void Process(AggressionStatePacket packet)
    {
        var player = localPlayer.Current;
        if (player != null)
        {
            player.Aggression = packet.Enabled;
        }
    }
}
