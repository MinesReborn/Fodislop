#nullable enable

using System;

namespace Kern.Core.Interfaces;
public enum WorldLoadPhase
{
    Handshake = 0,

    WorldManifest = 1,

    SpawnSync = 2,

    TerrainMesh = 3,

    SurfaceAssets = 4,

    Done = 5,
}

public interface IWorldLoadProgress
{
    WorldLoadPhase CurrentPhase { get; }

    event Action<WorldLoadPhase>? PhaseChanged;

    void Report(WorldLoadPhase phase);
}
