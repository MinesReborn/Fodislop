#nullable enable

using System;
using UnityEngine;

namespace Kern.Game;

internal static class WorldEntityGeometry
{
    public static void WriteSprite(
        Vector3[] verts,
        Vector2[] uvs,
        Color32[] colors,
        int[] tris,
        WorldEntityBatchRenderer.SpriteHandle handle,
        Rect atlasRect,
        int vertexOffset,
        int indexOffset)
    {
        Sprite sprite = handle.Sprite ?? throw new InvalidOperationException(
            "An enabled batched sprite requires a Sprite.");
        Rect source = sprite.rect;
        float pixelsPerUnit = sprite.pixelsPerUnit;
        Vector2 pivot = new(
            sprite.pivot.x / source.width,
            sprite.pivot.y / source.height);
        float width = source.width / pixelsPerUnit;
        float height = source.height / pixelsPerUnit;
        float left = -pivot.x * width;
        float right = left + width;
        float bottom = -pivot.y * height;
        float top = bottom + height;

        // Матрица снята опросом кадра, а не спрошена здесь: четыре
        // TransformPoint — это четыре вызова в движок на спрайт, тогда как
        // умножение на готовую матрицу считается на месте.
        Matrix4x4 localToWorld = handle.FrameLocalToWorld;

        verts[vertexOffset] = localToWorld.MultiplyPoint3x4(new Vector3(left, bottom, 0f));
        verts[vertexOffset + 1] = localToWorld.MultiplyPoint3x4(new Vector3(left, top, 0f));
        verts[vertexOffset + 2] = localToWorld.MultiplyPoint3x4(new Vector3(right, bottom, 0f));
        verts[vertexOffset + 3] = localToWorld.MultiplyPoint3x4(new Vector3(right, top, 0f));

        float uMin = atlasRect.xMin + ((source.xMin / sprite.texture.width) * atlasRect.width);
        float uMax = atlasRect.xMin + ((source.xMax / sprite.texture.width) * atlasRect.width);
        float vMin = atlasRect.yMin + ((source.yMin / sprite.texture.height) * atlasRect.height);
        float vMax = atlasRect.yMin + ((source.yMax / sprite.texture.height) * atlasRect.height);
        uvs[vertexOffset] = new Vector2(uMin, vMin);
        uvs[vertexOffset + 1] = new Vector2(uMin, vMax);
        uvs[vertexOffset + 2] = new Vector2(uMax, vMin);
        uvs[vertexOffset + 3] = new Vector2(uMax, vMax);

        Color32 color = handle.Color;
        colors[vertexOffset] = color;
        colors[vertexOffset + 1] = color;
        colors[vertexOffset + 2] = color;
        colors[vertexOffset + 3] = color;

        tris[indexOffset] = vertexOffset;
        tris[indexOffset + 1] = vertexOffset + 1;
        tris[indexOffset + 2] = vertexOffset + 2;
        tris[indexOffset + 3] = vertexOffset + 2;
        tris[indexOffset + 4] = vertexOffset + 1;
        tris[indexOffset + 5] = vertexOffset + 3;
    }
}
