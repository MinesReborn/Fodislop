using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Fodinae.Scripts.Utils;
using MG.GIF;
using unity.libwebp;
using unity.libwebp.Interop;
using WebP;

namespace Fodinae.Scripts.World
{
    public static class AnimationContainerDecoder
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
            public int FrameHeight;
            public float FPS;
        }

        public static Sprite[] Decode(Texture2D atlas, int width, int height, int frameCount)
        {
            if (atlas == null) return Array.Empty<Sprite>();

            Sprite[] frames = new Sprite[frameCount];
            int framesPerRow = atlas.width / width;

            for (int i = 0; i < frameCount; i++)
            {
                int x = (i % framesPerRow) * width;
                int y = (i / framesPerRow) * height;

                Texture2D frameTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                frameTex.filterMode = FilterMode.Point;

                Color[] colors = atlas.GetPixels(x, y, width, height);
                frameTex.SetPixels(colors);
                frameTex.Apply();

                // Flip Y manually
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

                frames[i] = Sprite.Create(frameTex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), width);
            }

            return frames;
        }

        public static DecodedAnimation DecodeGif(byte[] data)
        {
            try { return new GifInternalDecoder(data).Decode(); }
            catch (Exception e) { Debug.LogError($"[AnimationContainerDecoder] GIF decode failed: {e.Message}\n{e.StackTrace}"); return default; }
        }

        public static DecodedAnimation DecodeWebP(byte[] data)
        {
            try {
                if (data == null || data.Length <= 12) return default;
                int pos = 12;
                var frameTextures = new List<Texture2D>();
                var delays = new List<int>();
                int width = 0, height = 0;

                while (pos <= data.Length - 8) {
                    string chunkId = System.Text.Encoding.ASCII.GetString(data, pos, 4);
                    uint chunkSize = BitConverter.ToUInt32(data, pos + 4);
                    pos += 8;

                    if (chunkId == "VP8X") {
                        width = (data[pos + 4] | (data[pos + 5] << 8) | (data[pos + 6] << 16)) + 1;
                        height = (data[pos + 7] | (data[pos + 8] << 8) | (data[pos + 9] << 16)) + 1;
                    } else if (chunkId == "ANMF") {
                        int duration = data[pos + 12] | (data[pos + 13] << 8) | (data[pos + 14] << 16);
                        int payloadSize = (int)chunkSize - 16;
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

                    pos += (int)((chunkSize + 1) & ~1);
                }

                if (frameTextures.Count == 0)
                {
                    Texture2D tex = Texture2DExt.CreateTexture2DFromWebP(data, lMipmaps: false, lLinear: true, out WebP.Error error);
                    if (error == WebP.Error.Success && tex != null)
                    {
                        return new DecodedAnimation { Atlas = tex, FrameCount = 1, FrameHeight = tex.height, FPS = 0 };
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
                for (int i = 0; i < frameTextures.Count; i++) {
                    Graphics.CopyTexture(frameTextures[i], 0, 0, 0, 0, width, height, atlas, 0, 0, 0, (frameTextures.Count - 1 - i) * height);
                    totalDelay += delays[i];
                    UnityEngine.Object.Destroy(frameTextures[i]);
                }

                float avgDelay = (float)totalDelay / frameTextures.Count;
                return new DecodedAnimation {
                    Atlas = atlas,
                    FrameCount = frameTextures.Count,
                    FrameHeight = height,
                    FPS = avgDelay > 0 ? 1000f / avgDelay : 10f
                };
            } catch (Exception e) {
                Debug.LogError($"[AnimationContainerDecoder] WebP decode failed: {e.Message}");
                return default;
            }
        }

        private class GifInternalDecoder
        {
            private byte[] _data;
            private int _pos, _sw, _sh;
            private Color32[] _gt, _cv, _pv;
            public GifInternalDecoder(byte[] d) { _data = d; }
            public DecodedAnimation Decode() {
                if (_data[0] != 'G' || _data[1] != 'I' || _data[2] != 'F') return default;
                _pos = 6; _sw = _data[_pos++] | (_data[_pos++] << 8); _sh = _data[_pos++] | (_data[_pos++] << 8);
                byte p = _data[_pos++]; _pos += 2;
                if ((p & 0x80) != 0) _gt = ReadCT(1 << ((p & 0x07) + 1));
                _cv = new Color32[_sw * _sh]; _pv = new Color32[_sw * _sh];
                var fts = new List<Texture2D>(); var dls = new List<int>();
                int dl = 10, ti = -1, dm = 0;
                while (_pos < _data.Length) {
                    byte b = _data[_pos++];
                    if (b == 0x21) {
                        byte t = _data[_pos++];
                        if (t == 0xF9) { _pos++; byte g = _data[_pos++]; dm = (g & 0x1C) >> 2; dl = _data[_pos++] | (_data[_pos++] << 8); ti = _data[_pos++]; if ((g & 0x01) == 0) ti = -1; _pos++; }
                        else Skip();
                    } else if (b == 0x2C) {
                        int l = _data[_pos++] | (_data[_pos++] << 8), t = _data[_pos++] | (_data[_pos++] << 8);
                        int w = _data[_pos++] | (_data[_pos++] << 8), h = _data[_pos++] | (_data[_pos++] << 8);
                        byte ip = _data[_pos++]; var ct = (ip & 0x80) != 0 ? ReadCT(1 << ((ip & 0x07) + 1)) : _gt;
                        byte m = _data[_pos++]; var id = Lzw(ReadDB(), m, w * h);
                        if (dm == 3) Array.Copy(_cv, _pv, _cv.Length);
                        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) {
                            byte c = id[y * w + x]; if (c != ti) {
                                int cx = l + x, cy = t + y; if (cx < _sw && cy < _sh) _cv[cy * _sw + cx] = ct[c];
                            }
                        }

                        var tex = new Texture2D(_sw, _sh, TextureFormat.RGBA32, false); var fl = new Color32[_sw * _sh];
                        for (int y = 0; y < _sh; y++) Array.Copy(_cv, y * _sw, fl, (_sh - 1 - y) * _sw, _sw);
                        tex.SetPixels32(fl); tex.Apply(); fts.Add(tex); dls.Add(dl);
                        if (dm == 2) for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) {
                            int cx = l + x, cy = t + y; if (cx < _sw && cy < _sh) _cv[cy * _sw + cx] = new Color32(0, 0, 0, 0);
                        } else if (dm == 3) Array.Copy(_pv, _cv, _cv.Length);
                    } else if (b == 0x3B) break;
                    else break;
                }

                if (fts.Count == 0) return default;
                var atlas = new Texture2D(_sw, _sh * fts.Count, TextureFormat.RGBA32, false); atlas.filterMode = FilterMode.Point; float total = 0;
                for (int i = 0; i < fts.Count; i++) { Graphics.CopyTexture(fts[i], 0, 0, 0, 0, _sw, _sh, atlas, 0, 0, 0, (_sh * (fts.Count - 1 - i))); total += dls[i]; UnityEngine.Object.Destroy(fts[i]); }
                return new DecodedAnimation { Atlas = atlas, FrameCount = fts.Count, FrameHeight = _sh, FPS = total > 0 ? 1000f / (total / fts.Count) : 10f };
            }

            private Color32[] ReadCT(int s) { var t = new Color32[s]; for (int i = 0; i < s; i++) t[i] = new Color32(_data[_pos++], _data[_pos++], _data[_pos++], 255); return t; }
            private void Skip() { int s; while (_pos < _data.Length && (s = _data[_pos++]) > 0) _pos += s; }
            private byte[] ReadDB() { using (var ms = new MemoryStream()) { int s; while (_pos < _data.Length && (s = _data[_pos++]) > 0) { ms.Write(_data, _pos, s); _pos += s; } return ms.ToArray(); } }
            private byte[] Lzw(byte[] d, int m, int pc) {
                int cc = 1 << m, eoi = cc + 1, nc = cc + 2, cs = m + 1, cm = (1 << cs) - 1;
                int[] pref = new int[4096]; byte[] suff = new byte[4096], ps = new byte[4097];
                for (int i = 0; i < cc; i++) suff[i] = (byte)i;
                byte[] o = new byte[pc]; int op = 0, bb = 0, bc = 0, dp = 0, t = 0, oc = -1;
                while (op < pc) {
                    while (bc < cs && dp < d.Length) { bb |= d[dp++] << bc; bc += 8; }
                    if (bc < cs) break;
                    int c = bb & cm; bb >>= cs; bc -= cs;
                    if (c == cc) { cs = m + 1; cm = (1 << cs) - 1; nc = cc + 2; oc = -1; continue; }
                    if (c == eoi) break;
                    if (oc == -1) { o[op++] = suff[c]; oc = c; continue; }
                    int cur = c; if (c >= nc) { ps[t++] = (byte)LzwFirst(oc, cc, pref, suff); cur = oc; }
                    while (cur >= cc) { ps[t++] = suff[cur]; cur = pref[cur]; }
                    ps[t++] = suff[cur]; byte f = ps[t - 1]; while (t > 0) o[op++] = ps[--t];
                    if (nc < 4096) { pref[nc] = oc; suff[nc] = f; nc++; if (nc == (1 << cs) && cs < 12) { cs++; cm = (1 << cs) - 1; } }
                    oc = c;
                }

                return o;
            }

            private int LzwFirst(int c, int cc, int[] pref, byte[] suff) { while (c >= cc) c = pref[c]; return suff[c]; }
        }
    }
}
