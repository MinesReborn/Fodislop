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
    public void ExternallyModifiedPayload_IsRejectedAndDropped()
    {
        var cache = new PersistentAssetCache(_cachePath);
        cache.SaveAsset("Cells/117.png", new byte[] { 1, 2, 3, 4 });
        string payloadPath = cache.GetAssetPath("Cells/117.png");
        File.WriteAllBytes(payloadPath, new byte[] { 4, 3, 2, 1 });

        byte[]? payload = cache.GetAsset("Cells/117.png");

        Assert.That(payload, Is.Null);
        Assert.That(File.Exists(payloadPath), Is.False);
        Assert.That(cache.GetETag("Cells/117.png"), Is.Null);
        Assert.That(cache.GetRecordedETag("Cells/117.png"), Is.Null);
    }

    [Test]
    public void AdoptedPayload_IsVerifiedOnLaterReads()
    {
        var cache = new PersistentAssetCache(_cachePath);
        string payloadPath = cache.GetAssetPath("legacy.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(payloadPath)!);
        File.WriteAllBytes(payloadPath, new byte[] { 1, 2, 3 });
        Assert.That(cache.GetAsset("legacy.bin"), Is.EqualTo(new byte[] { 1, 2, 3 }));

        File.WriteAllBytes(payloadPath, new byte[] { 9, 9, 9 });

        Assert.That(cache.GetAsset("legacy.bin"), Is.Null);
    }

    [Test]
    public void Manifest_SurvivesRestartAndStillDetectsCorruption()
    {
        var cache = new PersistentAssetCache(_cachePath);
        byte[] payload = new byte[] { 1, 2, 3, 4 };
        cache.SaveAsset("Cells/5.png", payload);
        string payloadPath = cache.GetAssetPath("Cells/5.png");

        var restartedCache = new PersistentAssetCache(_cachePath);
        Assert.That(restartedCache.GetRecordedETag("Cells/5.png"), Is.EqualTo(ETagCalculator.Calculate(payload)));
        File.WriteAllBytes(payloadPath, new byte[] { 1, 2, 3, 5 });

        Assert.That(restartedCache.GetAsset("Cells/5.png"), Is.Null);
    }

    [Test]
    public void SystemFiles_SurviveRetainOnlyAndBrokenManifestCleanup()
    {
        var cache = new PersistentAssetCache(_cachePath);
        cache.SaveAsset("Visual/a.png", new byte[] { 1 });
        string rootJunk = Path.Combine(_cachePath, ".DS_Store");
        string folderJunk = Path.Combine(Path.GetDirectoryName(cache.GetAssetPath("Visual/a.png"))!, ".DS_Store");
        File.WriteAllBytes(rootJunk, new byte[] { 9 });
        File.WriteAllBytes(folderJunk, new byte[] { 9 });

        cache.RetainOnly(new string[0]);

        Assert.That(cache.HasAsset("Visual/a.png"), Is.False);
        Assert.That(File.Exists(rootJunk), Is.True);
        Assert.That(File.Exists(folderJunk), Is.True);

        File.WriteAllText(Path.Combine(_cachePath, "manifest.json"), "{ not json");
        _ = new PersistentAssetCache(_cachePath);

        Assert.That(File.Exists(rootJunk), Is.True);
        Assert.That(File.Exists(folderJunk), Is.True);
    }

    [Test]
    public void BrokenManifest_ClearsCacheInsteadOfAdoptingUnverifiableFiles()
    {
        var cache = new PersistentAssetCache(_cachePath);
        cache.SaveAsset("Cells/5.png", new byte[] { 1, 2, 3, 4 });
        string payloadPath = cache.GetAssetPath("Cells/5.png");
        File.WriteAllText(Path.Combine(_cachePath, "manifest.json"), "{ not json");

        var restartedCache = new PersistentAssetCache(_cachePath);

        Assert.That(File.Exists(payloadPath), Is.False);
        Assert.That(restartedCache.GetAsset("Cells/5.png"), Is.Null);
        Assert.That(restartedCache.HasAsset("Cells/5.png"), Is.False);
    }

    [Test]
    public void RetainOnly_KeepsManifestFilesAndDeletesTheRest()
    {
        var cache = new PersistentAssetCache(_cachePath);
        cache.SaveAsset("a.png", new byte[] { 1 });
        cache.SaveAsset("b.png", new byte[] { 2 });
        string unknownPath = cache.GetAssetPath("unknown.png");
        File.WriteAllBytes(unknownPath, new byte[] { 4 });

        int removed = cache.RetainOnly(new[] { "a.png" });

        Assert.That(removed, Is.EqualTo(2));
        Assert.That(cache.HasAsset("a.png"), Is.True);
        Assert.That(cache.HasAsset("b.png"), Is.False);
        Assert.That(File.Exists(unknownPath), Is.False);
        Assert.That(cache.GetRecordedETag("b.png"), Is.Null);
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
