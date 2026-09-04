#nullable enable

using Fodinae.World.Terrain;
using UnityEngine;

namespace Fodinae.World.Lighting.Pipeline
{
    /// <summary>
    /// The resources a lighting stage needs for one frame's dispatch. Plain
    /// data - stages read it, nothing writes it back. A record struct so a
    /// call site that needs per-call scalars (world rect, cell size, dynamic
    /// light count - values that vary within a single frame across multiple
    /// stage invocations) can start from the steady-state instance built once
    /// per frame and override just those fields with a <c>with</c>
    /// expression, instead of every stage's constructor threading its own
    /// subset of the same handful of scalars.
    /// </summary>
    public readonly record struct LightingFrameContext(
        ComputeShader Compute,
        int FieldWidth,
        int FieldHeight,
        int BounceWidth,
        int BounceHeight,
        RenderTexture DirectTexture,
        RenderTexture StaticDirectTexture,
        RenderTexture BounceTexture,
        RenderTexture ResultTexture,
        RenderTexture AutomaticNormalField,
        RenderTexture MaterialField,
        RenderTexture StaticEmissionField,
        RenderTexture DynamicEmissionField,
        Material DynamicEmissionMaterial,
        ComputeBuffer? DynamicLightBuffer,
        TerrainRenderer TerrainRenderer,
        LightingGeometryRegistry GeometryRegistry,
        Vector4 WorldRect = default,
        float CellSize = 0f,
        int DynamicLightCount = 0);
}
