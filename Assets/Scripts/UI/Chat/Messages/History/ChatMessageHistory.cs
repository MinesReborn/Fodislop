#nullable enable

using System.Collections.Generic;

namespace Kern.UI.Chat;

internal sealed class ChatMessageHistory
{
    public const int MaxMessages = 20;

    private readonly List<(string Formatted, long MessageId, string Time)> _messages = new();

    // MessageId — идентификатор сообщения (ChatMessagePacket.Id, 0 = системная
    // строка без клика); из него сервер резолвит отправителя при клике (Cpri).
    // Time — «18:42» для строки игрока, пусто у системной строки
    public void Add(string formattedMessage, long messageId = 0, string time = "")
    {
        _messages.Add((formattedMessage, messageId, time));
        while (_messages.Count > MaxMessages)
        {
            _messages.RemoveAt(0);
        }
    }

    public void Clear()
    {
        _messages.Clear();
    }

    public IReadOnlyList<(string Formatted, long MessageId, string Time)> GetMessages() => _messages;
}
