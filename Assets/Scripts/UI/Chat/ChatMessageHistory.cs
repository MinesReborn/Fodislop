#nullable enable

using System.Collections.Generic;

namespace Fodinae.UI;

internal enum ChatChannel
{
    Global,
    Local,
}

internal sealed class ChatMessageHistory
{
    public const int MaxMessages = 20;

    private readonly List<string> _globalMessages = new();
    private readonly List<string> _localMessages = new();

    public void Add(ChatChannel channel, string formattedMessage)
    {
        List<string> messages = channel == ChatChannel.Local
            ? _localMessages
            : _globalMessages;
        messages.Add(formattedMessage);
        while (messages.Count > MaxMessages)
        {
            messages.RemoveAt(0);
        }
    }

    public IReadOnlyList<string> GetMessages(ChatChannel channel)
    {
        return channel == ChatChannel.Local
            ? _localMessages
            : _globalMessages;
    }
}
