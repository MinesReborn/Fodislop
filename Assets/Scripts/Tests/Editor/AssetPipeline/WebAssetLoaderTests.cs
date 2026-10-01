#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.AssetPipeline;

public sealed class WebAssetLoaderTests
{
    private const string Url = "https://example.test/image.png";

    private readonly List<Texture2D> _createdTextures = new();

    [TearDown]
    public void TearDown()
    {
        foreach (Texture2D texture in _createdTextures)
        {
            if (texture != null)
            {
                UnityEngine.Object.DestroyImmediate(texture, true);
            }
        }

        _createdTextures.Clear();
    }

    [Test]
    public async Task ConcurrentRequestsForSameUrl_ShareSingleLoadAndTexture()
    {
        byte[] png = CreatePngBytes();
        int loadCount = 0;
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loader = new WebAssetLoader(
            async (_, _, _) =>
            {
                Interlocked.Increment(ref loadCount);
                await gate.Task.AsUniTask();
                return png;
            },
            () => null);

        UniTask<Texture2D?> first = loader.GetTextureAsync(Url);
        UniTask<Texture2D?> second = loader.GetTextureAsync(Url);
        gate.SetResult(true);

        Texture2D? firstTexture = await first;
        Texture2D? secondTexture = await second;
        _createdTextures.Add(firstTexture!);

        Assert.That(firstTexture, Is.Not.Null);
        Assert.That(secondTexture, Is.SameAs(firstTexture));
        Assert.That(loadCount, Is.EqualTo(1));
    }

    [Test]
    public async Task CompletedUrl_IsReusedWithoutSecondLoad()
    {
        byte[] png = CreatePngBytes();
        int loadCount = 0;
        var loader = new WebAssetLoader(
            (_, _, _) =>
            {
                Interlocked.Increment(ref loadCount);
                return UniTask.FromResult<byte[]?>(png);
            },
            () => null);

        Texture2D? first = await loader.GetTextureAsync(Url);
        Texture2D? second = await loader.GetTextureAsync(Url);
        _createdTextures.Add(first!);

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.SameAs(first));
        Assert.That(loadCount, Is.EqualTo(1));
    }

    [Test]
    public async Task DifferentUrls_LoadSeparately()
    {
        byte[] png = CreatePngBytes();
        int loadCount = 0;
        var loader = new WebAssetLoader(
            (_, _, _) =>
            {
                Interlocked.Increment(ref loadCount);
                return UniTask.FromResult<byte[]?>(png);
            },
            () => null);

        Texture2D? first = await loader.GetTextureAsync("https://example.test/a.png");
        Texture2D? second = await loader.GetTextureAsync("https://example.test/b.png");
        _createdTextures.Add(first!);
        _createdTextures.Add(second!);

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.Not.Null);
        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(loadCount, Is.EqualTo(2));
    }

    private byte[] CreatePngBytes()
    {
        Texture2D source = RuntimeTextureFactory.CreateRGBA32NoMip(
            2,
            2,
            "WebAssetLoaderTestSource",
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp);
        _createdTextures.Add(source);
        source.SetPixels32(
        [
            new Color32(255, 0, 0, 255),
            new Color32(0, 255, 0, 255),
            new Color32(0, 0, 255, 255),
            new Color32(255, 255, 255, 255),
        ]);
        source.Apply();
        return source.EncodeToPNG();
    }
}
