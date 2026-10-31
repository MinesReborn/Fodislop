#nullable enable

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Kern.Core;
using Kern.Persistence;
using NUnit.Framework;

using Kern.Bootstrap.Diagnostics;
using Kern.Networking.Offline;

namespace Kern.Tests.Core;

public sealed class MenuFileDiagnosticsTests
{
    private const string World = "testworld";

    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "kern-file-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string StreamingRoot => Path.Combine(_root, "StreamingAssets");

    private string PersistentRoot => Path.Combine(_root, "persistent");

    private string CacheDirectory => Path.Combine(PersistentRoot, DummyWorldMapArchive.CacheFolderName);

    // ── Проверка FMOD-банков ──

    [Test]
    public void CheckBankFile_MissingPersistentFile_FailsWithCachePath()
    {
        MenuFileCheckResult result = MenuFileDiagnosticsService.CheckBankFile(
            "key", "Master.bank", PersistentRoot, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(MenuFileCheckStatus.Failed));
        Assert.That(result.Detail, Does.Contain("persistentDataPath/Assets/Audio/Master.bank"));
    }

    [Test]
    public void CheckBankFile_ShortFile_Fails()
    {
        string audioRoot = Path.Combine(PersistentRoot, "Assets", "Audio");
        Directory.CreateDirectory(audioRoot);
        File.WriteAllBytes(Path.Combine(audioRoot, "Master.bank"), [0x46, 0x53]);

        MenuFileCheckResult result = MenuFileDiagnosticsService.CheckBankFile(
            "key", "Master.bank", PersistentRoot, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(MenuFileCheckStatus.Failed));
    }

    [Test]
    public void CheckBankFile_WrongMagic_Fails()
    {
        string audioRoot = Path.Combine(PersistentRoot, "Assets", "Audio");
        Directory.CreateDirectory(audioRoot);
        File.WriteAllBytes(
            Path.Combine(audioRoot, "Master.bank"),
            Encoding.ASCII.GetBytes("RIFFxxxx"));

        MenuFileCheckResult result = MenuFileDiagnosticsService.CheckBankFile(
            "key", "Master.bank", PersistentRoot, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(MenuFileCheckStatus.Failed));
        Assert.That(result.Detail, Does.Contain("FSB5"));
    }

    [Test]
    public void CheckBankFile_ValidFsb5_SucceedsWithExpectedMd5()
    {
        string audioRoot = Path.Combine(PersistentRoot, "Assets", "Audio");
        Directory.CreateDirectory(audioRoot);
        string path = Path.Combine(audioRoot, "Master.bank");
        File.WriteAllBytes(path, ValidBank());

        MenuFileCheckResult result = MenuFileDiagnosticsService.CheckBankFile(
            "key", "Master.bank", PersistentRoot, CancellationToken.None);

        // Сумма в детали сверяется с независимым подсчётом по файлу.
        using var md5 = MD5.Create();
        string expectedHash = BitConverter
            .ToString(md5.ComputeHash(File.ReadAllBytes(path)))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
        Assert.That(result.Status, Is.EqualTo(MenuFileCheckStatus.Ok));
        Assert.That(result.Detail, Does.Contain("FSB5"));
        Assert.That(result.Detail, Does.Contain($"MD5 {expectedHash[..16]}…"));
    }

    [Test]
    public void CheckBankFile_ReadsAudioBankFromPersistentAssets()
    {
        string audioRoot = Path.Combine(PersistentRoot, "Assets", "Audio");
        Directory.CreateDirectory(audioRoot);
        File.WriteAllBytes(Path.Combine(audioRoot, "Master.strings.bank"), ValidBank());

        MenuFileCheckResult result = MenuFileDiagnosticsService.CheckBankFile(
            "key", "Master.strings.bank", PersistentRoot, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(MenuFileCheckStatus.Ok));
    }

    [TestCase(0, "0 B")]
    [TestCase(512, "512 B")]
    [TestCase(2048, "2 KB")]
    [TestCase(5 * 1024 * 1024, "5 MB")]
    [TestCase(3L * 1024 * 1024 * 1024, "3 GB")]
    public void FormatBytes_FormatsBinaryUnits(long bytes, string expected)
    {
        Assert.That(MenuFileDiagnosticsService.FormatBytes(bytes), Is.EqualTo(expected));
    }

    // ── Снимок кэша офлайн-карты ──

