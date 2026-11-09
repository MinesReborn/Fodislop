#nullable enable

using System;
using Kern.Core.Interfaces;

namespace Kern.Core.Lifecycle;

public sealed class LoadingTip : ILoadingTip
{
    public event Action? Changed;

    public string Tip { get; private set; } = string.Empty;

    public void Set(string tip)
    {
        Tip = tip;
        Changed?.Invoke();
    }
}
