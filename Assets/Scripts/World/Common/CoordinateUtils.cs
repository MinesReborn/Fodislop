#nullable enable

using System;
using UnityEngine;

namespace Kern.World;

public static class CoordinateUtils
{
    private static int ResolveHeight(int worldHeight)
    {
        if (worldHeight > 0)
        {
            return worldHeight;
        }

        throw new InvalidOperationException(
            "[CoordinateUtils] World height is required for coordinate conversion, " +
            "and must be supplied by the world data owner.");
    }

    public static float ServerToUnityY(int serverY, int worldHeight) =>
        (ResolveHeight(worldHeight) - 1 - serverY) + 0.5f;

    public static int UnityToServerY(float unityY, int worldHeight)
    {
        int h = ResolveHeight(worldHeight);
        int y = Mathf.FloorToInt(unityY);
        if (y < 0 || y >= h)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unityY),
                unityY,
                $"Unity Y must map inside the world height [0, {h}).");
        }

        return h - 1 - y;
    }

    public static Vector3 ServerToUnityPos(int x, int y, int worldHeight, float z = 0f) =>
        new(x + 0.5f, ServerToUnityY(y, worldHeight), z);

    public static Vector2Int UnityToServerPos(Vector3 unityPos, int worldHeight) =>
        new(Mathf.FloorToInt(unityPos.x), UnityToServerY(unityPos.y, worldHeight));
}
