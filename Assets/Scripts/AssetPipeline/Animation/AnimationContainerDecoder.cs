#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Fodinae;
using UnityEngine;

namespace Fodinae.World
{
    public static class AnimationContainerDecoder
    {
        public enum ContainerType
        {
            None,
            PNG,
            GIF,
            WebP,
        }

        public static ContainerType DetectType(byte[] data)
        {
            if (data == null || data.Length < 12)
            {
                return ContainerType.None;
            }

            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            {
                return ContainerType.PNG;
            }

            if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38)
            {
                return ContainerType.GIF;
            }

            if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 &&
                data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)
            {
                return ContainerType.WebP;
            }

            return ContainerType.None;
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

                // DecodeGif/DecodeWebP place frame zero at the bottom of the
                // Unity texture and append later frames upwards. Re-inverting Y
                // here returned the animation in reverse order.
                frames[i] = Sprite.Create(
                    atlas,
                    new Rect(x, y, width, height),
                    new Vector2(0.5f, 0.5f),
                    RenderingConstants.PIXELS_PER_UNIT);
            }

            return frames;
        }

        public static DecodedAnimation DecodeGif(byte[] data)
        {
            return GifAnimationDecoder.Decode(data);
        }

        public static DecodedAnimation DecodeWebP(byte[] data)
        {
            return WebPAnimationDecoder.Decode(data);
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
}
