#nullable enable

using System;

namespace Kern.Core.Interfaces;

// Совет на экране входа в мир — последний присланный сервером (LoadingTipPacket); пусто — совета нет
public interface ILoadingTip
{
    event Action? Changed;

    string Tip { get; }

    void Set(string tip);
}
