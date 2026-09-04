#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fodinae.World;

/// <summary>
/// Dedicated decoder for GIF animations with custom LZW decompression.
/// </summary>
public static class GifAnimationDecoder
{
    public static AnimationContainerDecoder.DecodedAnimation Decode(byte[] data)
    {
        try
        {
            return new GifInternalDecoder(data).Decode();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[AnimationContainerDecoder] GIF decode failed; asset will be skipped: {e.Message}");
            throw new InvalidOperationException($"GIF decode failed: {e.Message}", e);
        }
    }

    private class GifInternalDecoder
    {
        private readonly byte[] _data;
        private readonly GifStreamReader _reader;
        private int _sw;
        private int _sh;
        private Color32[] _gt = Array.Empty<Color32>();
        private Color32[] _cv = Array.Empty<Color32>();
        private Color32[] _pv = Array.Empty<Color32>();

        public GifInternalDecoder(byte[] d)
        {
            _data = d;
            _reader = new GifStreamReader(d);
        }

        public AnimationContainerDecoder.DecodedAnimation Decode()
        {
            if (_data.Length < 13 ||
                _data[0] != 'G' ||
                _data[1] != 'I' ||
                _data[2] != 'F')
            {
                throw new InvalidDataException(
                    "GIF data is missing a complete header and logical screen descriptor.");
            }

            _reader.Position = 6;
            _sw = _reader.ReadUInt16();
            _sh = _reader.ReadUInt16();
            if (_sw <= 0 || _sh <= 0)
            {
                throw new InvalidDataException(
                    $"GIF logical screen has invalid dimensions {_sw}x{_sh}.");
            }

            int pixelCount = checked(_sw * _sh);
            if (_sw > SystemInfo.maxTextureSize ||
                _sh > SystemInfo.maxTextureSize)
            {
                throw new InvalidDataException(
                    $"GIF logical screen {_sw}x{_sh} exceeds the GPU " +
                    $"texture limit {SystemInfo.maxTextureSize}.");
            }

            byte packedFields = _reader.ReadByte();
            int backgroundColorIndex = _reader.ReadByte();
            _reader.ReadByte(); // Pixel aspect ratio.

            if ((packedFields & 0x80) != 0)
            {
                _gt = _reader.ReadColorTable(1 << ((packedFields & 0x07) + 1));
            }

            Color32 backgroundColor =
                backgroundColorIndex >= 0 && backgroundColorIndex < _gt.Length
                    ? _gt[backgroundColorIndex]
                    : new Color32(0, 0, 0, 0);
            _cv = new Color32[pixelCount];
            _pv = new Color32[pixelCount];
            var frameTextures = new List<Texture2D>();
            var frameDelays = new List<int>();
            Texture2D? atlas = null;
            bool foundTrailer = false;
            int delay = 0;
            int transparentIndex = -1;
            int disposalMethod = 0;

            try
            {
                while (_reader.Position < _reader.Length)
                {
                    byte blockType = _reader.ReadByte();
                    if (blockType == 0x21)
                    {
                        byte extensionType = _reader.ReadByte();
                        if (extensionType == 0xF9)
                        {
                            int blockSize = _reader.ReadByte();
                            if (blockSize != 4)
                            {
                                throw new InvalidDataException(
                                    $"GIF graphic control extension has size {blockSize}; expected 4.");
                            }

                            byte graphicControl = _reader.ReadByte();
                            disposalMethod = (graphicControl & 0x1C) >> 2;
                            delay = _reader.ReadUInt16();
                            transparentIndex = _reader.ReadByte();
                            if ((graphicControl & 0x01) == 0)
                            {
                                transparentIndex = -1;
                            }

                            if (_reader.ReadByte() != 0)
                            {
                                throw new InvalidDataException(
                                    "GIF graphic control extension has no zero terminator.");
                            }
                        }
                        else
                        {
                            _reader.SkipDataSubBlocks();
                        }
                    }
                    else if (blockType == 0x2C)
                    {
                        int left = _reader.ReadUInt16();
                        int top = _reader.ReadUInt16();
                        int width = _reader.ReadUInt16();
                        int height = _reader.ReadUInt16();
                        if (width <= 0 || height <= 0 ||
                            left > _sw - width || top > _sh - height)
                        {
                            throw new InvalidDataException(
                                $"GIF frame rectangle {width}x{height} at {left},{top} " +
                                $"does not fit the {_sw}x{_sh} canvas.");
                        }

                        byte imageFields = _reader.ReadByte();
                        Color32[] colorTable = (imageFields & 0x80) != 0
                            ? _reader.ReadColorTable(1 << ((imageFields & 0x07) + 1))
                            : _gt;
                        if (colorTable.Length == 0)
                        {
                            throw new InvalidDataException(
                                $"GIF frame {frameTextures.Count} has no color table.");
                        }

                        int minimumCodeSize = _reader.ReadByte();
                        byte[] colorIndices = GifLzwDecoder.Decompress(
                            _reader.ReadDataSubBlocks(),
                            minimumCodeSize,
                            checked(width * height));

                        if (disposalMethod == 3)
                        {
                            Array.Copy(_cv, _pv, _cv.Length);
                        }

                        bool interlaced = (imageFields & 0x40) != 0;
                        GifFrameCompositor.CompositeFrame(
                            _cv,
                            colorIndices,
                            colorTable,
                            left,
                            top,
                            width,
                            height,
                            _sw,
                            transparentIndex,
                            interlaced);

                        Texture2D frameTexture = RuntimeTextureFactory.CreateRgba32NoMip(
                            _sw,
                            _sh,
                            "DecodedGifFrame",
                            RuntimeTextureColorSpace.Srgb,
                            FilterMode.Point,
                            TextureWrapMode.Clamp);
                        var flippedPixels = new Color32[pixelCount];
                        for (int y = 0; y < _sh; y++)
                        {
                            Array.Copy(
                                _cv,
                                y * _sw,
                                flippedPixels,
                                (_sh - 1 - y) * _sw,
                                _sw);
                        }

                        frameTexture.SetPixels32(flippedPixels);
                        bool makeNoLongerReadable =
                            RuntimeTextureFactory.SupportsTexture2DGpuCopy;
                        frameTexture.Apply(
                            updateMipmaps: false,
                            makeNoLongerReadable: makeNoLongerReadable);
                        frameTextures.Add(frameTexture);
                        frameDelays.Add(delay);

                        if (disposalMethod == 2)
                        {
                            Color32 restoreColor = transparentIndex >= 0
                                ? new Color32(0, 0, 0, 0)
                                : backgroundColor;
                            GifFrameCompositor.ClearFrameRectangle(
                                _cv,
                                left,
                                top,
                                width,
                                height,
                                _sw,
                                restoreColor);
                        }
                        else if (disposalMethod == 3)
                        {
                            Array.Copy(_pv, _cv, _cv.Length);
                        }

                        delay = 0;
                        transparentIndex = -1;
                        disposalMethod = 0;
                    }
                    else if (blockType == 0x3B)
                    {
                        foundTrailer = true;
                        break;
                    }
                    else
                    {
                        throw new InvalidDataException(
                            $"GIF contains unknown block type 0x{blockType:X2} " +
                            $"at byte {_reader.Position - 1}.");
                    }
                }

                if (!foundTrailer)
                {
                    throw new InvalidDataException(
                        "GIF stream ended before its trailer byte.");
                }

                if (frameTextures.Count == 0)
                {
                    throw new InvalidDataException(
                        "GIF container was valid but contained no usable image frames.");
                }

                int frameCount = frameTextures.Count;
                if (frameCount > 1)
                {
                    for (int i = 0; i < frameDelays.Count; i++)
                    {
                        if (frameDelays[i] <= 0)
                        {
                            throw new InvalidDataException(
                                $"GIF animation frame {i} has no positive delay.");
                        }
                    }
                }

                int atlasHeight = checked(_sh * frameCount);
                if (atlasHeight > SystemInfo.maxTextureSize)
                {
                    throw new InvalidDataException(
                        $"GIF animation atlas {_sw}x{atlasHeight} exceeds the GPU " +
                        $"texture limit {SystemInfo.maxTextureSize}.");
                }

                atlas = RuntimeTextureFactory.CreateRgba32NoMip(
                    _sw,
                    atlasHeight,
                    "DecodedGifAtlas",
                    RuntimeTextureColorSpace.Srgb,
                    FilterMode.Point,
                    TextureWrapMode.Clamp);
                float totalDelay = 0;
                for (int i = 0; i < frameCount; i++)
                {
                    totalDelay += frameDelays[i];
                }

                AnimationContainerDecoder.CopyFramesToAtlas(
                    frameTextures,
                    atlas,
                    _sw,
                    _sh);
                float fps = AnimationContainerDecoder.GetAnimationFps(
                    totalDelay / frameCount,
                    frameCount,
                    "GIF");

                var result = new AnimationContainerDecoder.DecodedAnimation
                {
                    Atlas = atlas,
                    FrameCount = frameCount,
                    FrameHeight = _sh,
                    FPS = fps,
                };
                atlas = null;
                return result;
            }
            catch
            {
                AnimationContainerDecoder.DestroyTextures(frameTextures);
                if (atlas != null)
                {
                    UnityEngine.Object.Destroy(atlas);
                }

                throw;
            }
        }
    }
}
