using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.World;
using MG.GIF;
using unity.libwebp;
using unity.libwebp.Interop;
using UnityEngine;
using WebP;

namespace Fodinae.Scripts.World
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
                return Array.Empty<Sprite>();
            }

            Sprite[] frames = new Sprite[frameCount];
            int framesPerRow = atlas.width / width;
            int atlasHeight = atlas.height;

            for (int i = 0; i < frameCount; i++)
            {
                int x = (i % framesPerRow) * width;
                int y = (i / framesPerRow) * height;

                frames[i] = Sprite.Create(atlas, new Rect(x, atlasHeight - y - height, width, height), new Vector2(0.5f, 0.5f), RenderingConstants.PIXELS_PER_UNIT);
            }

            return frames;
        }

        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Decoding external data can throw various exceptions; we want to fail gracefully.")]
        public static DecodedAnimation DecodeGif(byte[] data)
        {
            try
            {
                return new GifInternalDecoder(data).Decode();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimationContainerDecoder] GIF decode failed: {e.Message}\n{e.StackTrace}");
                return default;
            }
        }

        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Decoding external data can throw various exceptions; we want to fail gracefully.")]
        public static DecodedAnimation DecodeWebP(byte[] data)
        {
            try
            {
                if (data == null || data.Length <= 12)
                {
                    return default;
                }

                int pos = 12;
                var frameTextures = new List<Texture2D>();
                var delays = new List<int>();
                int width = 0, height = 0;

                while (pos <= data.Length - 8)
                {
                    string chunkId = System.Text.Encoding.ASCII.GetString(data, pos, 4);
                    uint CHUNK_SIZE = BitConverter.ToUInt32(data, pos + 4);
                    pos += 8;

                    if (chunkId == "VP8X")
                    {
                        width = (data[pos + 4] | (data[pos + 5] << 8) | (data[pos + 6] << 16)) + 1;
                        height = (data[pos + 7] | (data[pos + 8] << 8) | (data[pos + 9] << 16)) + 1;
                    }
                    else if (chunkId == "ANMF")
                    {
                        int duration = data[pos + 12] | (data[pos + 13] << 8) | (data[pos + 14] << 16);
                        int payloadSize = (int)CHUNK_SIZE - 16;
                        int payloadPos = pos + 16;

                        byte[] frameFile = new byte[payloadSize + 12];
                        Buffer.BlockCopy(data, 0, frameFile, 0, 4); // RIFF
                        byte[] newSize = BitConverter.GetBytes((uint)payloadSize + 4);
                        Buffer.BlockCopy(newSize, 0, frameFile, 4, 4);
                        Buffer.BlockCopy(data, 8, frameFile, 8, 4); // WEBP
                        Buffer.BlockCopy(data, payloadPos, frameFile, 12, payloadSize);

                        Texture2D tex = Texture2DExt.CreateTexture2DFromWebP(frameFile, lMipmaps: false, lLinear: true, out WebP.Error error);
                        if (error == WebP.Error.Success && tex != null)
                        {
                            frameTextures.Add(tex);
                            delays.Add(duration);
                        }
                        else if (tex != null)
                        {
                            UnityEngine.Object.Destroy(tex);
                        }
                    }

                    pos += (int)((CHUNK_SIZE + 1) & ~1);
                }

                if (frameTextures.Count == 0)
                {
                    Texture2D tex = Texture2DExt.CreateTexture2DFromWebP(data, lMipmaps: false, lLinear: true, out WebP.Error error);
                    if (error == WebP.Error.Success && tex != null)
                    {
                        if (tex.isReadable)
                        {
                            return new DecodedAnimation
                            {
                                Atlas = tex,
                                FrameCount = 1,
                                FrameHeight = tex.height,
                                FPS = 0,
                            };
                        }

                        var readable = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                        readable.filterMode = FilterMode.Point;
                        Graphics.CopyTexture(tex, readable);
                        readable.Apply();
                        UnityEngine.Object.Destroy(tex);
                        return new DecodedAnimation
                        {
                            Atlas = readable,
                            FrameCount = 1,
                            FrameHeight = readable.height,
                            FPS = 0,
                        };
                    }

                    if (tex != null)
                    {
                        UnityEngine.Object.Destroy(tex);
                    }

                    return default;
                }

                width = frameTextures[0].width;
                height = frameTextures[0].height;
                var atlas = new Texture2D(width, height * frameTextures.Count, TextureFormat.RGBA32, false);
                atlas.filterMode = FilterMode.Point;
                float totalDelay = 0;
                for (int i = 0; i < frameTextures.Count; i++)
                {
                    Graphics.CopyTexture(frameTextures[i], 0, 0, 0, 0, width, height, atlas, 0, 0, 0, i * height);
                    totalDelay += delays[i];
                    UnityEngine.Object.Destroy(frameTextures[i]);
                }

                float avgDelay = totalDelay / frameTextures.Count;
                return new DecodedAnimation
                {
                    Atlas = atlas,
                    FrameCount = frameTextures.Count,
                    FrameHeight = height,
                    FPS = avgDelay > 0 ? 1000f / avgDelay : 10f,
                };
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimationContainerDecoder] WebP decode failed: {e.Message}");
                return default;
            }
        }

        public struct DecodedAnimation
        {
            public Texture2D Atlas { get; set; }

            public int FrameCount { get; set; }

            public int FrameHeight { get; set; }

            public float FPS { get; set; }
        }

        private class GifInternalDecoder
        {
            private byte[] _data;
            private int _pos;
            private int _sw;
            private int _sh;
            private Color32[] _gt;
            private Color32[] _cv;
            private Color32[] _pv;

            public GifInternalDecoder(byte[] d)
            {
                this._data = d;
            }

            public DecodedAnimation Decode()
            {
                if (this._data[0] != 'G' || this._data[1] != 'I' || this._data[2] != 'F')
                {
                    return default;
                }

                this._pos = 6;
                this._sw = this._data[this._pos++] | (this._data[this._pos++] << 8);
                this._sh = this._data[this._pos++] | (this._data[this._pos++] << 8);
                byte p = this._data[this._pos++];
                this._pos += 2;

                if ((p & 0x80) != 0)
                {
                    this._gt = this.ReadCT(1 << ((p & 0x07) + 1));
                }

                this._cv = new Color32[this._sw * this._sh];
                this._pv = new Color32[this._sw * this._sh];
                var fts = new List<Texture2D>();
                var dls = new List<int>();
                int dl = 10;
                int ti = -1;
                int dm = 0;

                while (this._pos < this._data.Length)
                {
                    byte b = this._data[this._pos++];
                    if (b == 0x21)
                    {
                        byte t = this._data[this._pos++];
                        if (t == 0xF9)
                        {
                            this._pos++;
                            byte g = this._data[this._pos++];
                            dm = (g & 0x1C) >> 2;
                            dl = this._data[this._pos++] | (this._data[this._pos++] << 8);
                            ti = this._data[this._pos++];
                            if ((g & 0x01) == 0)
                            {
                                ti = -1;
                            }

                            this._pos++;
                        }
                        else
                        {
                            this.Skip();
                        }
                    }
                    else if (b == 0x2C)
                    {
                        int l = this._data[this._pos++] | (this._data[this._pos++] << 8);
                        int t = this._data[this._pos++] | (this._data[this._pos++] << 8);
                        int w = this._data[this._pos++] | (this._data[this._pos++] << 8);
                        int h = this._data[this._pos++] | (this._data[this._pos++] << 8);
                        byte ip = this._data[this._pos++];
                        var ct = (ip & 0x80) != 0 ? this.ReadCT(1 << ((ip & 0x07) + 1)) : this._gt;
                        byte m = this._data[this._pos++];
                        var id = Lzw(this.ReadDB(), m, w * h);

                        if (dm == 3)
                        {
                            Array.Copy(this._cv, this._pv, this._cv.Length);
                        }

                        for (int y = 0; y < h; y++)
                        {
                            for (int x = 0; x < w; x++)
                            {
                                byte c = id[(y * w) + x];
                                if (c != ti)
                                {
                                    int cx = l + x;
                                    int cy = t + y;
                                    if (cx < this._sw && cy < this._sh)
                                    {
                                        this._cv[(cy * this._sw) + cx] = ct[c];
                                    }
                                }
                            }
                        }

                        var tex = new Texture2D(this._sw, this._sh, TextureFormat.RGBA32, false);
                        var fl = new Color32[this._sw * this._sh];
                        for (int y = 0; y < this._sh; y++)
                        {
                            Array.Copy(this._cv, y * this._sw, fl, (this._sh - 1 - y) * this._sw, this._sw);
                        }

                        tex.SetPixels32(fl);
                        tex.Apply();
                        fts.Add(tex);
                        dls.Add(dl);

                        if (dm == 2)
                        {
                            for (int y = 0; y < h; y++)
                            {
                                for (int x = 0; x < w; x++)
                                {
                                    int cx = l + x;
                                    int cy = t + y;
                                    if (cx < this._sw && cy < this._sh)
                                    {
                                        this._cv[(cy * this._sw) + cx] = new Color32(0, 0, 0, 0);
                                    }
                                }
                            }
                        }
                        else if (dm == 3)
                        {
                            Array.Copy(this._pv, this._cv, this._cv.Length);
                        }
                    }
                    else if (b == 0x3B)
                    {
                        break;
                    }
                    else
                    {
                        break;
                    }
                }

                if (fts.Count == 0)
                {
                    return default;
                }

                var atlas = new Texture2D(this._sw, this._sh * fts.Count, TextureFormat.RGBA32, false);
                atlas.filterMode = FilterMode.Point;
                float total = 0;
                for (int i = 0; i < fts.Count; i++)
                {
                    Graphics.CopyTexture(fts[i], 0, 0, 0, 0, this._sw, this._sh, atlas, 0, 0, 0, this._sh * i);
                    total += dls[i];
                    UnityEngine.Object.Destroy(fts[i]);
                }

                return new DecodedAnimation
                {
                    Atlas = atlas,
                    FrameCount = fts.Count,
                    FrameHeight = this._sh,
                    FPS = total > 0 ? 100f / (total / fts.Count) : 10f,
                };
            }

            private static byte[] Lzw(byte[] d, int m, int pc)
            {
                int cc = 1 << m;
                int eoi = cc + 1;
                int nc = cc + 2;
                int cs = m + 1;
                int cm = (1 << cs) - 1;
                int[] pref = new int[4096];
                byte[] suff = new byte[4096];
                byte[] ps = new byte[4097];

                for (int i = 0; i < cc; i++)
                {
                    suff[i] = (byte)i;
                }

                byte[] o = new byte[pc];
                int op = 0;
                int bb = 0;
                int bc = 0;
                int dp = 0;
                int t = 0;
                int oc = -1;

                while (op < pc)
                {
                    while (bc < cs && dp < d.Length)
                    {
                        bb |= d[dp++] << bc;
                        bc += 8;
                    }

                    if (bc < cs)
                    {
                        break;
                    }

                    int c = bb & cm;
                    bb >>= cs;
                    bc -= cs;

                    if (c == cc)
                    {
                        cs = m + 1;
                        cm = (1 << cs) - 1;
                        nc = cc + 2;
                        oc = -1;
                        continue;
                    }

                    if (c == eoi)
                    {
                        break;
                    }

                    if (oc == -1)
                    {
                        o[op++] = suff[c];
                        oc = c;
                        continue;
                    }

                    int cur = c;
                    if (c >= nc)
                    {
                        ps[t++] = (byte)LzwFirst(oc, cc, pref, suff);
                        cur = oc;
                    }

                    while (cur >= cc)
                    {
                        ps[t++] = suff[cur];
                        cur = pref[cur];
                    }

                    ps[t++] = suff[cur];
                    byte f = ps[t - 1];
                    while (t > 0)
                    {
                        o[op++] = ps[--t];
                    }

                    if (nc < 4096)
                    {
                        pref[nc] = oc;
                        suff[nc] = f;
                        nc++;
                        if (nc == (1 << cs) && cs < 12)
                        {
                            cs++;
                            cm = (1 << cs) - 1;
                        }
                    }

                    oc = c;
                }

                return o;
            }

            private static int LzwFirst(int c, int cc, int[] pref, byte[] suff)
            {
                while (c >= cc)
                {
                    c = pref[c];
                }

                return suff[c];
            }

            private Color32[] ReadCT(int s)
            {
                var t = new Color32[s];
                for (int i = 0; i < s; i++)
                {
                    t[i] = new Color32(this._data[this._pos++], this._data[this._pos++], this._data[this._pos++], 255);
                }

                return t;
            }

            private void Skip()
            {
                int s;
                while (this._pos < this._data.Length && (s = this._data[this._pos++]) > 0)
                {
                    this._pos += s;
                }
            }

            private byte[] ReadDB()
            {
                using (var ms = new MemoryStream())
                {
                    int s;
                    while (this._pos < this._data.Length && (s = this._data[this._pos++]) > 0)
                    {
                        ms.Write(this._data, this._pos, s);
                        this._pos += s;
                    }

                    return ms.ToArray();
                }
            }
        }
    }
}
