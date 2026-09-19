#nullable enable

using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Lighting;

public interface ILightingGeometryContributor
{
    ulong LightingGeometryRevision { get; }

    void RenderLightingFields(CommandBuffer commandBuffer, in LightingFieldContext context);
}

public readonly record struct LightingFieldContext(
    RenderTexture MaterialField,
    RenderTexture EmissionField,
    Vector4 WorldRect);
