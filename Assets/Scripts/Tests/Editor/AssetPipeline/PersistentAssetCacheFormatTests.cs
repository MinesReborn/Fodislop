#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Kern.Tests.AssetPipeline;

[TestFixture]
public sealed class PersistentAssetCacheFormatTests
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
    public void EnsureCurrent_UnmarkedCacheWithPayloads_StampsMarkerAndPreservesPayloads()
    {
        string relativeAsset = Path.Combine("Cells", "117.png");
        string assetPath = Path.Combine(_cachePath, relativeAsset);
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
        File.WriteAllBytes(assetPath, [1, 2, 3, 4]);

        PersistentAssetCacheFormat.EnsureCurrent(_cachePath);

        Assert.That(
            File.ReadAllText(Path.Combine(
                _cachePath,
                PersistentAssetCacheFormat.MarkerFileName)).Trim(),
            Is.EqualTo(PersistentAssetCacheFormat.CurrentSchemaVersion.ToString()));
        Assert.That(File.ReadAllBytes(Path.Combine(_cachePath, relativeAsset)), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
    }

    [Test]
    public void EnsureCurrent_FreshInstall_WritesCurrentMarker()
    {
        PersistentAssetCacheFormat.EnsureCurrent(_cachePath);

        Assert.That(
            File.ReadAllText(Path.Combine(_cachePath, PersistentAssetCacheFormat.MarkerFileName)).Trim(),
            Is.EqualTo(PersistentAssetCacheFormat.CurrentSchemaVersion.ToString()));
    }

    [Test]
    public void EnsureCurrent_UnknownMarkerVersion_RestampsWithoutTouchingPayloads()
    {
        Directory.CreateDirectory(_cachePath);
        string payloadPath = Path.Combine(_cachePath, "old.bin");
        File.WriteAllText(payloadPath, "old");
        File.WriteAllText(
            Path.Combine(_cachePath, PersistentAssetCacheFormat.MarkerFileName),
            "999");

        PersistentAssetCacheFormat.EnsureCurrent(_cachePath);

        Assert.That(File.ReadAllText(payloadPath), Is.EqualTo("old"));
        Assert.That(
            File.ReadAllText(Path.Combine(
                _cachePath,
                PersistentAssetCacheFormat.MarkerFileName)).Trim(),
            Is.EqualTo(PersistentAssetCacheFormat.CurrentSchemaVersion.ToString()));
    }

    [Test]
    public void EnsureCurrent_RejectsCorruptMarker()
    {
        Directory.CreateDirectory(_cachePath);
        File.WriteAllText(
            Path.Combine(_cachePath, PersistentAssetCacheFormat.MarkerFileName),
            "not-a-version");

        Assert.Throws<InvalidDataException>(
            () => PersistentAssetCacheFormat.EnsureCurrent(_cachePath));
    }

    [Test]
    public void ManifestlessPayload_IsInvalidatedOnFirstRead()
    {
        Directory.CreateDirectory(_cachePath);
        File.WriteAllText(
            Path.Combine(_cachePath, PersistentAssetCacheFormat.MarkerFileName),
            "1");
        string payloadPath = Path.Combine(_cachePath, "old.bin");
        File.WriteAllBytes(payloadPath, [1, 2, 3]);
        var cache = new PersistentAssetCache(_cachePath);

        byte[]? payload = cache.GetAsset("old.bin");

        Assert.That(payload, Is.Null);
        Assert.That(File.Exists(payloadPath), Is.False);
        Assert.That(
            File.ReadAllText(Path.Combine(
                _cachePath,
                PersistentAssetCacheFormat.MarkerFileName)).Trim(),
            Is.EqualTo(PersistentAssetCacheFormat.CurrentSchemaVersion.ToString()));
    }

    [Test]
    public void Entry_RejectsTamperedPayload()
    {
        var cache = new PersistentAssetCache(_cachePath);
        cache.SaveAsset("Cells/117.png", [1, 2, 3, 4], "etag-1");
        string payloadPath = cache.GetAssetPath("Cells/117.png");
        File.WriteAllBytes(payloadPath, [4, 3, 2, 1]);

        byte[]? payload = cache.GetAsset("Cells/117.png");

        Assert.That(payload, Is.Null);
        Assert.That(cache.GetETag("Cells/117.png"), Is.Null);
        Assert.That(File.Exists(payloadPath), Is.False);
        Assert.That(File.Exists(payloadPath + ".entry"), Is.False);
    }

    [Test]
    public void Entry_RoundTripsPayloadAndEtag()
    {
        var cache = new PersistentAssetCache(_cachePath);

        cache.SaveAsset("Cells/117.png", [1, 2, 3, 4], "etag-1");

        Assert.That(cache.GetAsset("Cells/117.png"), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        Assert.That(cache.GetETag("Cells/117.png"), Is.EqualTo("etag-1"));
    }

    [Test]
    public void Entry_RoundTripsEmptyEtag()
    {
        var cache = new PersistentAssetCache(_cachePath);

        cache.SaveAsset("local.bin", [5, 6, 7], string.Empty);

        Assert.That(cache.GetAsset("local.bin"), Is.EqualTo(new byte[] { 5, 6, 7 }));
        Assert.That(cache.GetETag("local.bin"), Is.Empty);
    }

    [Test]
    public void Entry_MissingManifestInvalidatesOrphanPayload()
    {
        var cache = new PersistentAssetCache(_cachePath);
        cache.SaveAsset("orphan.bin", [1, 2, 3], "etag");
        string payloadPath = cache.GetAssetPath("orphan.bin");
        File.Delete(payloadPath + ".entry");

        Assert.That(cache.GetAsset("orphan.bin"), Is.Null);
        Assert.That(File.Exists(payloadPath), Is.False);
    }

    [Test]
    public void GetAssetPath_RejectsParentDirectoryTraversal()
    {
        var cache = new PersistentAssetCache(_cachePath);

        Assert.Throws<ArgumentException>(
            () => cache.GetAssetPath("../outside.bin"));
    }

    [Test]
    public async Task ConcurrentReadsAndWritesNeverExposeMixedEntryPairs()
    {
        var cache = new PersistentAssetCache(_cachePath);
        byte[] first = [1, 1, 1, 1];
        byte[] second = [2, 2, 2, 2];
        cache.SaveAsset("shared.bin", first, "first");

        async Task WriteMany()
        {
            for (int index = 0; index < 20; index++)
            {
                bool useFirst = index % 2 == 0;
                await cache.SaveAssetAsync(
                    "shared.bin",
                    useFirst ? first : second,
                    useFirst ? "first" : "second");
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
        Assert.That(cache.GetETag("shared.bin"), Is.EqualTo("second"));
    }
}
