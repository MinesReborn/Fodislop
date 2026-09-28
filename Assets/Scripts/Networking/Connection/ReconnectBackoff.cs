#nullable enable

using System;

namespace Kern.Networking.Connection;

internal sealed class ReconnectBackoff
{
    private static readonly float[] _steps = [1f, 2f, 4f, 8f, 16f, 30f];
    private int _attempt;

    public float CurrentDelay => _steps[Math.Min(_attempt, _steps.Length - 1)];

    public void RecordFailure() => _attempt++;

    public void Reset() => _attempt = 0;
}
