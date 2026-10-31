#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

using Kern.AssetPipeline.Cache;

namespace Kern.Tests.AssetPipeline;

[TestFixture]
public sealed class PersistentAssetCacheTests
{
    private string _testRoot = null!;
    private string _cachePath = null!;

    [SetUp]
    public void SetUp()
    {
        _testRoot = Path.Combine(
            Path.GetTempPath(),
            $"kern_asset_cache_format_{Guid.NewGuid():N}");
        _cachePath = Path.Combine(_testRoot, "AssetCache");
        Directory.CreateDirectory(_testRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [Test]
    public void ExistingPayload_IsReadableWithoutLocalIndex()
    {
        Directory.CreateDirectory(_cachePath);
        var cache = new PersistentAssetCache(_cachePath);
        string payloadPath = cache.GetAssetPath("old.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(payloadPath)!);
        File.WriteAllBytes(payloadPath, new byte[] { 1, 2, 3 });

        var restartedCache = new PersistentAssetCache(_cachePath);
        byte[]? payload = restartedCache.GetAsset("old.bin");

        Assert.That(payload, Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(File.Exists(payloadPath), Is.True);
    }

    [Test]
    public void ETag_IsCalculatedFromCurrentPayload()
    {
        var cache = new PersistentAssetCache(_cachePath);
        cache.SaveAsset("Cells/117.png", new byte[] { 1, 2, 3, 4 });
        string payloadPath = cache.GetAssetPath("Cells/117.png");
        File.WriteAllBytes(payloadPath, new byte[] { 4, 3, 2, 1 });

        byte[]? payload = cache.GetAsset("Cells/117.png");

        Assert.That(payload, Is.EqualTo(new byte[] { 4, 3, 2, 1 }));
        Assert.That(cache.GetETag("Cells/117.png"), Is.EqualTo(ETagCalculator.Calculate(payload)));
        Assert.That(File.Exists(payloadPath), Is.True);
    }

    [Test]
    public void Cache_RoundTripsPayloadAndProtocolETag()
    {
        var cache = new PersistentAssetCache(_cachePath);

        byte[] payload = new byte[] { 1, 2, 3, 4 };
        cache.SaveAsset("Cells/117.png", payload);

        Assert.That(cache.GetAsset("Cells/117.png"), Is.EqualTo(payload));
        Assert.That(cache.GetETag("Cells/117.png"), Is.EqualTo(ETagCalculator.Calculate(payload)));
    }

    [Test]
    public void Cache_CalculatesETagForAnyStoredAsset()
    {
        var cache = new PersistentAssetCache(_cachePath);

        byte[] payload = new byte[] { 5, 6, 7 };
        cache.SaveAsset("local.bin", payload);

        Assert.That(cache.GetAsset("local.bin"), Is.EqualTo(payload));
        Assert.That(cache.GetETag("local.bin"), Is.EqualTo(ETagCalculator.Calculate(payload)));
    }

    [Test]
    public void Restart_PreservesCachedPayload()
    {
        var cache = new PersistentAssetCache(_cachePath);
        cache.SaveAsset("orphan.bin", new byte[] { 1, 2, 3 });
        string payloadPath = cache.GetAssetPath("orphan.bin");
        var restartedCache = new PersistentAssetCache(_cachePath);

        Assert.That(restartedCache.GetAsset("orphan.bin"), Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(File.Exists(payloadPath), Is.True);
    }

    [Test]
    public void GetAssetPath_RejectsParentDirectoryTraversal()
    {
        var cache = new PersistentAssetCache(_cachePath);

        Assert.Throws<ArgumentException>(
            () => cache.GetAssetPath("../outside.bin"));
    }

    [Test]
    public async Task ConcurrentReadsAndWritesExposeWholePayloads()
    {
        var cache = new PersistentAssetCache(_cachePath);
        byte[] first = [1, 1, 1, 1];
        byte[] second = [2, 2, 2, 2];
        cache.SaveAsset("shared.bin", first);

        async Task WriteMany()
        {
            for (int index = 0; index < 20; index++)
            {
                bool useFirst = index % 2 == 0;
                await cache.SaveAssetAsync("shared.bin", useFirst ? first : second);
            }
        }

        async Task ReadMany()
        {
            for (int index = 0; index < 40; index++)
            {
                byte[]? payload = await cache.GetAssetAsync("shared.bin");
                Assert.That(payload, Is.EqualTo(first).Or.EqualTo(second));
            }
        }

        await Task.WhenAll(WriteMany(), ReadMany(), ReadMany());

        Assert.That(cache.GetAsset("shared.bin"), Is.EqualTo(second));
        Assert.That(cache.GetETag("shared.bin"), Is.EqualTo(ETagCalculator.Calculate(second)));
    }

    [Test]
    public async Task AsyncReadAndETagMatchProtocolPayload()
    {
        var cache = new PersistentAssetCache(_cachePath);
        byte[] payload = [8, 13, 21];
        cache.SaveAsset("async.bin", payload);

        Assert.That(await cache.GetAssetAsync("async.bin"), Is.EqualTo(payload));
        Assert.That(await cache.GetETagAsync("async.bin"), Is.EqualTo(ETagCalculator.Calculate(payload)));
    }
}
