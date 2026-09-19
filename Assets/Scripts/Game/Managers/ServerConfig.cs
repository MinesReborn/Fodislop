#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;

namespace Kern.Game.Managers;

// Чистый сервис контейнера: ни рендера, ни transform (SCENE_STANDARD.md §1).
public sealed class ServerConfig : IServerConfig
{
    public bool IsInitialized => true;

    public event Action? OnInitialized
    {
        add => value?.Invoke();
        remove { }
    }

    public float DigCooldown { get; } = ProjectRuntimeContracts.Gameplay.DefaultDigCooldown;

    public int MaxGlobalChatLength { get; } = ProjectRuntimeContracts.Chat.MaximumGlobalChatLength;

    public int MaxLocalChatLength { get; } = ProjectRuntimeContracts.Chat.MaximumLocalChatLength;
}
