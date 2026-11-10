#nullable enable

using System;
using System.Buffers.Binary;
using NUnit.Framework;
using UnityEngine;

using Kern.AssetPipeline.Loading;

namespace Kern.Tests.AssetPipeline;

// Оракул — кодировщики Unity: картинка известного размера кодируется ими,
// а не собирается руками по тому же разбору, что проверяется.
public sealed class EncodedImageHeaderTests
{
    [Test]
    public void ReadsSizeOfUnityEncodedPng()
    {
        AssertEncodedSize(texture => texture.EncodeToPNG(), 37, 19);
    }

    [Test]
    public void RejectsUnknownFormat()
    {
        byte[] data = { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09 };

        Assert.That(EncodedImageHeader.TryReadSize(data, out _, out _), Is.False);
    }

    [Test]
    public void FactoryRejectsPngDeclaringOversizedDimensionsBeforeDecoding()
    {
        byte[] png = Encode(texture => texture.EncodeToPNG(), 4, 4, TextureFormat.RGBA32);
        // IHDR: ширина и высота — big-endian по смещениям 16 и 20.
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), 16384);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), 16384);

        Assert.Throws<InvalidOperationException>(() =>
            RuntimeTextureFactory.DecodeEncodedImageToRGBA32NoMip(
                png,
                "oversized",
                RuntimeTextureColorSpace.Srgb,
                FilterMode.Point,
                TextureWrapMode.Clamp,
                makeNoLongerReadable: false));
    }

    [Test]
    public void FactoryStillDecodesOrdinaryPng()
    {
        byte[] png = Encode(texture => texture.EncodeToPNG(), 64, 48, TextureFormat.RGBA32);

        Texture2D decoded = RuntimeTextureFactory.DecodeEncodedImageToRGBA32NoMip(
            png,
            "ordinary",
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp,
            makeNoLongerReadable: false);
        try
        {
            Assert.That(decoded.width, Is.EqualTo(64));
            Assert.That(decoded.height, Is.EqualTo(48));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(decoded);
        }
    }

    private static void AssertEncodedSize(
        Func<Texture2D, byte[]> encode,
        int width,
        int height)
    {
        byte[] data = Encode(encode, width, height, TextureFormat.RGBA32);

        Assert.That(EncodedImageHeader.TryReadSize(data, out int readWidth, out int readHeight), Is.True);
        Assert.That(readWidth, Is.EqualTo(width));
        Assert.That(readHeight, Is.EqualTo(height));
    }

    private static byte[] Encode(Func<Texture2D, byte[]> encode, int width, int height, TextureFormat format)
    {
        var texture = new Texture2D(width, height, format, mipChain: false);
        try
        {
            return encode(texture);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
