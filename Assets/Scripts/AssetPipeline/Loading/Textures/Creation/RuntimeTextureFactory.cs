#nullable enable

using System;
using System.Runtime.CompilerServices;
using Kern.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.AssetPipeline.Loading;

public enum RuntimeTextureColorSpace
{
    Srgb,
    Linear,
}

public static class RuntimeTextureFactory
{
    private sealed class TextureOpacity
    {
        public TextureOpacity(bool fullyOpaque)
        {
            FullyOpaque = fullyOpaque;
        }

        public bool FullyOpaque { get; }
    }

    private static readonly ConditionalWeakTable<Texture2D, TextureOpacity> s_decodedOpacity = new();
    private static readonly byte[] s_pngSignature =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
    };

    public static bool SupportsTexture2DGPUCopy =>
        (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0;

    public static bool TryGetDecodedOpacity(Texture2D texture, out bool fullyOpaque)
    {
        if (s_decodedOpacity.TryGetValue(texture, out TextureOpacity opacity))
        {
            fullyOpaque = opacity.FullyOpaque;
            return true;
        }

        fullyOpaque = false;
        return false;
    }

    public static Texture2D CreateRGBA32NoMip(
        int width,
        int height,
        string name,
        RuntimeTextureColorSpace colorSpace,
        FilterMode filterMode,
        TextureWrapMode wrapMode)
    {
        return CreateRGBA32(
            width,
            height,
            name,
            colorSpace,
            filterMode,
            wrapMode,
            mipChain: false);
    }

    public static Texture2D CreateRGBA32MipChain(
        int width,
        int height,
        string name,
        RuntimeTextureColorSpace colorSpace,
        FilterMode filterMode,
        TextureWrapMode wrapMode)
    {
        return CreateRGBA32(
            width,
            height,
            name,
            colorSpace,
            filterMode,
            wrapMode,
            mipChain: true);
    }

    private static Texture2D CreateRGBA32(
        int width,
        int height,
        string name,
        RuntimeTextureColorSpace colorSpace,
        FilterMode filterMode,
        TextureWrapMode wrapMode,
        bool mipChain)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                "Runtime texture width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                height,
                "Runtime texture height must be positive.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Runtime texture name cannot be null or whitespace.",
                nameof(name));
        }

        if (width > SystemInfo.maxTextureSize)
        {
            string message = $"Runtime texture '{name}' width exceeds " +
                $"the GPU limit {SystemInfo.maxTextureSize}.";
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                message);
        }

        if (height > SystemInfo.maxTextureSize)
        {
            string message = $"Runtime texture '{name}' height exceeds " +
                $"the GPU limit {SystemInfo.maxTextureSize}.";
            throw new ArgumentOutOfRangeException(
                nameof(height),
                height,
                message);
        }

        var texture = new Texture2D(
            width,
            height,
            TextureFormat.RGBA32,
            mipChain: mipChain,
            linear: colorSpace == RuntimeTextureColorSpace.Linear)
        {
            name = name,
        };
        ApplySampling(texture, filterMode, wrapMode);
        return texture;
    }

    // Половинная точность: столько же каналов, вдвое меньше памяти. Нужна для
    // снятия содержимого HDR-целей на CPU (ReadPixels), где RGBA32 обрезал бы
    // всё ярче единицы, а RGBAFloat стоил бы вдвое больше без выигрыша в
    // точности — цель и так хранит half.
    public static Texture2D CreateRGBAHalfNoMip(
        int width,
        int height,
        string name,
        RuntimeTextureColorSpace colorSpace,
        FilterMode filterMode,
        TextureWrapMode wrapMode)
    {
        ValidateDimensions(width, height, name);
        var texture = new Texture2D(
            width,
            height,
            TextureFormat.RGBAHalf,
            mipChain: false,
            linear: colorSpace == RuntimeTextureColorSpace.Linear)
        {
            name = name,
        };
        ApplySampling(texture, filterMode, wrapMode);
        return texture;
    }

    public static Texture2D CreateRGBAFloatNoMip(
        int width,
        int height,
        string name,
        RuntimeTextureColorSpace colorSpace,
        FilterMode filterMode,
        TextureWrapMode wrapMode)
    {
        ValidateDimensions(width, height, name);
        var texture = new Texture2D(width, height, TextureFormat.RGBAFloat,
            mipChain: false, linear: colorSpace == RuntimeTextureColorSpace.Linear)
        {
            name = name,
        };
        ApplySampling(texture, filterMode, wrapMode);
        return texture;
    }

    public static Texture3D CreateRGBAFloat3DNoMip(
        int size,
        string name,
        FilterMode filterMode,
        TextureWrapMode wrapMode)
    {
        ValidateDimensions(size, size, name);
        var texture = new Texture3D(
            size,
            size,
            size,
            TextureFormat.RGBAFloat,
            mipChain: false)
        {
            name = name,
        };
        ApplySampling(texture, filterMode, wrapMode);
        return texture;
    }
    public static Texture2D DecodeEncodedImageToRGBA32NoMip(
        byte[] data,
        string name,
        RuntimeTextureColorSpace colorSpace,
        FilterMode filterMode,
        TextureWrapMode wrapMode,
        bool makeNoLongerReadable)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (data.Length == 0)
        {
            throw new ArgumentException(
                "Encoded image data cannot be empty.",
                nameof(data));
        }

        if (!EncodedImageHeader.TryReadSize(data, out int encodedWidth, out int encodedHeight))
        {
            throw new InvalidOperationException(
                $"Encoded image '{name}' is not PNG, or its header is damaged.");
        }

        int maximumSide = Math.Min(
            AssetStreamingLimits.MaximumEncodedImageSide,
            SystemInfo.maxTextureSize);
        if (encodedWidth <= 0 || encodedHeight <= 0 ||
            encodedWidth > maximumSide || encodedHeight > maximumSide)
        {
            throw new InvalidOperationException(
                $"Encoded image '{name}' declares {encodedWidth}x{encodedHeight}; " +
                $"the limit is {maximumSide} per side.");
        }

        // Unity already decodes PNG directly into the Texture2D and uploads
        // it. Copying that image through GetPixels32/SetPixels32 allocated a
        // managed Color32[] as large as the image, copied every pixel again,
        // and uploaded a second texture. This path is used for streamed world
        // cells and menu backgrounds, so large PNGs could allocate many MB in
        // one frame. PNG decoding produces RGBA32, which is the format this
        // pipeline exposes to its consumers.
        if (data.AsSpan().StartsWith(s_pngSignature))
        {
            return DecodePngDirect(
                data,
                name,
                colorSpace,
                filterMode,
                wrapMode,
                makeNoLongerReadable);
        }

        Texture2D staging = CreateRGBA32NoMip(
            2,
            2,
            $"Decoding_{name}",
            colorSpace,
            filterMode,
            wrapMode);
        try
        {
            if (!staging.LoadImage(data, markNonReadable: false))
            {
                throw new InvalidOperationException(
                    $"Encoded image '{name}' could not be decoded by Unity.");
            }

            return CopyToRGBA32NoMip(
                staging,
                name,
                colorSpace,
                filterMode,
                wrapMode,
                makeNoLongerReadable);
        }
        finally
        {
            DestroyRuntimeObject(staging);
        }
    }

    private static Texture2D DecodePngDirect(
        byte[] data,
        string name,
        RuntimeTextureColorSpace colorSpace,
        FilterMode filterMode,
        TextureWrapMode wrapMode,
        bool makeNoLongerReadable)
    {
        Texture2D texture = CreateRGBA32NoMip(
            2,
            2,
            name,
            colorSpace,
            filterMode,
            wrapMode);
        var canonicalFormat = texture.graphicsFormat;
        try
        {
            if (!texture.LoadImage(data, markNonReadable: false))
            {
                throw new InvalidOperationException(
                    $"Encoded PNG '{name}' could not be decoded by Unity.");
            }

            // LoadImage can replace the requested graphics format with a
            // platform-selected PNG format. Keep the no-copy path only when
            // it preserves the factory's canonical RGBA32 format; atlas
            // consumers use Graphics.CopyTexture and require an exact match.
            if (texture.graphicsFormat != canonicalFormat)
            {
                Texture2D canonicalTexture = CopyToRGBA32NoMip(
                    texture,
                    name,
                    colorSpace,
                    filterMode,
                    wrapMode,
                    makeNoLongerReadable);
                DestroyRuntimeObject(texture);
                return canonicalTexture;
            }

            ApplySampling(texture, filterMode, wrapMode);
            s_decodedOpacity.Add(texture, new TextureOpacity(IsTextureFullyOpaque(texture)));
            if (makeNoLongerReadable)
            {
                texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            }

            return texture;
        }
        catch
        {
            DestroyRuntimeObject(texture);
            throw;
        }
    }

    private static bool IsTextureFullyOpaque(Texture2D texture)
    {
        Unity.Collections.NativeArray<Color32> pixels = texture.GetPixelData<Color32>(0);
        for (int index = 0; index < pixels.Length; index++)
        {
            if (pixels[index].a < 250)
            {
                return false;
            }
        }

        return true;
    }

    public static Texture2D CopyToRGBA32NoMip(
        Texture2D source,
        string name,
        RuntimeTextureColorSpace colorSpace,
        FilterMode filterMode,
        TextureWrapMode wrapMode,
        bool makeNoLongerReadable)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (!source.isReadable)
        {
            throw new InvalidOperationException(
                $"Texture '{source.name}' must be readable before conversion to RGBA32.");
        }

        Texture2D result = CreateRGBA32NoMip(
            source.width,
            source.height,
            name,
            colorSpace,
            filterMode,
            wrapMode);
        try
        {
            bool fullyOpaque = true;
            if (source.format == TextureFormat.RGBA32)
            {
                Unity.Collections.NativeArray<Color32> pixels = source.GetPixelData<Color32>(0);
                for (int index = 0; index < pixels.Length; index++)
                {
                    if (pixels[index].a < 250)
                    {
                        fullyOpaque = false;
                        break;
                    }
                }

                result.SetPixelData(pixels, 0);
            }
            else
            {
                Color32[] pixels = source.GetPixels32();
                for (int index = 0; index < pixels.Length; index++)
                {
                    if (pixels[index].a < 250)
                    {
                        fullyOpaque = false;
                        break;
                    }
                }

                result.SetPixels32(pixels);
            }

            result.Apply(
                updateMipmaps: false,
                makeNoLongerReadable: makeNoLongerReadable);
            s_decodedOpacity.Add(result, new TextureOpacity(fullyOpaque));
            return result;
        }
        catch
        {
            DestroyRuntimeObject(result);
            throw;
        }
    }

    public static void ApplySampling(
        Texture texture,
        FilterMode filterMode,
        TextureWrapMode wrapMode)
    {
        if (texture == null)
        {
            throw new ArgumentNullException(nameof(texture));
        }

        texture.filterMode = filterMode;
        texture.wrapMode = wrapMode;
        texture.anisoLevel = 0;
    }

    private static void DestroyRuntimeObject(UnityEngine.Object runtimeObject)
    {
        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(runtimeObject);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(runtimeObject);
        }
    }

    private static void ValidateDimensions(int width, int height, string name)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Runtime texture dimensions must be positive.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Runtime texture name cannot be null or whitespace.",
                nameof(name));
        }

        if (width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Runtime texture dimensions exceed the GPU limit.");
        }
    }
}
