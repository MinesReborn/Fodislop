#nullable enable

using System;

namespace Kern.World.Lighting;

[Flags]
public enum LightingInvalidationFlags
{
    None = 0,
    GeometryChanged = 1 << 0,
    RegionChanged = 1 << 1,
    FieldDirty = 1 << 2,
    StaticEmissionChanged = 1 << 3,
    DynamicLightsChanged = 1 << 4,
    StaticRadianceChanged = 1 << 5,
    DynamicRadianceChanged = 1 << 6,
    CompositeDirty = 1 << 8,
    ReceiverCoverageChanged = 1 << 9,
    All = ~0,
}
