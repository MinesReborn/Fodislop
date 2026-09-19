#nullable enable

using UnityEngine;

namespace Kern.UI;

// Точка обзора рига меню без компонента Camera.
//
// Камера в проекте одна — в Bootstrap. Риг рисуется в свою текстуру вручную,
// поэтому поза и проекция считаются здесь теми же формулами, что у Camera:
// вид — обратная к TRS с отражённой Z, проекция — перспектива OpenGL.
internal readonly struct MenuSceneryViewpoint
{
    public const float NearClip = 0.3f;
    public const float FarClip = 60f;

    public MenuSceneryViewpoint(Vector3 position, Quaternion rotation, float aspect)
    {
        Position = position;
        Rotation = rotation;
        Aspect = Mathf.Max(aspect, 0.1f);
    }

    public Vector3 Position { get; }

    public Quaternion Rotation { get; }

    public float Aspect { get; }

    public Matrix4x4 WorldToView =>
        Matrix4x4.TRS(Position, Rotation, new Vector3(1f, 1f, -1f)).inverse;

    public Matrix4x4 Projection =>
        Matrix4x4.Perspective(MenuSceneryFraming.FieldOfView, Aspect, NearClip, FarClip);

    // Как Camera.WorldToViewportPoint: xy в долях кадра от левого нижнего
    // угла, z — глубина вдоль взгляда в мировых единицах.
    public Vector3 WorldToViewport(Vector3 worldPosition)
    {
        Vector3 view = WorldToView.MultiplyPoint3x4(worldPosition);
        float depth = -view.z;
        if (depth <= 0f)
        {
            return new Vector3(0f, 0f, depth);
        }

        Vector3 ndc = Projection.MultiplyPoint(view);
        return new Vector3((ndc.x * 0.5f) + 0.5f, (ndc.y * 0.5f) + 0.5f, depth);
    }
}
