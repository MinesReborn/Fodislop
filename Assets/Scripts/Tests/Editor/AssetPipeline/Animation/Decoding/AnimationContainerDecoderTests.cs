#nullable enable

using System;
using System.Linq;
using System.IO;
using Kern.World;
using NUnit.Framework;
using UnityEngine;

using Kern.AssetPipeline.Animation;
using Kern.AssetPipeline.Cache;

namespace Kern.Tests.AssetPipeline;

[TestFixture]
public sealed class AnimationContainerDecoderTests
{
    [Test]
    public void DetectType_PngData_ReturnsPNG()
    {
        byte[] pngHeader = new byte[] {
0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
 };
        Assert.That(AnimationContainerDecoder.DetectType(pngHeader), Is.EqualTo(AnimationContainerDecoder.ContainerType.PNG));
    }

    [Test]
    public void DetectType_NullOrShort_ReturnsNone()
    {
        Assert.That(AnimationContainerDecoder.DetectType(null!), Is.EqualTo(AnimationContainerDecoder.ContainerType.None));
        Assert.That(AnimationContainerDecoder.DetectType(Array.Empty<byte>()), Is.EqualTo(AnimationContainerDecoder.ContainerType.None));
        Assert.That(AnimationContainerDecoder.DetectType(new byte[] { 0x89, 0x50 }), Is.EqualTo(AnimationContainerDecoder.ContainerType.None));
    }

    [TestCase("vfx/bz", 16, 480, 16, 32, 15, 30f)]
    [TestCase("VFX/death.png", 64, 2496, 64, 64, 39, 40f)]
    [TestCase("cells/GrayAcid.png", 32, 160, 32, 32, 5, 10f)]
    [TestCase("cells/PurpleAcid", 32, 160, 32, 32, 5, 10f)]
    [TestCase("cells/PassiveAcid", 32, 160, 32, 32, 5, 10f)]
    [TestCase("cells/DeepLazuriteSand.png", 32, 160, 32, 32, 5, 2f)]
    [TestCase("cells/Box.png", 32, 128, 32, 32, 4, 4f)]
    public void TryGetAnimationConfig_KnownAnimations_ReturnsExpectedValues(
        string filename,
        int width,
        int height,
        int expectedFrameWidth,
        int expectedFrameHeight,
        int expectedFrameCount,
        float expectedFPS)
    {
        bool success = AnimationContainerDecoder.TryGetAnimationConfig(
            filename,
            width,
            height,
            out int frameWidth,
            out int frameHeight,
            out int frameCount,
            out float fps);

        Assert.That(success, Is.True);
        Assert.That(frameWidth, Is.EqualTo(expectedFrameWidth));
        Assert.That(frameHeight, Is.EqualTo(expectedFrameHeight));
        Assert.That(frameCount, Is.EqualTo(expectedFrameCount));
        Assert.That(fps, Is.EqualTo(expectedFPS));
    }

    [Test]
    public void TryGetAnimationConfig_ArbitraryVerticalStrip_CalculatesSquareFrames()
    {
        bool success = AnimationContainerDecoder.TryGetAnimationConfig(
            "custom/effect",
            32,
            160,
            out int frameWidth,
            out int frameHeight,
            out int frameCount,
            out float fps);

        Assert.That(success, Is.True);
        Assert.That(frameWidth, Is.EqualTo(32));
        Assert.That(frameHeight, Is.EqualTo(32));
        Assert.That(frameCount, Is.EqualTo(5));
        Assert.That(fps, Is.EqualTo(5f));
    }

    [Test]
    public void TryGetAnimationConfig_SingleSquareFrame_ReturnsSingleFrame()
    {
        bool success = AnimationContainerDecoder.TryGetAnimationConfig(
            "custom/static",
            32,
            32,
            out int frameWidth,
            out int frameHeight,
            out int frameCount,
            out float fps);

        Assert.That(success, Is.True);
        Assert.That(frameWidth, Is.EqualTo(32));
        Assert.That(frameHeight, Is.EqualTo(32));
        Assert.That(frameCount, Is.EqualTo(1));
        Assert.That(fps, Is.EqualTo(0f));
    }

    [TestCase(0, 32)]
    [TestCase(32, 0)]
    [TestCase(-1, 32)]
    public void TryGetAnimationConfig_InvalidDimensions_ReturnsFalse(int width, int height)
    {
        bool success = AnimationContainerDecoder.TryGetAnimationConfig(
            "test",
            width,
            height,
            out _,
            out _,
            out _,
            out _);

        Assert.That(success, Is.False);
    }