    [Test]
    public void InspectLocalCache_EmptyRoots_NoSourceNoCache()
    {
        var snapshot = DummyWorldMapDiagnostics.InspectLocalCache(World, StreamingRoot, PersistentRoot);

        Assert.That(snapshot.SourcePresent, Is.False);
        Assert.That(snapshot.CachedMapPresent, Is.False);
        Assert.That(snapshot.Stamp, Is.EqualTo(DummyMapCacheStampState.Unknown));
    }

    [Test]
    public void InspectLocalCache_FreshlyExtractedZip_CurrentAndValid()
    {
        string archive = CreateArchiveWithMap();
        DummyWorldMapArchive.ExtractIfStale(
            archive,
            CacheDirectory,
            World,
            DummyWorldMapDiagnostics.BuildArchiveStamp(archive));

        var snapshot = DummyWorldMapDiagnostics.InspectLocalCache(World, StreamingRoot, PersistentRoot);

        Assert.That(snapshot.SourcePresent, Is.True);
        Assert.That(snapshot.SourceBytes, Is.EqualTo(new FileInfo(archive).Length));
        Assert.That(snapshot.CachedMapPresent, Is.True);
        Assert.That(snapshot.Stamp, Is.EqualTo(DummyMapCacheStampState.Current));
        Assert.That(snapshot.HeaderValid, Is.True);
        Assert.That(snapshot.HeaderCorrupted, Is.False);
    }

    [Test]
    public void InspectLocalCache_UpdatedArchive_Stale()
    {
        string archive = CreateArchiveWithMap();
        DummyWorldMapArchive.ExtractIfStale(
            archive,
            CacheDirectory,
            World,
            DummyWorldMapDiagnostics.BuildArchiveStamp(archive));
        File.SetLastWriteTimeUtc(archive, DateTime.UtcNow.AddMinutes(2));

        var snapshot = DummyWorldMapDiagnostics.InspectLocalCache(World, StreamingRoot, PersistentRoot);

        Assert.That(snapshot.Stamp, Is.EqualTo(DummyMapCacheStampState.Stale));
    }

    [Test]
    public void InspectLocalCache_GarbageCachedMap_CorruptedHeader()
    {
        string archive = CreateArchiveWithMap();
        string map = DummyWorldMapArchive.ExtractIfStale(
            archive,
            CacheDirectory,
            World,
            DummyWorldMapDiagnostics.BuildArchiveStamp(archive));
        File.WriteAllBytes(map, Enumerable.Range(1, 16).Select(index => (byte)index).ToArray());

        var snapshot = DummyWorldMapDiagnostics.InspectLocalCache(World, StreamingRoot, PersistentRoot);

        Assert.That(snapshot.CachedMapPresent, Is.True);
        Assert.That(snapshot.HeaderValid, Is.False);
        Assert.That(snapshot.HeaderCorrupted, Is.True);
    }

    [Test]
    public void InspectLocalCache_CopiedRawMap_Current()
    {
        string streamingMaps = CreateStreamingMaps();
        string rawMap = Path.Combine(streamingMaps, $"{World}_cells.mapb");
        File.WriteAllBytes(rawMap, ValidMap());
        DummyWorldMapArchive.CopyLocalMapToCache(rawMap, CacheDirectory, World);

        var snapshot = DummyWorldMapDiagnostics.InspectLocalCache(World, StreamingRoot, PersistentRoot);

        Assert.That(snapshot.SourcePresent, Is.True);
        Assert.That(snapshot.CachedMapPresent, Is.True);
        // Формат штампа сырой карты совпадает с тем, что пишет реальное копирование.
        Assert.That(snapshot.Stamp, Is.EqualTo(DummyMapCacheStampState.Current));
    }

    // ── Маппинг снимка на строку отчёта ──

    [Test]
    public void BuildMapCacheCheck_CurrentValid_OkWithDimensions()
    {
        var result = MenuFileDiagnosticsService.BuildMapCacheCheck("key", Snapshot(
            stamp: DummyMapCacheStampState.Current,
            cachedMapPresent: true,
            headerValid: true));

        Assert.That(result.Status, Is.EqualTo(MenuFileCheckStatus.Ok));
        Assert.That(result.Detail, Does.Contain("64×96"));
    }

