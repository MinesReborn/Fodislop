#nullable enable

using System.Collections.Generic;

namespace Kern.UI;

internal sealed class ChatMessageHistory
{
    public const int MaxMessages = 20;

    private readonly List<string> _messages = new();

    public void Add(string formattedMessage)
    {
        _messages.Add(formattedMessage);
        while (_messages.Count > MaxMessages)
        {
            _messages.RemoveAt(0);
        }
    }

    public IReadOnlyList<string> GetMessages() => _messages;
}
