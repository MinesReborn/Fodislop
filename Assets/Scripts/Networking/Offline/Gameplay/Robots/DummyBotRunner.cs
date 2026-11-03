#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Kern.Networking.Offline;

internal static class DummyBotRunner
{
    private const int CommandRobotCount = 100;
    private const ushort StaticCommandBotIdBase = 2000;
    private const ushort CircularCommandBotIdBase = 2100;
    private const float CommandOrbitRadius = 16f;
    private const float CommandOrbitSpeed = 0.45f;

    public static readonly string[] BotNames =
    {
        "Mira",
        "Kite",
        "Rook",
        "Nova",
        "Iris",
        "Vex",
    };

    public static async UniTask RunStaticBotsAround(
        int lifecycleVersion,
        int count,
        ushort centerX,
        ushort centerY,
        IDummyClock clock,
        Action<ServerPacket> sendPacket,
        Func<int, bool> loopAlive,
        CancellationToken cancellationToken)
    {
        List<CommandBot> bots = SpawnCommandBots(
            count,
            centerX,
            centerY,
            0,
            StaticCommandBotIdBase,
            sendPacket,
            moveInCircle: false);
        var positions = new IHBPacket[bots.Count];

        while (loopAlive(lifecycleVersion))
        {
            for (int i = 0; i < bots.Count; i++)
            {
                CommandBot bot = bots[i];
                float radius = 8f + (i / 25) * 4f;
                (ushort x, ushort y) = GetPosition(bot.CenterX, bot.CenterY, radius, bot.InitialAngle);
                positions[i] = new RobotPositionPacket(bot.Id, x, y, GetRotation(bot.InitialAngle));
            }

            sendPacket(new ServerPacket(new HBPacket((IHBPacket[])positions.Clone())));
            await clock.Delay(100, cancellationToken);
        }
    }

    public static async UniTask RunCircularBotsAround(
        int lifecycleVersion,
        ushort centerX,
        ushort centerY,
        IDummyClock clock,
        Action<ServerPacket> sendPacket,
        Func<int, bool> loopAlive,
        CancellationToken cancellationToken)
    {
        long startedAtMilliseconds = DummyClockTime.UnixMilliseconds(clock);
        List<CommandBot> bots = SpawnCommandBots(
            CommandRobotCount,
            centerX,
            centerY,
            startedAtMilliseconds,
            CircularCommandBotIdBase,
            sendPacket,
            moveInCircle: true);
        var positions = new IHBPacket[bots.Count];

        while (loopAlive(lifecycleVersion))
        {
            for (int i = 0; i < bots.Count; i++)
            {
                CommandBot bot = bots[i];
                double angle = bot.InitialAngle +
                    (DummyClockTime.UnixMilliseconds(clock) - bot.StartedAtMilliseconds) / 1000d * CommandOrbitSpeed;
                (ushort x, ushort y) = GetPosition(bot.CenterX, bot.CenterY, CommandOrbitRadius, angle);
                positions[i] = new RobotPositionPacket(bot.Id, x, y, GetRotation(angle));
            }

            sendPacket(new ServerPacket(new HBPacket((IHBPacket[])positions.Clone())));
            await clock.Delay(100, cancellationToken);
        }
    }

    private static List<CommandBot> SpawnCommandBots(
        int count,
        ushort centerX,
        ushort centerY,
        long startedAtMilliseconds,
        ushort botIdBase,
        Action<ServerPacket> sendPacket,
        bool moveInCircle)
    {
        var bots = new List<CommandBot>(count);
        var initialPositions = new IHBPacket[count];

        for (int i = 0; i < count; i++)
        {
            int ringIndex = i / 25;
            float radius = moveInCircle ? CommandOrbitRadius : 8f + ringIndex * 4f;
            double angle = moveInCircle
                ? i * (Math.PI * 2d / count)
                : (i % 25) * (Math.PI * 2d / 25);
            ushort id = checked((ushort)(botIdBase + i));
            (ushort x, ushort y) = GetPosition(centerX, centerY, radius, angle);

            sendPacket(new ServerPacket(new RobotInfoPacket(
                id,
                1000,
                0,
                "Visual/Skin/bee.png",
                "Visual/Tail/default.png",
                $"Test {id}")));

            bots.Add(new CommandBot(id, centerX, centerY, angle, startedAtMilliseconds));
            initialPositions[i] = new RobotPositionPacket(id, x, y, GetRotation(angle));
        }

        sendPacket(new ServerPacket(new HBPacket(initialPositions)));
        return bots;
    }

