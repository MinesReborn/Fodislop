using System;
using UnityEngine;
using Fodinae.Scripts.Utils;

namespace Fodinae.Scripts.World
{
    public static class AnimationContainerDecoder
    {
        public static Sprite[] Decode(Texture2D atlas, int width, int height, int frameCount)
        {
            if (atlas == null) return Array.Empty<Sprite>();

            Sprite[] frames = new Sprite[frameCount];
            int framesPerRow = atlas.width / width;

            for (int i = 0; i < frameCount; i++)
            {
                int x = (i % framesPerRow) * width;
                int y = (i / framesPerRow) * height;

                // Create a temporary texture to flip the frame (Unity UVs are bottom-up, server frames are top-down)
                Texture2D frameTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                frameTex.filterMode = FilterMode.Point;
                
                Color[] colors = atlas.GetPixels(x, y, width, height);
                frameTex.SetPixels(colors);
                frameTex.Apply();

                // Flip Y manually if needed or just handle in UVs
                // Most simple way to flip texture pixels:
                if (true) 
                {
                    Color32[] pixels = frameTex.GetPixels32();
                    Color32[] rowBuffer = new Color32[width];
                    for (int yy = 0; yy < height / 2; yy++)
                    {
                        int topIndex = yy * width;
                        int bottomIndex = (height - 1 - yy) * width;

                        Array.Copy(pixels, topIndex, rowBuffer, 0, width);
                        Array.Copy(pixels, bottomIndex, pixels, topIndex, width);
                        Array.Copy(rowBuffer, 0, pixels, bottomIndex, width);
                    }
                    frameTex.SetPixels32(pixels);
                    frameTex.Apply();
                }

                frames[i] = Sprite.Create(frameTex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), width);
            }

            return frames;
        }
    }
}
