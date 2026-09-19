#nullable enable

using System;

namespace Kern.Core.Interfaces;
public interface IServerConfig
{
    float DigCooldown { get; }
    int MaxGlobalChatLength { get; }
    int MaxLocalChatLength { get; }
    bool IsInitialized { get; }
    event Action OnInitialized;
}
