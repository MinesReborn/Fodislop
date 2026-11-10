#nullable enable

using System;
using UnityEngine;

namespace Kern.AssetPipeline.Loading;

public static class RuntimeTextureArrayFactory
{
    public static Texture2DArray CreateRGBA32NoMip(
        int width,
        int height,
        int sliceCount,
        string name,
        RuntimeTextureColorSpace colorSpace,
        FilterMode filterMode,
        TextureWrapMode wrapMode)
    {
        if (width <= 0 || height <= 0 || sliceCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Runtime texture array dimensions and slice count must be positive.");
        }

        if (width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Runtime texture array dimensions exceed the GPU limit.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Runtime texture array name cannot be empty.", nameof(name));
        }

        var texture = new Texture2DArray(
            width,
            height,
            sliceCount,
            TextureFormat.RGBA32,
            mipChain: false,
            linear: colorSpace == RuntimeTextureColorSpace.Linear)
        {
            name = name,
        };
        RuntimeTextureFactory.ApplySampling(texture, filterMode, wrapMode);
        return texture;
    }
}
