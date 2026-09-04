#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using unity.libwebp;
using unity.libwebp.Interop;
using UnityEngine;

namespace Fodinae.World;

/// <summary>
/// Dedicated decoder for WebP animations using native libwebp.
/// </summary>
public static class WebPAnimationDecoder
{
    public static unsafe AnimationContainerDecoder.DecodedAnimation Decode(byte[] data)
    {
        var frameTextures = new List<Texture2D>();
        Texture2D? atlas = null;
        try
        {
            if (data == null || data.Length < 12 || AnimationContainerDecoder.DetectType(data) != AnimationContainerDecoder.ContainerType.WebP)
            {
                throw new InvalidDataException("WebP data is missing a valid RIFF/WEBP header.");
            }

            long declaredFileSize = BitConverter.ToUInt32(data, 4) + 8L;
            if (declaredFileSize < 12L || declaredFileSize > data.Length)
            {
                throw new InvalidDataException(
                    $"WebP RIFF payload ends at byte {declaredFileSize}, outside the " +
                    $"{data.Length}-byte input.");
            }

            var delays = new List<int>();
            int width;
            int height;
            int expectedFrameCount;
            fixed (byte* dataPointer = data)
            {
                var webpData = new WebPData
                {
                    bytes = dataPointer,
                    size = (UIntPtr)data.Length,
                };
                WebPAnimDecoderOptions options = default;
                if (NativeLibwebpdemux.WebPAnimDecoderOptionsInit(&options) == 0)
                {
                    throw new InvalidDataException(
                        "libwebp could not initialize animation decoder options.");
                }

                options.color_mode = WEBP_CSP_MODE.MODE_RGBA;
                options.use_threads = 1;
                WebPAnimDecoder* decoder =
                    NativeLibwebpdemux.WebPAnimDecoderNew(&webpData, &options);
                if (decoder == null)
                {
                    throw new InvalidDataException(
                        "libwebp could not create an animation decoder.");
                }

                try
                {
                    WebPAnimInfo info = default;
                    if (NativeLibwebpdemux.WebPAnimDecoderGetInfo(
                            decoder,
                            &info) == 0)
                    {
                        throw new InvalidDataException(
                            "libwebp could not read WebP animation metadata.");
                    }

                    width = checked((int)info.canvas_width);
                    height = checked((int)info.canvas_height);
                    expectedFrameCount = checked((int)info.frame_count);
                    if (width <= 0 || height <= 0 || expectedFrameCount <= 0)
                    {
                        throw new InvalidDataException(
                            $"WebP reports invalid canvas/frame metadata: " +
                            $"{width}x{height}, {expectedFrameCount} frame(s).");
                    }

                    if (width > SystemInfo.maxTextureSize ||
                        height > SystemInfo.maxTextureSize)
                    {
                        throw new InvalidDataException(
                            $"WebP canvas {width}x{height} exceeds the GPU " +
                            $"texture limit {SystemInfo.maxTextureSize}.");
                    }

                    int stride = checked(width * 4);
                    int byteCount = checked(stride * height);
                    int previousTimestamp = 0;
                    while (NativeLibwebpdemux.WebPAnimDecoderHasMoreFrames(
                               decoder) != 0)
                    {
                        byte* frameBuffer = null;
                        int timestamp = 0;
                        if (NativeLibwebpdemux.WebPAnimDecoderGetNext(
                                decoder,
                                &frameBuffer,
                                &timestamp) == 0 ||
                            frameBuffer == null)
                        {
                            throw new InvalidDataException(
                                $"libwebp failed while decoding frame " +
                                $"{frameTextures.Count}.");
                        }

                        int duration = timestamp - previousTimestamp;
                        if (expectedFrameCount > 1 && duration <= 0)
                        {
                            throw new InvalidDataException(
                                $"WebP animation frame {frameTextures.Count} " +
                                $"has non-positive duration {duration} ms.");
                        }

                        previousTimestamp = timestamp;
                        byte[] rawPixels = new byte[byteCount];
                        for (int sourceY = 0; sourceY < height; sourceY++)
                        {
                            int destinationY = height - 1 - sourceY;
                            Marshal.Copy(
                                (IntPtr)(frameBuffer + (sourceY * stride)),
                                rawPixels,
                                destinationY * stride,
                                stride);
                        }

                        Texture2D? frameTexture = RuntimeTextureFactory.CreateRgba32NoMip(
                            width,
                            height,
                            $"DecodedWebPFrame_{frameTextures.Count}",
                            RuntimeTextureColorSpace.Srgb,
                            FilterMode.Point,
                            TextureWrapMode.Clamp);
                        try
                        {
                            frameTexture.LoadRawTextureData(rawPixels);
                            bool makeNoLongerReadable =
                                RuntimeTextureFactory.SupportsTexture2DGpuCopy;
                            frameTexture.Apply(
                                updateMipmaps: false,
                                makeNoLongerReadable: makeNoLongerReadable);
                            frameTextures.Add(frameTexture);
                            frameTexture = null;
                        }
                        finally
                        {
                            if (frameTexture != null)
                            {
                                UnityEngine.Object.Destroy(frameTexture);
                            }
                        }

                        delays.Add(duration);
                    }

                    if (frameTextures.Count != expectedFrameCount)
                    {
                        throw new InvalidDataException(
                            $"libwebp decoded {frameTextures.Count} frame(s), but " +
                            $"the container declares {expectedFrameCount}.");
                    }
                }
                finally
                {
                    NativeLibwebpdemux.WebPAnimDecoderDelete(decoder);
                }
            }

            int frameCount = frameTextures.Count;
            if (frameCount == 1)
            {
                Texture2D texture = frameTextures[0];
                frameTextures.Clear();
                return new AnimationContainerDecoder.DecodedAnimation
                {
                    Atlas = texture,
                    FrameCount = 1,
                    FrameHeight = height,
                    FPS = 0f,
                };
            }

            int atlasHeight = checked(height * frameCount);
            if (atlasHeight > SystemInfo.maxTextureSize)
            {
                throw new InvalidDataException(
                    $"WebP animation atlas {width}x{atlasHeight} exceeds the GPU " +
                    $"texture limit {SystemInfo.maxTextureSize}.");
            }

            atlas = RuntimeTextureFactory.CreateRgba32NoMip(
                width,
                atlasHeight,
                "DecodedWebPAtlas",
                RuntimeTextureColorSpace.Srgb,
                FilterMode.Point,
                TextureWrapMode.Clamp);

            AnimationContainerDecoder.CopyFramesToAtlas(frameTextures, atlas, width, height);

            float totalDelay = 0f;
            for (int i = 0; i < delays.Count; i++)
            {
                totalDelay += delays[i];
            }

            float averageDelay = totalDelay / delays.Count;
            float fps = AnimationContainerDecoder.GetAnimationFps(averageDelay, frameCount, "WebP");

            return new AnimationContainerDecoder.DecodedAnimation
            {
                Atlas = atlas,
                FrameCount = frameCount,
                FrameHeight = height,
                FPS = fps,
            };
        }
        catch (Exception e)
        {
            AnimationContainerDecoder.DestroyTextures(frameTextures);
            if (atlas != null)
            {
                UnityEngine.Object.Destroy(atlas);
            }

            Debug.LogWarning($"[AnimationContainerDecoder] WebP decode failed; asset will be skipped: {e.Message}");
            throw new InvalidOperationException($"WebP decode failed: {e.Message}", e);
        }
    }
}
