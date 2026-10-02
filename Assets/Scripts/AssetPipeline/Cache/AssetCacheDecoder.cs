#nullable enable

using System;
using Kern.Core;
using Kern.World;
using UnityEngine;

namespace Kern;

internal static class AssetCacheDecoder
{
    public readonly record struct DecodedTextureResult(
        Texture2D? Texture,
        float FPS,
        int FrameHeight,
        int FrameCount);

    public readonly record struct DecodedAnimationResult(
        Sprite[] Sprites,
        Texture2D Atlas,
        float FPS,
        int FrameHeight,
        int FrameCount);

    public static DecodedTextureResult DecodeTexture(byte[] bytes, string filename)
    {
        // M3G has no magic bytes, so it can only be recognized by the
        // extension (URL query strings included, e.g. image.m3g?v=2).
        if (IsM3g(filename))
        {
            Texture2D m3g = M3gImageDecoder.Decode(bytes);
            m3g.name = $"Cache_M3G_{DateTime.Now.Ticks}";
            return new DecodedTextureResult(m3g, 0f, 0, 0);
        }

        var containerType = AnimationContainerDecoder.DetectType(bytes);
        if (containerType == AnimationContainerDecoder.ContainerType.WebP)
        {
            var decoded = AnimationContainerDecoder.DecodeWebP(bytes);
            if (decoded.Atlas != null)
            {
                decoded.Atlas.name = $"Cache_WebP_{DateTime.Now.Ticks}";
                RuntimeTextureFactory.ApplySampling(
                    decoded.Atlas,
                    FilterMode.Point,
                    TextureWrapMode.Clamp);
            }

            return new DecodedTextureResult(decoded.Atlas, decoded.FPS, decoded.FrameHeight, decoded.FrameCount);
        }

        bool makeNoLongerReadable = RuntimeTextureFactory.SupportsTexture2DGpuCopy;
        Texture2D? staticTex = RuntimeTextureFactory.DecodeEncodedImageToRGBA32NoMip(
            bytes,
            $"Cache_Tex_{DateTime.Now.Ticks}",
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp,
            makeNoLongerReadable: makeNoLongerReadable);

        return new DecodedTextureResult(staticTex, 0f, 0, 0);
    }

    private static bool IsM3g(string filename)
    {
        string path = Uri.TryCreate(filename, UriKind.Absolute, out Uri? uri)
            ? uri.AbsolutePath
            : filename;
        return path.EndsWith(".m3g", StringComparison.OrdinalIgnoreCase);
    }

    public static DecodedAnimationResult DecodeAnimationSprites(byte[] bytes, string filename)
    {
        var containerType = AnimationContainerDecoder.DetectType(bytes);
        AnimationContainerDecoder.DecodedAnimation anim =
            containerType == AnimationContainerDecoder.ContainerType.WebP
                ? AnimationContainerDecoder.DecodeWebP(bytes)
                : default;

        if (anim.Atlas != null && anim.FrameCount > 0)
        {
            anim.Atlas.name = $"Cache_Animation_{DateTime.Now.Ticks}";
            RuntimeTextureFactory.ApplySampling(
                anim.Atlas,
                FilterMode.Point,
                TextureWrapMode.Clamp);
            Sprite[] sprites = AnimationContainerDecoder.Decode(
                anim.Atlas, anim.Atlas.width, anim.FrameHeight, anim.FrameCount);
            return new DecodedAnimationResult(sprites, anim.Atlas, anim.FPS, anim.FrameHeight, anim.FrameCount);
        }

        throw new InvalidOperationException($"Unknown or empty animation container for '{filename}'.");
    }

    public static Sprite[] SliceAnimationFromTexture(Texture2D texture, int frameHeight, int frameCount)
    {
        int count = frameCount > 0 ? frameCount : Mathf.Max(1, texture.height / frameHeight);
        return AnimationContainerDecoder.Decode(texture, texture.width, frameHeight, count);
    }
}