    private static (ushort X, ushort Y) GetPosition(ushort centerX, ushort centerY, float radius, double angle)
    {
        int x = (int)Math.Round(centerX + Math.Cos(angle) * radius, MidpointRounding.AwayFromZero);
        int y = (int)Math.Round(centerY + Math.Sin(angle) * radius, MidpointRounding.AwayFromZero);
        return ((ushort)Math.Clamp(x, 0, ushort.MaxValue), (ushort)Math.Clamp(y, 0, ushort.MaxValue));
    }

    private static byte GetRotation(double angle)
    {
        double degrees = ((Math.Atan2(Math.Sin(angle), Math.Cos(angle)) * (180d / Math.PI)) + 360d) % 360d;
        return degrees switch
        {
            > 225 and <= 315 => 0,
            > 135 and <= 225 => 1,
            > 45 and <= 135 => 2,
            _ => 3,
        };
    }

    private readonly record struct CommandBot(
        ushort Id,
        ushort CenterX,
        ushort CenterY,
        double InitialAngle,
        long StartedAtMilliseconds);

    public static async UniTask RunCircularBots(
        int count,
        int lifecycleVersion,
        IDummyClock clock,
        Action<ServerPacket> sendPacket,
        Func<bool> loopAlive,
        Func<(ushort X, ushort Y)> getObserverPosition,
        CancellationToken cancellationToken)
    {
        const int BaseId = 1000;
        const float CENTER_X = 30f;
        const float CENTER_Y = 50f;
        const int InterestChunkExtent = 1;
        const int ChunkSize = WorldGridDefaults.ChunkSize;
        string[] names = BotNames;
        HashSet<ushort> visibleLastTick = new(count);
        HashSet<ushort> visibleNow = new(count);
        var positions = new List<IHBPacket>(count);

        var bots = new List<(ushort id, string name, float cx, float cy, float r, float a, float speed)>();
        for (int i = 0; i < count; i++)
        {
            ushort botId = (ushort)(BaseId + i);
            float radius = 2.5f + (i % 3);
            float angle = (float)(i * (Math.PI * 2d / count));
            float speed = 0.45f + ((i % 2) * 0.1f);
            bots.Add((botId, names[i % names.Length], CENTER_X, CENTER_Y, radius, angle, speed));
        }

        while (loopAlive())
        {
            (ushort observerX, ushort observerY) = getObserverPosition();
            int observerChunkX = observerX / ChunkSize;
            int observerChunkY = observerY / ChunkSize;
            positions.Clear();
            visibleNow.Clear();
            for (int i = 0; i < bots.Count; i++)
            {
                var b = bots[i];
                int x = (int)Math.Round(b.cx + (Math.Cos(b.a) * b.r), MidpointRounding.AwayFromZero);
                int y = (int)Math.Round(b.cy + (Math.Sin(b.a) * b.r), MidpointRounding.AwayFromZero);
                double deg = ((Math.Atan2(Math.Sin(b.a), Math.Cos(b.a)) * (180.0 / Math.PI)) + 360) % 360;
                byte rot = deg switch
                {
                    > 225 and <= 315 => 0,
                    > 135 and <= 225 => 1,
                    > 45 and <= 135 => 2,
                    _ => 3,
                };
                int botChunkX = x / ChunkSize;
                int botChunkY = y / ChunkSize;
                if (Math.Abs(botChunkX - observerChunkX) <= InterestChunkExtent &&
                    Math.Abs(botChunkY - observerChunkY) <= InterestChunkExtent)
                {
                    if (!visibleLastTick.Contains(b.id))
                    {
                        sendPacket(new ServerPacket(new RobotInfoPacket(
                            b.id,
                            1000,
                            0,
                            "Visual/Skin/bee.png",
                            "Visual/Tail/default.png",
                            b.name)));
                    }

                    visibleNow.Add(b.id);
                    positions.Add(new RobotPositionPacket(b.id, (ushort)x, (ushort)y, rot));
                }

                bots[i] = (b.id, b.name, b.cx, b.cy, b.r, b.a + (b.speed * 0.1f), b.speed);
            }

            (visibleLastTick, visibleNow) = (visibleNow, visibleLastTick);

            if (positions.Count > 0)
            {
                // The loop reuses positions; queued packets must own a snapshot.
                sendPacket(new ServerPacket(new HBPacket(positions.ToArray())));
            }

            await clock.Delay(100, cancellationToken);
        }
    }
}
