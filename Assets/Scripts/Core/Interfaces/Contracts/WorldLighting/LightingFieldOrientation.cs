#nullable enable

using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.Core.Interfaces.WorldLighting;

/// <summary>
/// The single owner of how world-space lighting fields are laid out in texture
/// memory. Raster writers (terrain material, emission and AO fields) and every
/// reader (lighting compute, terrain AO sampling, bloom emission sampling,
/// diagnostics readback) take the row order from here, never from
/// <see cref="SystemInfo.graphicsUVStartsAtTop"/> directly.
/// </summary>
public static class LightingFieldOrientation
{
    /// <summary>Global int: 1 when memory row 0 holds the top edge of the field's world rect.</summary>
    public static readonly int RowsTopDownID = Shader.PropertyToID("_KernFieldRowsTopDown");

    private static readonly int _viewID = Shader.PropertyToID("_KernLightingFieldView");
    private static readonly int _projectionID = Shader.PropertyToID("_KernLightingFieldProjection");
    private static readonly int _objectToWorldID = Shader.PropertyToID("_KernLightingFieldObjectToWorld");

    // Field meshes lie at z 0..0.1 (background quads at 0.1). There is no depth
    // buffer and ZTest is Always; the range only has to contain that slab.
    private const float NearPlane = -1f;
    private const float FarPlane = 1f;

    /// <summary>
    /// The field raster uses the unflipped device projection: world top maps to
    /// NDC +Y, which is memory row 0 exactly when the API's texture origin is at
    /// the top. This is the only fact the readers depend on.
    /// </summary>
    public static bool RowsTopDown => SystemInfo.graphicsUVStartsAtTop;

    /// <summary>Memory row of a bottom-up field row <paramref name="y"/>.</summary>
    public static int MemoryRow(int y, int height) => RowsTopDown ? height - 1 - y : y;

    /// <summary>Texture V of a bottom-up field coordinate <paramref name="v"/>.</summary>
    public static float TextureV(float v) => RowsTopDown ? 1f - v : v;

    /// <summary>
    /// Binds the field raster transform as explicit globals for one field pass.
    /// The view keeps translation out of the projection so large world Y does
    /// not quantize field samples. Camera matrices are left untouched: no
    /// implicit Unity projection conversion participates in field rasterization.
    /// </summary>
    public static void BindRaster(CommandBuffer commandBuffer, Vector4 worldRect, Matrix4x4 objectToWorld)
    {
        commandBuffer.SetGlobalMatrix(_viewID, View(worldRect));
        commandBuffer.SetGlobalMatrix(_projectionID, Projection(worldRect));
        commandBuffer.SetGlobalMatrix(_objectToWorldID, objectToWorld);
    }

    /// <summary>Publishes the row order for shaders that sample fields by UV.</summary>
    public static void PublishGlobals() => Shader.SetGlobalInteger(RowsTopDownID, RowsTopDown ? 1 : 0);

    public static Matrix4x4 View(Vector4 worldRect) => Matrix4x4.Translate(new Vector3(
        -(worldRect.x + worldRect.z * 0.5f),
        -(worldRect.y + worldRect.w * 0.5f),
        0f));

    public static Matrix4x4 Projection(Vector4 worldRect) => GL.GetGPUProjectionMatrix(
        Matrix4x4.Ortho(
            -worldRect.z * 0.5f,
            worldRect.z * 0.5f,
            -worldRect.w * 0.5f,
            worldRect.w * 0.5f,
            NearPlane,
            FarPlane),
        renderIntoTexture: false);
}
