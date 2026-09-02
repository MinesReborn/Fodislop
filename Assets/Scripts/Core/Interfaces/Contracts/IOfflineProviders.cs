#nullable enable

namespace Fodinae.Core.Interfaces
{
    /// <summary>
    /// Runtime switches consumed by debug tooling and the systems being
    /// diagnosed. The state is application-scoped so gameplay code does not
    /// depend on a concrete debug or offline implementation.
    /// </summary>
    public interface IRuntimeDebugSettings
    {
        bool IgnoreCollision { get; set; }
        bool BypassLightingCompute { get; set; }
        bool BypassTerrainDraw { get; set; }
        bool BypassCpuMeshRebuild { get; set; }
        bool ShowRobotDebugVisuals { get; set; }
    }

    public sealed class RuntimeDebugSettings : IRuntimeDebugSettings
    {
        public bool IgnoreCollision { get; set; }
        public bool BypassLightingCompute { get; set; }
        public bool BypassTerrainDraw { get; set; }
        public bool BypassCpuMeshRebuild { get; set; }
        public bool ShowRobotDebugVisuals { get; set; }
    }
}
