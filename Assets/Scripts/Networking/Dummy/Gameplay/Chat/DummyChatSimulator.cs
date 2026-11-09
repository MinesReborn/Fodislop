#nullable enable

using System;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.Core;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Chat;
using UnityEngine;

namespace Kern.Networking.Dummy;

internal sealed class DummyChatSimulator
{
    private readonly Action<ServerPacket> _onReceived;
    private readonly Func<int, bool> _loopAlive;
    private readonly IAsyncOperationSupervisor _operations;
    private readonly IDummyClock _clock;
    internal DummyChatSimulator(
    Action<ServerPacket> onReceived,
    Func<int, bool> loopAlive,
    IAsyncOperationSupervisor operations,
    IDummyClock clock)
    {
        _onReceived = onReceived;
        _loopAlive = loopAlive;
        _operations = operations;
        _clock = clock;
    }

    // Имена берём из DummyBotRunner.BotNames — единый источник: чат-«игроки»
    // всегда те же, кого видно на карте, и дубль списка не расходится.
    private static readonly string[] s_messages =
    {
        "gg", "welcome!", "как дела?", "lol", "nice",
        "gl hf", "куда бежать?", "фармим)", "👋", "подскажите кто знает",
    };

    // Реакции на действия игрока — мир ощущается живым.
    private static readonly string[] s_miningReactions =
    {
        "кто-то копает рядом!",
        "красиво копает 🎉",
        "привет, сосед!",
        "ого, добыча пошла",
        "уважаю труд)",
    };

    // Петля принадлежит сессии, в которой запущена. Проверка «жива ли текущая
    // сессия» оставляла петли всех прошлых подключений работать вечно.
    public void SendChatMock(int lifecycleVersion) =>
        _operations.Run("dummy_chat_loop", cancellationToken => SendChatMockAsync(lifecycleVersion, cancellationToken));

    private async UniTask SendChatMockAsync(int lifecycleVersion, CancellationToken cancellationToken)
    {
        while (_loopAlive(lifecycleVersion))
        {
            await _clock.Delay(8000 + _clock.Random.Next(4000), cancellationToken);
            if (!_loopAlive(lifecycleVersion))
            {
                break;
            }

            SendChatLine(DummyBotRunner.BotNames[_clock.Random.Next(DummyBotRunner.BotNames.Length)], s_messages[_clock.Random.Next(s_messages.Length)]);
        }
    }

    public void SendMiningReaction()
    {
        if (_clock.Random.Next(100) >= 25)
        {
            return;
        }

        SendChatLine(DummyBotRunner.BotNames[_clock.Random.Next(DummyBotRunner.BotNames.Length)], s_miningReactions[_clock.Random.Next(s_miningReactions.Length)]);
    }

    private void SendChatLine(string name, string message)
    {
        long now = DummyClockTime.UnixMilliseconds(_clock);
        System.Drawing.Color nickColor = System.Drawing.Color.FromArgb(
            255, _clock.Random.Next(100, 256), _clock.Random.Next(100, 256), _clock.Random.Next(100, 256));

        var chatMsg = new ChatMessagePacket(
            now,
            now,
            _clock.Random.Next(100, 999), (byte)_clock.Random.Next(0, 3),
            nickColor, name,
            System.Drawing.Color.White, message);
        _onReceived.Invoke(new ServerPacket(new ChatMessageListPacket(
            ChatProtocolLimits.GlobalChannelTag,
            new[] { chatMsg })));
    }
}
