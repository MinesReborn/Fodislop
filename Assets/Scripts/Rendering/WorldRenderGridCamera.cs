#nullable enable

using System;
using UnityEngine;

namespace Kern.Rendering;

/// <summary>
/// Camera binding for the renderer's grid. Captures matrices without changing the
/// original camera, so UI Toolkit/input continue to project through that camera.
/// The renderer owns GPU projection conversion and texture-origin flipping.
/// </summary>
internal readonly struct WorldRenderGridCamera
{
    public WorldRenderGrid Grid { get; }
    public Matrix4x4 ViewMatrix { get; }
    public Matrix4x4 ProjectionMatrix { get; }
    public Matrix4x4 OriginalProjectionMatrix { get; }
    public Vector4 ViewportToWorldUv { get; }

    private WorldRenderGridCamera(WorldRenderGrid grid, Matrix4x4 view,
        Matrix4x4 projection, Matrix4x4 originalProjection, Vector4 viewportToWorldUv)
    {
        Grid = grid;
        ViewMatrix = view;
        ProjectionMatrix = projection;
        OriginalProjectionMatrix = originalProjection;
        ViewportToWorldUv = viewportToWorldUv;
    }

    public static WorldRenderGridCamera Capture(Camera camera)
    {
        if (camera == null)
        {
            throw new ArgumentNullException(nameof(camera));
        }

        if (!camera.orthographic || camera.aspect <= 0f || camera.orthographicSize <= 0f ||
            Quaternion.Angle(camera.transform.rotation, Quaternion.identity) > 0.001f)
        {
            throw new InvalidOperationException("World render grid requires an axis-aligned orthographic camera.");
        }

        Vector3 position = camera.transform.position;
        double height = (double)camera.orthographicSize * 2;
        double width = height * camera.aspect;
        var grid = WorldRenderGrid.Create(position.x - width / 2, position.y - height / 2, width, height);
        Matrix4x4 view = camera.worldToCameraMatrix;
        var projection = Matrix4x4.Ortho(
            (float)(grid.WorldMinX - position.x),
            (float)(grid.WorldMinX + grid.WorldWidth - position.x),
            (float)(grid.WorldMinY - position.y),
            (float)(grid.WorldMinY + grid.WorldHeight - position.y),
            camera.nearClipPlane, camera.farClipPlane);
        (double u, double v) = grid.ViewportToWorldUv(0, 0);
        return new WorldRenderGridCamera(grid, view, projection, camera.projectionMatrix,
            new Vector4((float)(grid.ViewportWidth / grid.WorldWidth),
                (float)(grid.ViewportHeight / grid.WorldHeight), (float)u, (float)v));
    }
}
