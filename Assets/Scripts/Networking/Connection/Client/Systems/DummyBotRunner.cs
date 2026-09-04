#nullable enable

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace MinesServer.Networking.Connection.Client;

internal static class DummyBotRunner
{
    /// <summary>
    /// Имена ботов-«игроков». Единый источник истины: DummyChatSimulator
    /// ссылается на него, чтобы чат-игроки были теми же, кого видно на карте.
    /// </summary>
    public static readonly string[] BotNames =
    {
        "Mira",
        "Kite",
        "Rook",
        "Nova",
        "Iris",
        "Vex",
    };

    public static async UniTask RunCircularBots(int count, int lifecycleVersion, Action<ServerPacket> sendPacket, Func<bool> loopAlive)
    {
        const int BASE_ID = 1000;
        const float CENTER_X = 30f;
        const float CENTER_Y = 50f;
        string[] names = BotNames;
        var positions = new IHBPacket[count];

        var bots = new List<(ushort id, string name, float cx, float cy, float r, float a, float speed)>();
        for (int i = 0; i < count; i++)
        {
            ushort botId = (ushort)(BASE_ID + i);
            sendPacket(new ServerPacket(new RobotInfoPacket(botId, 1000, 0,
                "Skin/bee.png", "Tail/default.png", names[i % names.Length])));

            float radius = 2.5f + (i % 3);
            float angle = (float)(i * (Math.PI * 2d / count));
            float speed = 0.45f + ((i % 2) * 0.1f);
            bots.Add((botId, names[i % names.Length], CENTER_X, CENTER_Y, radius, angle, speed));
        }

        while (loopAlive())
        {
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
                positions[i] = new RobotPositionPacket(b.id, (ushort)x, (ushort)y, rot);
                bots[i] = (b.id, b.name, b.cx, b.cy, b.r, b.a + (b.speed * 0.1f), b.speed);
            }

            sendPacket(new ServerPacket(new HBPacket(positions)));
            await UniTask.Delay(100);
        }
    }
}
