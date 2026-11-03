#nullable enable

using System;
using System.IO;
using Kern.Core;
using Kern.World;
using UnityEngine;

using Kern.AssetPipeline.Animation;
using Kern.AssetPipeline.Loading;

namespace Kern.AssetPipeline.Cache;

internal static class AssetCacheDecoder
{
    public readonly record struct DecodedTextureResult(
        Texture2D? Texture,
        float FPS,
        int FrameHeight,
        int FrameCount,
        SpriteEmissionFrame[] FrameEmission);

    public readonly record struct DecodedAnimationResult(
        Sprite[] Sprites,
        Texture2D Atlas,
        float FPS,
        int FrameHeight,
        int FrameCount,
        SpriteEmissionFrame[] FrameEmission);

    public static DecodedTextureResult DecodeTexture(byte[] bytes, string filename)
    {
        // M3G has no magic bytes, so it can only be recognized by the
        // extension (URL query strings included, e.g. image.m3g?v=2).
        if (IsM3G(filename))
        {
            Texture2D m3g = M3GImageDecoder.Decode(bytes);
            m3g.name = $"Cache_M3G_{DateTime.Now.Ticks}";
            return new DecodedTextureResult(m3g, 0f, 0, 0, Array.Empty<SpriteEmissionFrame>());
        }

        bool makeNoLongerReadable = RuntimeTextureFactory.SupportsTexture2DGPUCopy;
        bool emissionNeedsPixels = AnimationContainerDecoder.HasFrameEmission(filename) &&
            EncodedImageHeader.TryReadSize(bytes, out int headerWidth, out int headerHeight) &&
            AnimationContainerDecoder.TryGetAnimationConfig(filename, headerWidth, headerHeight,
                out _, out _, out int headerFrames, out _) && headerFrames > 1;
        Texture2D? staticTex = RuntimeTextureFactory.DecodeEncodedImageToRGBA32NoMip(
            bytes,
            $"Cache_Tex_{DateTime.Now.Ticks}",
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp,
            makeNoLongerReadable: makeNoLongerReadable && !emissionNeedsPixels);

        int frameHeight = 0;
        int frameCount = 0;
        float fps = 0f;
        SpriteEmissionFrame[] emission = Array.Empty<SpriteEmissionFrame>();
        if (staticTex != null &&
            AnimationContainerDecoder.TryGetAnimationConfig(
                filename,
                staticTex.width,
                staticTex.height,
                out int fw,
                out int fh,
                out int fc,
                out float fFPS) &&
            fc > 1)
        {
            frameHeight = fh;
            frameCount = fc;
            fps = fFPS;
            if (emissionNeedsPixels)
            {
                emission = SpriteEmissionIntegrator.Frames(staticTex, fw, fh, fc,
                    AnimationContainerDecoder.ResolvePixelsPerUnit(filename, fw));
            }
        }

        if (makeNoLongerReadable && emissionNeedsPixels && staticTex != null)
        {
            staticTex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        }
        return new DecodedTextureResult(staticTex, fps, frameHeight, frameCount, emission);
    }

    private static bool IsM3G(string filename)
    {
        string path = Uri.TryCreate(filename, UriKind.Absolute, out Uri? uri)
            ? uri.AbsolutePath
            : filename;
        return path.EndsWith(".m3g", StringComparison.Ordinal);
    }

    public static DecodedAnimationResult DecodeAnimationSprites(byte[] bytes, string filename)
    {
        Texture2D? atlas = RuntimeTextureFactory.DecodeEncodedImageToRGBA32NoMip(
            bytes,
            $"Cache_Animation_{DateTime.Now.Ticks}",
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp,
            makeNoLongerReadable: false);

        if (atlas == null)
        {
            throw new InvalidDataException($"Failed to decode image data for animation '{filename}'.");
        }

        if (!AnimationContainerDecoder.TryGetAnimationConfig(
                filename,
                atlas.width,
                atlas.height,
                out int frameWidth,
                out int frameHeight,
                out int frameCount,
                out float fps) ||
            frameCount <= 0)
        {
            throw new InvalidOperationException($"Could not determine animation frame layout for '{filename}'.");
        }

        Sprite[] sprites = AnimationContainerDecoder.Decode(
            atlas, frameWidth, frameHeight, frameCount,
            AnimationContainerDecoder.ResolvePixelsPerUnit(filename, frameWidth));
        SpriteEmissionFrame[] emission = AnimationContainerDecoder.HasFrameEmission(filename)
            ? SpriteEmissionIntegrator.Frames(atlas, frameWidth, frameHeight, frameCount,
                AnimationContainerDecoder.ResolvePixelsPerUnit(filename, frameWidth))
            : Array.Empty<SpriteEmissionFrame>();
        return new DecodedAnimationResult(sprites, atlas, fps, frameHeight, frameCount, emission);
    }

    public static Sprite[] SliceAnimationFromTexture(Texture2D texture, int frameHeight, int frameCount,
        string filename = "")
    {
        int count = frameCount > 0 ? frameCount : Mathf.Max(1, texture.height / frameHeight);
        return AnimationContainerDecoder.Decode(texture, texture.width, frameHeight, count,
            AnimationContainerDecoder.ResolvePixelsPerUnit(filename, texture.width));
    }
}
