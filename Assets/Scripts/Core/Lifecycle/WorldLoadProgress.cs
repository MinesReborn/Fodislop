#nullable enable

using System;
using Kern.Core.Interfaces;

namespace Kern.Core.Lifecycle;

public sealed class WorldLoadProgress : IWorldLoadProgress
{
    public WorldLoadPhase CurrentPhase { get; private set; }

    public event Action<WorldLoadPhase>? PhaseChanged;

    public void Report(WorldLoadPhase phase)
    {
        if (CurrentPhase == phase)
        {
            return;
        }

        CurrentPhase = phase;
        PhaseChanged?.Invoke(phase);
    }
}
