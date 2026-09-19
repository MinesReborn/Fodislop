#nullable enable

using System.Collections.Generic;
using MinesServer.Data;
using Kern.World;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

public sealed class CellTextureCacheTests
{
    private static Texture2D MakeTexture(int w, int h)
    {
        var tex = Kern.RuntimeTextureFactory.CreateRGBA32NoMip(
            w,
            h,
            "TestCellTexture",
            Kern.RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp);
        CreatedTextures.Add(tex);
        return tex;
    }

    private static List<Texture2D> CreatedTextures { get; } = new();

    [TearDown]
    public void TearDown()
    {
        foreach (Texture2D tex in CreatedTextures)
        {
            if (tex != null)
            {
                Object.DestroyImmediate(tex, true);
            }
        }

        CreatedTextures.Clear();
    }

    [Test]
    public void AddTexture_ThenTryGetTexture_ReturnsInfo()
    {
        var cache = new CellTextureCache();
        var texture = MakeTexture(2, 2);
        var info = new CellTextureInfo
        {
            CellType = CellType.Rock,
            BaseTexture = texture,
            OwnsBaseTexture = true,
        };

        cache.AddTexture(CellType.Rock, info);

        Assert.That(cache.TryGetTexture(CellType.Rock, out CellTextureInfo result), Is.True);
        Assert.That(result.BaseTexture, Is.SameAs(texture));
        Assert.That(cache.GetCachedTexture(CellType.Rock), Is.SameAs(texture));
    }

    [Test]
    public void AddTexture_ReplaceExistingTexture_DestroyPreviousOwned()
    {
        var cache = new CellTextureCache();
        var first = MakeTexture(2, 2);
        var second = MakeTexture(2, 2);

        cache.AddTexture(CellType.Rock, new CellTextureInfo
        {
            CellType = CellType.Rock,
            BaseTexture = first,
            OwnsBaseTexture = true,
        });

        cache.AddTexture(CellType.Rock, new CellTextureInfo
        {
            CellType = CellType.Rock,
            BaseTexture = second,
            OwnsBaseTexture = true,
        });

        Assert.That(cache.GetCachedTexture(CellType.Rock), Is.SameAs(second));
    }

    [Test]
    public void Clear_RemovesAllEntries()
    {
        var cache = new CellTextureCache();
        var texture = MakeTexture(2, 2);
        cache.AddTexture(CellType.Rock, new CellTextureInfo
        {
            CellType = CellType.Rock,
            BaseTexture = texture,
            OwnsBaseTexture = true,
        });

        cache.Clear();

        Assert.That(cache.TryGetTexture(CellType.Rock, out _), Is.False);
        Assert.That(cache.GetCachedTexture(CellType.Rock), Is.Null);
    }

    [Test]
    public void GetCacheStats_ReturnsTextureCount()
    {
        var cache = new CellTextureCache();
        var texture = MakeTexture(2, 2);
        cache.AddTexture(CellType.Rock, new CellTextureInfo
        {
            CellType = CellType.Rock,
            BaseTexture = texture,
            OwnsBaseTexture = true,
        });

        string stats = cache.GetCacheStats();
        Assert.That(stats, Does.Contain("1 textures"));
    }

    [Test]
    public void GetMemoryUsage_ReturnsApproximateBytes()
    {
        var cache = new CellTextureCache();
        var texture = MakeTexture(4, 4);
        cache.AddTexture(CellType.Rock, new CellTextureInfo
        {
            CellType = CellType.Rock,
            BaseTexture = texture,
            OwnsBaseTexture = true,
        });

        // 4x4 RGBA32 = 64 bytes
        Assert.That(cache.GetMemoryUsage(), Is.EqualTo(64L));
    }
}
