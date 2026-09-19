#nullable enable

using System;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Kern;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Chat;
using UnityEngine;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyChatSimulator(
    Action<ServerPacket> onReceived,
    Func<int, bool> loopAlive,
    IAsyncOperationSupervisor operations,
    IDummyClock clock)
{
    // Имена берём из DummyBotRunner.BotNames — единый источник: чат-«игроки»
    // всегда те же, кого видно на карте, и дубль списка не расходится.
    private static readonly string[] _messages =
    [
        "gg", "welcome!", "как дела?", "lol", "nice",
        "gl hf", "куда бежать?", "фармим)", "👋", "подскажите кто знает",
    ];

    // Реакции на действия игрока — мир ощущается живым.
    private static readonly string[] _miningReactions =
    [
        "кто-то копает рядом!",
        "красиво копает 🎉",
        "привет, сосед!",
        "ого, добыча пошла",
        "уважаю труд)",
    ];

    // Петля принадлежит сессии, в которой запущена. Проверка «жива ли текущая
    // сессия» оставляла петли всех прошлых подключений работать вечно.
    public void SendChatMock(int lifecycleVersion) =>
        operations.Run("dummy_chat_loop", cancellationToken => SendChatMockAsync(lifecycleVersion, cancellationToken));

    private async UniTask SendChatMockAsync(int lifecycleVersion, CancellationToken cancellationToken)
    {
        while (loopAlive(lifecycleVersion))
        {
            await clock.Delay(8000 + clock.Random.Next(4000), cancellationToken);
            if (!loopAlive(lifecycleVersion))
            {
                break;
            }

            SendChatLine(DummyBotRunner.BotNames[clock.Random.Next(DummyBotRunner.BotNames.Length)], _messages[clock.Random.Next(_messages.Length)]);
        }
    }

    public void SendMiningReaction()
    {
        if (clock.Random.Next(100) >= 25)
        {
            return;
        }

        SendChatLine(DummyBotRunner.BotNames[clock.Random.Next(DummyBotRunner.BotNames.Length)], _miningReactions[clock.Random.Next(_miningReactions.Length)]);
    }

    private void SendChatLine(string name, string message)
    {
        long now = DummyClockTime.UnixMilliseconds(clock);
        System.Drawing.Color nickColor = System.Drawing.Color.FromArgb(
            255, clock.Random.Next(100, 256), clock.Random.Next(100, 256), clock.Random.Next(100, 256));

        var chatMsg = new ChatMessagePacket(
            now,
            now,
            clock.Random.Next(100, 999), (byte)clock.Random.Next(0, 3),
            nickColor, name,
            System.Drawing.Color.White, message);
        onReceived.Invoke(new ServerPacket(new ChatMessageListPacket("global", [chatMsg])));
    }
}
