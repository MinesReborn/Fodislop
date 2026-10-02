#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Kern;
using UnityEngine;

namespace Kern.World;
public static class AnimationContainerDecoder
{
    public enum ContainerType
    {
        None,
        PNG,
    }

    private static readonly Dictionary<string, (int frameWidth, int frameHeight, int frameCount, float fps)> KnownAnimations =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "vfx/bz", (16, 32, 15, 15f) },
            { "vfx/death", (64, 64, 39, 40f) },
            { "vfx/destroy", (1, 1, 1, 0f) },
            { "cells/66", (32, 32, 6, 5f) },
            { "cells/67", (32, 32, 6, 5f) },
            { "cells/90", (32, 32, 4, 4f) },
        };

    public static ContainerType DetectType(byte[] data)
    {
        if (data == null || data.Length < 8)
        {
            return ContainerType.None;
        }

        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
        {
            return ContainerType.PNG;
        }

        return ContainerType.None;
    }

    public static bool TryGetAnimationConfig(
        string filename,
        int width,
        int height,
        out int frameWidth,
        out int frameHeight,
        out int frameCount,
        out float fps)
    {
        if (width <= 0 || height <= 0)
        {
            frameWidth = 0;
            frameHeight = 0;
            frameCount = 0;
            fps = 0f;
            return false;
        }

        string normalized = NormalizeAnimationName(filename);
        if (KnownAnimations.TryGetValue(normalized, out var config))
        {
            frameWidth = config.frameWidth;
            frameHeight = config.frameHeight;
            frameCount = config.frameCount;
            fps = config.fps;
            return true;
        }

        if (height > width && height % width == 0)
        {
            frameWidth = width;
            frameHeight = width;
            frameCount = height / width;
            fps = 5f;
            return true;
        }

        frameWidth = width;
        frameHeight = height;
        frameCount = 1;
        fps = 0f;
        return true;
    }

    private static string NormalizeAnimationName(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        string normalized = path.Replace('\\', '/').TrimStart('/');
        int dot = normalized.LastIndexOf('.');
        if (dot > 0 && dot > normalized.LastIndexOf('/'))
        {
            normalized = normalized.Substring(0, dot);
        }

        return normalized;
    }

    public static Sprite[] Decode(Texture2D atlas, int width, int height, int frameCount)
    {
        if (atlas == null)
        {
            throw new ArgumentNullException(nameof(atlas));
        }

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Sprite frame dimensions must be positive.");
        }

        if (frameCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameCount),
                "Sprite frame count must be positive.");
        }

        if (atlas.width < width || atlas.height < height)
        {
            throw new InvalidDataException(
                $"Sprite atlas {atlas.width}x{atlas.height} is smaller than frame {width}x{height}.");
        }

        Sprite[] frames = new Sprite[frameCount];
        int framesPerRow = atlas.width / width;
        if (framesPerRow <= 0 ||
            (int)Math.Ceiling(frameCount / (double)framesPerRow) * height > atlas.height)
        {
            throw new InvalidDataException(
                $"Sprite atlas {atlas.width}x{atlas.height} cannot contain " +
                $"{frameCount} frames of {width}x{height}.");
        }

        for (int i = 0; i < frameCount; i++)
        {
            int x = (i % framesPerRow) * width;
            int y = (i / framesPerRow) * height;

            frames[i] = Sprite.Create(
                atlas,
                new Rect(x, y, width, height),
                new Vector2(0.5f, 0.5f),
                RenderingConstants.PIXELS_PER_UNIT);
        }

        return frames;
    }

    public static void CopyFramesToAtlas(
        List<Texture2D> frameTextures,
        Texture2D atlas,
        int width,
        int height)
    {
        bool useGpuCopy = RuntimeTextureFactory.SupportsTexture2DGpuCopy;
        for (int i = 0; i < frameTextures.Count; i++)
        {
            Texture2D frame = frameTextures[i];
            if (frame.width != width || frame.height != height)
            {
                throw new InvalidDataException(
                    $"Animation frame {i} is {frame.width}x{frame.height}; " +
                    $"expected {width}x{height}.");
            }

            if (useGpuCopy)
            {
                if (frame.graphicsFormat != atlas.graphicsFormat)
                {
                    throw new InvalidDataException(
                        $"Animation frame {i} uses GPU format " +
                        $"{frame.graphicsFormat}, but atlas uses " +
                        $"{atlas.graphicsFormat}.");
                }

                Graphics.CopyTexture(
                    frame,
                    0,
                    0,
                    0,
                    0,
                    width,
                    height,
                    atlas,
                    0,
                    0,
                    0,
                    i * height);
            }
            else
            {
                atlas.SetPixels32(
                    x: 0,
                    y: i * height,
                    blockWidth: width,
                    blockHeight: height,
                    colors: frame.GetPixels32());
            }
        }

        if (!useGpuCopy)
        {
            atlas.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }

        DestroyTextures(frameTextures);
    }

    public static void DestroyTextures(List<Texture2D> textures)
    {
        for (int i = 0; i < textures.Count; i++)
        {
            if (textures[i] != null)
            {
                UnityEngine.Object.Destroy(textures[i]);
            }
        }

        textures.Clear();
    }

    public static float GetAnimationFps(
        float averageDelay,
        int frameCount,
        string containerName)
    {
        if (frameCount <= 1)
        {
            return 0f;
        }

        if (averageDelay <= 0f || float.IsNaN(averageDelay) || float.IsInfinity(averageDelay))
        {
            throw new InvalidDataException(
                $"{containerName} animation has {frameCount} frames but no positive frame delay.");
        }

        return containerName == "GIF"
            ? 100f / averageDelay
            : 1000f / averageDelay;
    }

    public struct DecodedAnimation
    {
        public Texture2D Atlas { get; set; }

        public int FrameCount { get; set; }

        public int FrameHeight { get; set; }

        public float FPS { get; set; }
    }
}
