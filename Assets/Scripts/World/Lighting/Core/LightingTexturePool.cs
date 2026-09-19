#nullable enable

using System;
using UnityEngine;

namespace Kern.World.Lighting;

internal static class LightingTexturePool
{
    public static RenderTexture CreateTexture(
        int width,
        int height,
        RenderTextureFormat format,
        bool randomWrite,
        FilterMode filterMode,
        string name,
        bool useMipMap = false)
    {
        var texture = new RenderTexture(
            width,
            height,
            0,
            format,
            RenderTextureReadWrite.Linear)
        {
            enableRandomWrite = randomWrite,
            useMipMap = useMipMap,
            autoGenerateMips = false,
            filterMode = filterMode,
            wrapMode = TextureWrapMode.Clamp,
            name = name,
        };

        if (!texture.Create())
        {
            DestroyLightingObject(texture);
            throw new InvalidOperationException($"Failed to create required lighting target '{name}'.");
        }

        return texture;
    }

    public static void ReleaseTexture(ref RenderTexture? texture)
    {
        if (texture == null)
        {
            return;
        }

        texture.Release();
        DestroyLightingObject(texture);
        texture = null;
    }

    public static void DestroyLightingObject(UnityEngine.Object target)
    {
        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(target);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
