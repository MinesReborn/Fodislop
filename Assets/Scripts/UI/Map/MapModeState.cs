#nullable enable

using System;

namespace Kern.UI;

public sealed class MapModeState
{
    public bool IsOpen { get; private set; }

    public event Action<bool>? Changed;

    public void SetOpen(bool open)
    {
        if (IsOpen == open)
        {
            return;
        }

        IsOpen = open;
        Changed?.Invoke(open);
    }
}