    [Test]
    public void Decode_NullOrInvalidAtlas_ThrowsExpectedException()
    {
        Assert.Throws<ArgumentNullException>(() => AnimationContainerDecoder.Decode(null!, 32, 32, 1));

        Texture2D smallTexture = new(16, 16);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => AnimationContainerDecoder.Decode(smallTexture, 0, 32, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => AnimationContainerDecoder.Decode(smallTexture, 32, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => AnimationContainerDecoder.Decode(smallTexture, 32, 32, 0));
            Assert.Throws<InvalidDataException>(() => AnimationContainerDecoder.Decode(smallTexture, 32, 32, 1));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(smallTexture);
        }
    }

    [Test]
    public void Decode_ValidAtlas_ProducesCorrectSprites()
    {
        Texture2D texture = new(32, 96);
        try
        {
            Sprite[] sprites = AnimationContainerDecoder.Decode(texture, 32, 32, 3);
            Assert.That(sprites, Is.Not.Null);
            Assert.That(sprites.Length, Is.EqualTo(3));

            Assert.That(sprites[0].rect, Is.EqualTo(new Rect(0, 0, 32, 32)));
            Assert.That(sprites[1].rect, Is.EqualTo(new Rect(0, 32, 32, 32)));
            Assert.That(sprites[2].rect, Is.EqualTo(new Rect(0, 64, 32, 32)));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void DecodeAnimationSprites_ActualPngCell_SuccessfullyDecodes()
    {
        string cellPath = Path.Combine(Application.dataPath, "Textures/Cells/Structures/Storage/Box.png");
        if (!File.Exists(cellPath))
        {
            Assert.Ignore($"Asset not found at {cellPath}");
        }

        byte[] bytes = File.ReadAllBytes(cellPath);
        var result = AssetCacheDecoder.DecodeAnimationSprites(bytes, "Cells/Box");

        Assert.That(result.Sprites, Is.Not.Null);
        Assert.That(result.Sprites.Length, Is.EqualTo(4));
        Assert.That(result.FrameHeight, Is.EqualTo(32));
        Assert.That(result.FrameCount, Is.EqualTo(4));
        Assert.That(result.FPS, Is.EqualTo(4f));
        Assert.That(result.Atlas, Is.Not.Null);

        UnityEngine.Object.DestroyImmediate(result.Atlas);
    }

    [Test]
    public void DecodeAnimationSprites_ActualPngVFX_SuccessfullyDecodes()
    {
        string vfxPath = Path.Combine(Application.dataPath, "Textures/VFX/bz/bz.png");
        if (!File.Exists(vfxPath))
        {
            Assert.Ignore($"Asset not found at {vfxPath}");
        }

        byte[] bytes = File.ReadAllBytes(vfxPath);
        var result = AssetCacheDecoder.DecodeAnimationSprites(bytes, "VFX/bz");

        Assert.That(result.Sprites, Is.Not.Null);
        Assert.That(result.Sprites.Length, Is.EqualTo(15));
        Assert.That(result.FrameEmission.Length, Is.EqualTo(15));
        Assert.That(result.FrameEmission.Any(frame => frame.Radiance.maxColorComponent > 0f), Is.True);
        Assert.That(result.FrameEmission.All(frame => float.IsFinite(frame.Centroid.x) && float.IsFinite(frame.Centroid.y)), Is.True);
        Assert.That(result.FrameHeight, Is.EqualTo(64));
        Assert.That(result.FrameCount, Is.EqualTo(15));
        Assert.That(result.FPS, Is.EqualTo(30f));
        Assert.That(result.Atlas, Is.Not.Null);

        UnityEngine.Object.DestroyImmediate(result.Atlas);
    }
    [Test]
    public void TextureFirstMiningDecode_PreservesEmissionMetadataBeforeDroppingCpuPixels()
    {
        string path = Path.Combine(Application.dataPath, "Textures/VFX/bz/bz.png");
        Assert.That(File.Exists(path), Is.True);
        var result = AssetCacheDecoder.DecodeTexture(File.ReadAllBytes(path), "VFX/bz");
        try
        {
            Assert.That(result.FrameEmission.Length, Is.EqualTo(15));
            Assert.That(result.FrameEmission.Any(frame => frame.Radiance.maxColorComponent > 0f), Is.True);
            Assert.That(result.FrameEmission.All(frame => Mathf.Abs(frame.Centroid.x) <= 0.5f &&
                Mathf.Abs(frame.Centroid.y) <= 1f), Is.True);
            if (Kern.AssetPipeline.Loading.RuntimeTextureFactory.SupportsTexture2DGPUCopy)
            {
                Assert.That(result.Texture!.isReadable, Is.False);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(result.Texture);
        }
    }

}
