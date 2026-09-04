#nullable enable

using System;
using Fodinae.Core;
using Fodinae.World;
using UnityEngine;

namespace Fodinae;

/// <summary>
/// Decodes raw byte buffers into GPU textures, sprites, and animations for AssetCache.
/// </summary>
internal static class AssetCacheDecoder
{
    public readonly record struct DecodedTextureResult(
        Texture2D? Texture,
        float Fps,
        int FrameHeight,
        int FrameCount);

    public readonly record struct DecodedAnimationResult(
        Sprite[] Sprites,
        Texture2D Atlas,
        float Fps,
        int FrameHeight,
        int FrameCount);

    public static DecodedTextureResult DecodeTexture(byte[] bytes, string filename)
    {
        var containerType = AnimationContainerDecoder.DetectType(bytes);
        if (containerType == AnimationContainerDecoder.ContainerType.GIF)
        {
            var decoded = AnimationContainerDecoder.DecodeGif(bytes);
            if (decoded.Atlas != null)
            {
                decoded.Atlas.name = $"Cache_GIF_{DateTime.Now.Ticks}";
                RuntimeTextureFactory.ApplySampling(
                    decoded.Atlas,
                    FilterMode.Point,
                    TextureWrapMode.Clamp);
            }

            return new DecodedTextureResult(decoded.Atlas, decoded.FPS, decoded.FrameHeight, decoded.FrameCount);
        }

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
        Texture2D? staticTex = RuntimeTextureFactory.DecodeEncodedImageToRgba32NoMip(
            bytes,
            $"Cache_Tex_{DateTime.Now.Ticks}",
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp,
            makeNoLongerReadable: makeNoLongerReadable);

        return new DecodedTextureResult(staticTex, 0f, 0, 0);
    }

    public static DecodedAnimationResult DecodeAnimationSprites(byte[] bytes, string filename)
    {
        var containerType = AnimationContainerDecoder.DetectType(bytes);
        AnimationContainerDecoder.DecodedAnimation anim;

        if (containerType == AnimationContainerDecoder.ContainerType.GIF)
        {
            anim = AnimationContainerDecoder.DecodeGif(bytes);
        }
        else if (containerType == AnimationContainerDecoder.ContainerType.WebP)
        {
            anim = AnimationContainerDecoder.DecodeWebP(bytes);
        }
        else
        {
            anim = default;
        }

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
