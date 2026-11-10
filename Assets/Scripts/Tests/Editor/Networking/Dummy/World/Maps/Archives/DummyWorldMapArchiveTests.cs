#nullable enable

using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Persistence;
using MinesServer.Data;
using NUnit.Framework;
using UnityEngine.TestTools;

using Kern.Networking.Dummy;

namespace Kern.Tests.Networking;

public sealed class DummyWorldMapArchiveTests
{
    private const string World = "testworld";

    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "kern-dummy-map-" + Guid.NewGuid().ToString("N"));
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

    [TestCase("jar:file:///data/app/game.apk!/assets", "WorldMaps/WorldMaps.manifest", "jar:file:///data/app/game.apk!/assets/WorldMaps/WorldMaps.manifest")]
    [TestCase("jar:file:///data/app/game.apk!/assets/", "World Maps/map file.zip", "jar:file:///data/app/game.apk!/assets/World%20Maps/map%20file.zip")]
    public void CombineStreamingUri_UsesUriSeparatorsAndEscapesPathSegments(
        string root,
        string relativePath,
        string expected)
    {
        Assert.That(
            DummyWorldMapArchive.CombineStreamingUri(root, relativePath),
            Is.EqualTo(expected));
    }

    [TestCase("../outside")]
    [TestCase("/absolute")]
    [TestCase("WorldMaps//map.zip")]
    public void CombineStreamingUri_RejectsInvalidRelativePaths(string relativePath)
    {
        Assert.Throws<ArgumentException>(() =>
            DummyWorldMapArchive.CombineStreamingUri("jar:file:///game.apk!/assets", relativePath));
    }

    [Test]
    public void FirstExtraction_WritesMapAndStampWithoutTempFiles()
    {
        string archive = CreateArchive($"{World}_cells.mapb", ValidMap());

        string map = DummyWorldMapArchive.ExtractIfStale(archive, CacheDirectory, World, "v1");

        Assert.That(File.ReadAllBytes(map), Is.EqualTo(ValidMap()));
        Assert.That(DummyWorldMapArchive.IsCacheCurrent(map, "v1"), Is.True);
        Assert.That(Directory.GetFiles(CacheDirectory, "*.tmp"), Is.Empty);
    }

    [Test]
    public void SameStamp_KeepsExistingCache()
    {
        string archive = CreateArchive($"{World}_cells.mapb", ValidMap());
        string map = DummyWorldMapArchive.ExtractIfStale(archive, CacheDirectory, World, "v1");
        File.WriteAllBytes(map, Marker);

        DummyWorldMapArchive.ExtractIfStale(archive, CacheDirectory, World, "v1");

        Assert.That(File.ReadAllBytes(map), Is.EqualTo(Marker));
    }

    [Test]
    public void ChangedStamp_ExtractsAgain()
    {
        string archive = CreateArchive($"{World}_cells.mapb", ValidMap());
        string map = DummyWorldMapArchive.ExtractIfStale(archive, CacheDirectory, World, "v1");
        File.WriteAllBytes(map, Marker);

        DummyWorldMapArchive.ExtractIfStale(archive, CacheDirectory, World, "v2");

        Assert.That(File.ReadAllBytes(map), Is.EqualTo(ValidMap()));
        Assert.That(DummyWorldMapArchive.IsCacheCurrent(map, "v1"), Is.False);
    }

    [Test]
    public void LocalStreamingMap_IsCopiedBeforeMigrationAndMigratedCacheIsReused()
    {
        string source = Path.Combine(_root, "StreamingAssets", $"{World}_cells.mapb");
        byte[] sourceBytes = ValidMap();
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllBytes(source, sourceBytes);

        string cache = DummyWorldMapArchive.CopyLocalMapToCache(source, CacheDirectory, World);
        WorldLayer<CellType>.MigrateLegacyFileIfRequired(cache, 2, 3, 32);
        string reusedCache = DummyWorldMapArchive.CopyLocalMapToCache(source, CacheDirectory, World);

        Assert.That(cache, Is.Not.EqualTo(source));
        Assert.That(reusedCache, Is.EqualTo(cache));
        Assert.That(File.ReadAllBytes(source), Is.EqualTo(sourceBytes));
        Assert.That(ReadFormatVersion(source), Is.EqualTo(WorldLayerFileHeader.LegacyRLEFormatVersion));
        Assert.That(ReadFormatVersion(cache), Is.EqualTo(WorldLayerFileHeader.CurrentFormatVersion));
    }

    [Test]
    public void MapWithoutStamp_ExtractsAgain()
    {
        // Сбой между заменой карты и записью отметки.
        string archive = CreateArchive($"{World}_cells.mapb", ValidMap());
        string map = DummyWorldMapArchive.ExtractIfStale(archive, CacheDirectory, World, "v1");
        File.Delete(map + ".stamp");
        File.WriteAllBytes(map, Marker);

        DummyWorldMapArchive.ExtractIfStale(archive, CacheDirectory, World, "v1");

        Assert.That(File.ReadAllBytes(map), Is.EqualTo(ValidMap()));
    }

    [Test]
    public void AbandonedTempFiles_AreRemoved_FreshOnesKept()
    {
        Directory.CreateDirectory(CacheDirectory);
        string abandoned = Path.Combine(CacheDirectory, "abandoned.tmp");
        string fresh = Path.Combine(CacheDirectory, "fresh.tmp");
        File.WriteAllBytes(abandoned, Marker);
        File.WriteAllBytes(fresh, Marker);
        File.SetLastWriteTimeUtc(abandoned, DateTime.UtcNow.AddHours(-2));
        string archive = CreateArchive($"{World}_cells.mapb", ValidMap());

        DummyWorldMapArchive.ExtractIfStale(archive, CacheDirectory, World, "v1");

        Assert.That(File.Exists(abandoned), Is.False);
        Assert.That(File.Exists(fresh), Is.True);
    }

    [Test]
    public void ArchiveWithoutMap_ThrowsAndLeavesNoCache()
    {
        string archive = CreateArchive("unrelated.bin", ValidMap());

        Assert.Throws<InvalidDataException>(
            () => DummyWorldMapArchive.ExtractIfStale(archive, CacheDirectory, World, "v1"));

        Assert.That(File.Exists(Path.Combine(CacheDirectory, $"{World}_cells.mapb")), Is.False);
        Assert.That(Directory.GetFiles(CacheDirectory, "*.tmp"), Is.Empty);
    }

    [Test]
    public void CorruptArchive_ThrowsInvalidData()
    {
        string archive = Path.Combine(_root, "corrupt.zip");
        File.WriteAllBytes(archive, Marker);

        Assert.Throws<InvalidDataException>(
            () => DummyWorldMapArchive.ExtractIfStale(archive, CacheDirectory, World, "v1"));
    }

    [Test]
    public void HeaderValidation_RejectsGarbageAndTruncation()
    {
        string valid = Path.Combine(_root, "valid.mapb");
        string garbage = Path.Combine(_root, "garbage.mapb");
        string truncated = Path.Combine(_root, "truncated.mapb");
        File.WriteAllBytes(valid, ValidMap());
        File.WriteAllBytes(garbage, new byte[32]);
        File.WriteAllBytes(truncated, new byte[5]);

        Assert.That(DummyWorldMapArchive.HasValidHeader(valid), Is.True);
        Assert.That(DummyWorldMapArchive.HasValidHeader(garbage), Is.False);
        Assert.That(DummyWorldMapArchive.HasValidHeader(truncated), Is.False);
    }

    [Test]
    public void ReadDimensions_RejectsChunkSizeDifferentFromRuntimeFormat()
    {
        string map = Path.Combine(_root, "wrong_chunk_size.mapb");
        using (var stream = File.Create(map))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(2);
            writer.Write(3);
            writer.Write(16);
            writer.Write(WorldLayerFileHeader.LegacyRLEFormatVersion);
            for (int index = 0; index < 6; index++)
            {
                writer.Write(-1L);
            }
        }

        Assert.Throws<InvalidDataException>(() => DummyWorldMapArchive.ReadDimensions(map));
    }

    [Test]
    public void ReadDimensions_RejectsTruncatedOrInvalidOffsetTable()
    {
        string truncated = Path.Combine(_root, "truncated_offsets.mapb");
        using (var stream = File.Create(truncated))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(2);
            writer.Write(3);
            writer.Write(32);
            writer.Write(WorldLayerFileHeader.CurrentFormatVersion);
            for (int index = 0; index < 5; index++)
            {
                writer.Write(-1L);
            }
        }

        string invalid = Path.Combine(_root, "invalid_offset.mapb");
        using (var stream = File.Create(invalid))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(2);
            writer.Write(3);
            writer.Write(32);
            writer.Write(WorldLayerFileHeader.CurrentFormatVersion);
            writer.Write(0L);
            for (int index = 1; index < 6; index++)
            {
                writer.Write(-1L);
            }
        }

        Assert.Throws<InvalidDataException>(() => DummyWorldMapArchive.ReadDimensions(truncated));
        Assert.Throws<InvalidDataException>(() => DummyWorldMapArchive.ReadDimensions(invalid));
    }

    [TestCase(2048, 1, 32)]
    [TestCase(int.MaxValue, 1, 1024)]
    public void ReadDimensions_RejectsDimensionsThatWorldInitPacketCannotRepresent(
        int widthChunks,
        int heightChunks,
        int chunkSize)
    {
        string map = Path.Combine(_root, "oversized.mapb");
        using (var stream = File.Create(map))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(widthChunks);
            writer.Write(heightChunks);
            writer.Write(chunkSize);
            writer.Write(0);
        }

        Assert.Throws<InvalidDataException>(() => DummyWorldMapArchive.ReadDimensions(map));
    }

    [UnityTest]
    public IEnumerator FailedPreparation_IsNotCached() => UniTask.ToCoroutine(async () =>
    {
        var source = new DummyWorldMapSource(new ImmediateSupervisor());

        Exception first = await CaptureAsync(source);
        Exception second = await CaptureAsync(source);

        Assert.That(first, Is.InstanceOf<FileNotFoundException>());
        Assert.That(second, Is.InstanceOf<FileNotFoundException>());
        // Запомненная неудача вернула бы тот же объект исключения.
        Assert.That(second, Is.Not.SameAs(first));
    });

    private static async UniTask<Exception> CaptureAsync(DummyWorldMapSource source)
    {
        try
        {
            await source.GetMapFileAsync("kern_missing_test_world", CancellationToken.None);
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new AssertionException("Preparation of a missing world must fail.");
    }

    private string CacheDirectory => Path.Combine(_root, "cache");

    private static byte[] Marker => new byte[] { 1, 2, 3 };

    private static int ReadFormatVersion(string path)
    {
        using var stream = File.OpenRead(path);
        return WorldLayerFileHeader.TryReadFormatVersion(stream) ?? -1;
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

    private string CreateArchive(string entryName, byte[] content)
    {
        string path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using Stream entry = archive.CreateEntry(entryName).Open();
        entry.Write(content, 0, content.Length);
        return path;
    }
}

// Запускает операцию сразу и не ждёт её: для теста подготовки карты этого достаточно.
internal sealed class ImmediateSupervisor : Kern.IAsyncOperationSupervisor
{
    public int ActiveCount => 0;

    public void Run(string operationName, Func<CancellationToken, UniTask> operation)
    {
        _ = operation(CancellationToken.None);
    }

    public UniTask StopAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;
}

internal sealed class UnavailableDummyWorldMapSource : IDummyWorldMapSource
{
    public UniTask<string> GetMapFileAsync(string worldCodeName, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Tests using this source must not open the dummy world map.");
}
