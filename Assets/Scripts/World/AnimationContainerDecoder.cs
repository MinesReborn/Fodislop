using MG.GIF; // Requires mgGIF script
using System;
using System.Collections.Generic;
using unity.libwebp;
using unity.libwebp.Interop;
using UnityEngine;
using WebP;   // Requires the netpyoung/unity.webp package

namespace Fodinae.Assets.Scripts.World
{
    public class AnimationContainerDecoder
    {
        public enum ContainerType { None, PNG, GIF, WebP }

        public static ContainerType DetectType(byte[] data)
        {
            if (data == null || data.Length < 12) return ContainerType.None;
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return ContainerType.PNG;
            if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38) return ContainerType.GIF;
            if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 &&
                data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50) return ContainerType.WebP;
            return ContainerType.None;
        }

        public struct DecodedAnimation
        {
            public Texture2D Atlas;
            public int FrameCount;
            public float FPS;
        }

        public static DecodedAnimation DecodeGif(byte[] data)
        {
            if (data == null || data.Length == 0) return default;

            try
            {
                using (var decoder = new Decoder(data))
                {
                    List<Texture2D> frameTextures = new List<Texture2D>();
                    List<int> frameDelays = new List<int>();

                    MG.GIF.Image img;

                    // The decoder modifies and returns the SAME Image instance on each loop.
                    // We must immediately extract the Texture2D from it before reading the next.
                    while ((img = decoder.NextImage()) != null)
                    {
                        frameTextures.Add(img.CreateTexture());
                        frameDelays.Add(img.Delay);
                    }

                    int frameCount = frameTextures.Count;
                    if (frameCount == 0) return default;

                    // Grab dimensions from the decoder header
                    int width = decoder.Width;
                    int height = decoder.Height;

                    // IMPORTANT: We use ARGB32 because the mgGif script creates ARGB32 textures.
                    // Graphics.CopyTexture requires exact format matches.
                    Texture2D atlas = new Texture2D(width, height * frameCount, TextureFormat.ARGB32, false);
                    atlas.filterMode = FilterMode.Point;

                    int totalDelay = 0;

                    for (int i = 0; i < frameCount; i++)
                    {
                        Texture2D frameTex = frameTextures[i];
                        totalDelay += frameDelays[i];

                        // Write to the output Atlas vertically (Unity Y = 0 corresponds to Frame 0)
                        Graphics.CopyTexture(frameTex, 0, 0, 0, 0, width, height, atlas, 0, 0, 0, i * height);

                        // Clean up the temporary frame immediately
                        UnityEngine.Object.Destroy(frameTex);
                    }

                    float avgDelay = frameCount > 0 ? (float)totalDelay / frameCount : 0;
                    float fps = avgDelay > 0 ? 1000f / avgDelay : 10f;
                    return new DecodedAnimation
                    {
                        Atlas = atlas,
                        FrameCount = frameCount,
                        FPS = Mathf.Clamp(fps, 0.1f, 60f)
                    };
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimationContainerDecoder] GIF decode failed: {e.Message}\n{e.StackTrace}");
                return default;
            }
        }

        public static unsafe DecodedAnimation DecodeWebP(byte[] data)
        {
            if (data == null || data.Length == 0) return default;

            try
            {
                WebPAnimDecoderOptions option = new WebPAnimDecoderOptions
                {
                    use_threads = 1,
                    color_mode = WEBP_CSP_MODE.MODE_RGBA
                };
                NativeLibwebpdemux.WebPAnimDecoderOptionsInit(&option);

                fixed (byte* p = data)
                {
                    WebPData webpdata = new WebPData
                    {
                        bytes = p,
                        size = new UIntPtr((uint)data.Length)
                    };

                    WebPAnimDecoder* dec = NativeLibwebpdemux.WebPAnimDecoderNew(&webpdata, &option);

                    if (dec == null)
                    {
                        Error err;
                        Texture2D tex = Texture2DExt.CreateTexture2DFromWebP(data, false, false, out err);
                        if (err == Error.Success && tex != null)
                        {
                            return new DecodedAnimation { Atlas = tex, FrameCount = 1, FPS = 0 };
                        }
                        return default;
                    }

                    WebPAnimInfo anim_info = new WebPAnimInfo();
                    NativeLibwebpdemux.WebPAnimDecoderGetInfo(dec, &anim_info);

                    int width = (int)anim_info.canvas_width;
                    int height = (int)anim_info.canvas_height;
                    int frameCount = (int)anim_info.frame_count;

                    if (frameCount <= 0)
                    {
                        NativeLibwebpdemux.WebPAnimDecoderDelete(dec);
                        return default;
                    }

                    Texture2D atlas = new Texture2D(width, height * frameCount, TextureFormat.RGBA32, false);
                    atlas.filterMode = FilterMode.Point;

                    int frameSize = width * height * 4;
                    int totalDelay = 0;
                    int prevTimestamp = 0;
                    int frameIndex = 0;

                    while (NativeLibwebpdemux.WebPAnimDecoderHasMoreFrames(dec) != 0 && frameIndex < frameCount)
                    {
                        byte* buf;
                        int timestamp = 0;

                        int result = NativeLibwebpdemux.WebPAnimDecoderGetNext(dec, &buf, &timestamp);
                        if (result != 1) break;

                        int delay = timestamp - prevTimestamp;
                        totalDelay += delay;
                        prevTimestamp = timestamp;

                        Texture2D frameTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                        frameTex.LoadRawTextureData((IntPtr)buf, frameSize);
                        frameTex.Apply();

                        Color32[] pixels = frameTex.GetPixels32();
                        Color32[] rowBuffer = new Color32[width];
                        for (int y = 0; y < height / 2; y++)
                        {
                            int topIndex = y * width;
                            int bottomIndex = (height - 1 - y) * width;

                            Array.Copy(pixels, topIndex, rowBuffer, 0, width);
                            Array.Copy(pixels, bottomIndex, pixels, topIndex, width);
                            Array.Copy(rowBuffer, 0, pixels, bottomIndex, width);
                        }
                        frameTex.SetPixels32(pixels);
                        frameTex.Apply();

                        Graphics.CopyTexture(frameTex, 0, 0, 0, 0, width, height, atlas, 0, 0, 0, frameIndex * height);
                        UnityEngine.Object.Destroy(frameTex);

                        frameIndex++;
                    }

                    NativeLibwebpdemux.WebPAnimDecoderDelete(dec);

                    float avgDelay = frameIndex > 0 ? (float)totalDelay / frameIndex : 0;
                    float fps = avgDelay > 0 ? 1000f / avgDelay : 10f;
                    return new DecodedAnimation
                    {
                        Atlas = atlas,
                        FrameCount = frameIndex,
                        FPS = Mathf.Clamp(fps, 0.1f, 60f)
                    };
                }
            }
            catch (DllNotFoundException)
            {
                Debug.LogError("[AnimationContainerDecoder] WebP native library not found. Please ensure the netpyoung/unity.webp package is correctly installed.");
                return default;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimationContainerDecoder] WebP decode failed: {e.Message}");
                return default;
            }
        }
    }
}