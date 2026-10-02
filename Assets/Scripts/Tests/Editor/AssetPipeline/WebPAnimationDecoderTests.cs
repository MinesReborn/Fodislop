#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using Kern.World;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.AssetPipeline;

[TestFixture]
public sealed class WebPAnimationDecoderTests
{
    private Texture2D? _createdAtlas;

    [TearDown]
    public void TearDown()
    {
        if (_createdAtlas != null)
        {
            UnityEngine.Object.DestroyImmediate(_createdAtlas, true);
            _createdAtlas = null;
        }
    }

    [Test]
    public void Decode_InvalidHeader_ThrowsInvalidDataException()
    {
        byte[] invalidData = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13];
        Assert.Throws<InvalidDataException>(() => WebPAnimationDecoder.Decode(invalidData));
    }

    [Test]
    public void Decode_ActualCellWebP_SuccessfullyDecodesFramesAndAtlas()
    {
        string webpPath = Path.Combine(Application.dataPath, "Textures", "Cells", "90.webp");
        if (!File.Exists(webpPath))
        {
            Assert.Ignore($"WebP asset not found at {webpPath}");
            return;
        }

        byte[] bytes = File.ReadAllBytes(webpPath);
        AnimationContainerDecoder.DecodedAnimation decoded = WebPAnimationDecoder.Decode(bytes);

        Assert.That(decoded.Atlas, Is.Not.Null);
        _createdAtlas = decoded.Atlas;

        Assert.That(decoded.FrameCount, Is.GreaterThan(0));
        Assert.That(decoded.Atlas.width, Is.GreaterThan(0));
        Assert.That(decoded.Atlas.height, Is.GreaterThan(0));
        Assert.That(decoded.FrameHeight, Is.GreaterThan(0));
        Assert.That(decoded.FPS, Is.GreaterThan(0f));
    }

    [Test]
    public void Decode_ActualVfxWebP_SuccessfullyDecodesAndMeasuresSpeed()
    {
        string vfxPath = Path.Combine(Application.dataPath, "Textures", "VFX", "destroy.webp");
        if (!File.Exists(vfxPath))
        {
            Assert.Ignore($"VFX WebP asset not found at {vfxPath}");
            return;
        }

        byte[] bytes = File.ReadAllBytes(vfxPath);

        // Warm up decoder
        AnimationContainerDecoder.DecodedAnimation initial = WebPAnimationDecoder.Decode(bytes);
        if (initial.Atlas != null)
        {
            UnityEngine.Object.DestroyImmediate(initial.Atlas, true);
        }

        // Benchmark decode speed over iterations
        const int iterations = 10;
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            AnimationContainerDecoder.DecodedAnimation result = WebPAnimationDecoder.Decode(bytes);
            if (result.Atlas != null)
            {
                UnityEngine.Object.DestroyImmediate(result.Atlas, true);
            }
        }

        stopwatch.Stop();
        double msPerDecode = stopwatch.Elapsed.TotalMilliseconds / iterations;
        UnityEngine.Debug.Log($"[WebP Performance] Average decode time for {Path.GetFileName(vfxPath)}: {msPerDecode:F2} ms");

        // Native libwebp decoding of small sprite sheets should comfortably complete in under 50ms per animation
        Assert.That(msPerDecode, Is.LessThan(50.0), $"WebP decode took too long: {msPerDecode:F2} ms");
    }
}
