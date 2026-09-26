#nullable enable

using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Threading;
using Cysharp.Threading.Tasks;
using MinesServer.Networking.Connection.Client;
using NUnit.Framework;
using UnityEngine.TestTools;

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

    [Test]
    public void FirstExtraction_WritesMapAndStampWithoutTempFiles()
    {
        string archive = CreateArchive($"{World}_cells.mapb", ValidMap());

        string map = DummyWorldMapArchive.ExtractIfStale(archive, _CacheDirectory, World, "v1");

        Assert.That(File.ReadAllBytes(map), Is.EqualTo(ValidMap()));
        Assert.That(DummyWorldMapArchive.IsCacheCurrent(map, "v1"), Is.True);
        Assert.That(Directory.GetFiles(_CacheDirectory, "*.tmp"), Is.Empty);
    }

    [Test]
    public void SameStamp_KeepsExistingCache()
    {
        string archive = CreateArchive($"{World}_cells.mapb", ValidMap());
        string map = DummyWorldMapArchive.ExtractIfStale(archive, _CacheDirectory, World, "v1");
        File.WriteAllBytes(map, Marker);

        DummyWorldMapArchive.ExtractIfStale(archive, _CacheDirectory, World, "v1");

        Assert.That(File.ReadAllBytes(map), Is.EqualTo(Marker));
    }

    [Test]
    public void ChangedStamp_ExtractsAgain()
    {
        string archive = CreateArchive($"{World}_cells.mapb", ValidMap());
        string map = DummyWorldMapArchive.ExtractIfStale(archive, _CacheDirectory, World, "v1");
        File.WriteAllBytes(map, Marker);

        DummyWorldMapArchive.ExtractIfStale(archive, _CacheDirectory, World, "v2");

        Assert.That(File.ReadAllBytes(map), Is.EqualTo(ValidMap()));
        Assert.That(DummyWorldMapArchive.IsCacheCurrent(map, "v1"), Is.False);
    }

    [Test]
    public void MapWithoutStamp_ExtractsAgain()
    {
        // Сбой между заменой карты и записью отметки.
        string archive = CreateArchive($"{World}_cells.mapb", ValidMap());
        string map = DummyWorldMapArchive.ExtractIfStale(archive, _CacheDirectory, World, "v1");
        File.Delete(map + ".stamp");
        File.WriteAllBytes(map, Marker);

        DummyWorldMapArchive.ExtractIfStale(archive, _CacheDirectory, World, "v1");

        Assert.That(File.ReadAllBytes(map), Is.EqualTo(ValidMap()));
    }

    [Test]
    public void AbandonedTempFiles_AreRemoved_FreshOnesKept()
    {
        Directory.CreateDirectory(_CacheDirectory);
        string abandoned = Path.Combine(_CacheDirectory, "abandoned.tmp");
        string fresh = Path.Combine(_CacheDirectory, "fresh.tmp");
        File.WriteAllBytes(abandoned, Marker);
        File.WriteAllBytes(fresh, Marker);
        File.SetLastWriteTimeUtc(abandoned, DateTime.UtcNow.AddHours(-2));
        string archive = CreateArchive($"{World}_cells.mapb", ValidMap());

        DummyWorldMapArchive.ExtractIfStale(archive, _CacheDirectory, World, "v1");

        Assert.That(File.Exists(abandoned), Is.False);
        Assert.That(File.Exists(fresh), Is.True);
    }

    [Test]
    public void ArchiveWithoutMap_ThrowsAndLeavesNoCache()
    {
        string archive = CreateArchive("unrelated.bin", ValidMap());

        Assert.Throws<InvalidDataException>(
            () => DummyWorldMapArchive.ExtractIfStale(archive, _CacheDirectory, World, "v1"));

        Assert.That(File.Exists(Path.Combine(_CacheDirectory, $"{World}_cells.mapb")), Is.False);
        Assert.That(Directory.GetFiles(_CacheDirectory, "*.tmp"), Is.Empty);
    }

    [Test]
    public void CorruptArchive_ThrowsInvalidData()
    {
        string archive = Path.Combine(_root, "corrupt.zip");
        File.WriteAllBytes(archive, Marker);

        Assert.Throws<InvalidDataException>(
            () => DummyWorldMapArchive.ExtractIfStale(archive, _CacheDirectory, World, "v1"));
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

    private string _CacheDirectory => Path.Combine(_root, "cache");

    private static byte[] Marker => [1, 2, 3];

    private static byte[] ValidMap()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(2);
            writer.Write(3);
            writer.Write(32);
            writer.Write(0);
            writer.Write(new byte[64]);
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