    [Test]
    public void BuildMapCacheCheck_Stale_Warning()
    {
        var result = MenuFileDiagnosticsService.BuildMapCacheCheck("key", Snapshot(
            stamp: DummyMapCacheStampState.Stale,
            cachedMapPresent: true,
            headerValid: true));

        Assert.That(result.Status, Is.EqualTo(MenuFileCheckStatus.Warning));
    }

    [Test]
    public void BuildMapCacheCheck_CorruptedHeader_Failed()
    {
        var result = MenuFileDiagnosticsService.BuildMapCacheCheck("key", Snapshot(
            stamp: DummyMapCacheStampState.Current,
            cachedMapPresent: true,
            headerValid: false,
            headerCorrupted: true));

        Assert.That(result.Status, Is.EqualTo(MenuFileCheckStatus.Failed));
    }

    [Test]
    public void BuildMapCacheCheck_UnreadableFile_Warning()
    {
        var result = MenuFileDiagnosticsService.BuildMapCacheCheck("key", Snapshot(
            stamp: DummyMapCacheStampState.Unknown,
            cachedMapPresent: true,
            headerValid: false,
            headerCorrupted: false));

        Assert.That(result.Status, Is.EqualTo(MenuFileCheckStatus.Warning));
    }

    [Test]
    public void BuildMapCacheCheck_NoSourceNoCache_Failed()
    {
        var result = MenuFileDiagnosticsService.BuildMapCacheCheck("key", Snapshot(
            stamp: DummyMapCacheStampState.Unknown,
            cachedMapPresent: false,
            headerValid: false));

        Assert.That(result.Status, Is.EqualTo(MenuFileCheckStatus.Failed));
    }

    [Test]
    public void BuildMapCacheCheck_SourceOnlyInPackage_Warning()
    {
        var result = MenuFileDiagnosticsService.BuildMapCacheCheck("key", Snapshot(
            stamp: DummyMapCacheStampState.Unknown,
            cachedMapPresent: false,
            headerValid: false,
            sourcePresent: false,
            sourceInsidePackage: true));

        Assert.That(result.Status, Is.EqualTo(MenuFileCheckStatus.Warning));
    }

    [Test]
    public void BuildMapCacheCheck_SourceWithoutCache_Warning()
    {
        var result = MenuFileDiagnosticsService.BuildMapCacheCheck("key", Snapshot(
            stamp: DummyMapCacheStampState.Unknown,
            cachedMapPresent: false,
            headerValid: false,
            sourcePresent: true));

        Assert.That(result.Status, Is.EqualTo(MenuFileCheckStatus.Warning));
    }

    private static DummyMapCacheSnapshot Snapshot(
        DummyMapCacheStampState stamp,
        bool cachedMapPresent,
        bool headerValid,
        bool headerCorrupted = false,
        bool sourcePresent = false,
        bool sourceInsidePackage = false)
    {
        return new DummyMapCacheSnapshot(
            World,
            SourcePresent: sourcePresent,
            SourceInsidePackage: sourceInsidePackage,
            SourcePath: sourcePresent ? "source.zip" : null,
            SourceBytes: sourcePresent ? 1024 : 0,
            Stamp: stamp,
            CachedMapPresent: cachedMapPresent,
            CachedMapPath: cachedMapPresent ? "cached.mapb" : null,
            CachedMapBytes: cachedMapPresent ? 4096 : 0,
            HeaderValid: headerValid,
            HeaderCorrupted: headerCorrupted,
            WorldWidthCells: 64,
            WorldHeightCells: 96);
    }

    private string CreateStreamingMaps()
    {
        string maps = Path.Combine(StreamingRoot, "WorldMaps");
        Directory.CreateDirectory(maps);
        return maps;
    }

    private string CreateArchiveWithMap()
    {
        string maps = CreateStreamingMaps();
        string path = Path.Combine(maps, $"{World}_cells.zip");
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using Stream entry = archive.CreateEntry($"{World}_cells.mapb").Open();
        byte[] content = ValidMap();
        entry.Write(content, 0, content.Length);
        return path;
    }

    private static byte[] ValidMap()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(2);
            writer.Write(3);
            writer.Write(32);
            writer.Write(WorldLayerFileHeader.LegacyRLEFormatVersion);
            for (int index = 0; index < 6; index++)
            {
                writer.Write(-1L);
            }
        }

        return stream.ToArray();
    }

    private static byte[] ValidBank() => Encoding.ASCII.GetBytes("FSB5hello");
}
