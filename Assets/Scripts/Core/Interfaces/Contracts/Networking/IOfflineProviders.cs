#nullable enable

namespace Kern.Core.Interfaces;
public interface IRuntimeDebugSettings
{
    bool IgnoreCollision { get; set; }
    bool BypassLightingCompute { get; set; }
    bool BypassTerrainDraw { get; set; }
    bool BypassCpuMeshRebuild { get; set; }
    bool ShowRobotDebugVisuals { get; set; }
    bool BypassGameUI { get; set; }
}

public sealed class RuntimeDebugSettings : IRuntimeDebugSettings
{
    public bool IgnoreCollision { get; set; }
    public bool BypassLightingCompute { get; set; }
    public bool BypassTerrainDraw { get; set; }
    public bool BypassCpuMeshRebuild { get; set; }
    public bool ShowRobotDebugVisuals { get; set; }
    public bool BypassGameUI { get; set; }
}
