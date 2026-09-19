#nullable enable

using System.Collections.Generic;
using Kern.World.Lighting.Quality;
using UnityEngine;

namespace Kern.World.Lighting;

internal readonly record struct LightingFrameRequest(
    Vector4 WorldRect,
    float CellSize,
    int DynamicLightCount,
    bool RebuildFields,
    bool DynamicLightsChanged,
    bool StaticRadianceChanged,
    bool ReuseStaticAtlas,
    Vector2Int RegionDelta,
    IReadOnlyList<RectInt> DirtyRegions,
    bool AllowStaticDependencyMask,
    bool DynamicRadianceChanged,
    bool ClearDynamicRadiance,
    bool BounceDirty,
    bool CompositeDirty,
    LightingQualityMode Quality,
    LightingEngine.DebugView DebugView);

internal readonly record struct LightingFrameResult(
    LightingInvalidationFlags Invalidations,
    bool StaticRadianceChanged,
    bool DynamicRadianceChanged,
    bool DynamicRadianceCleared,
    IReadOnlyList<string> ExecutedStages);
