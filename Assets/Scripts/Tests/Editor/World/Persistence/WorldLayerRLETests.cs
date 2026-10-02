#nullable enable

using System;
using System.Collections;
using System.IO;
using Cysharp.Threading.Tasks;
using Kern.Core.Lifecycle;
using Kern.Persistence;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Kern.Tests.World;

[TestFixture]
public class WorldLayerRLETests
{
    private string _tempFilePath = null!;
    private AsyncOperationSupervisor _operations = null!;

    [SetUp]
    public void SetUp()
    {
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"world_layer_test_{Guid.NewGuid():N}.mapb");
        _operations = new AsyncOperationSupervisor();
    }

    [TearDown]
    public void TearDown()
    {
        _operations.Dispose();

        if (File.Exists(_tempFilePath))
        {
            try
            {
                File.Delete(_tempFilePath);
            }
            catch
            {
                // Ignored in cleanup
            }
        }

        DeleteIfPresent(_tempFilePath + ".v0.backup");
        DeleteIfPresent(_tempFilePath + ".v1.backup");
        DeleteIfPresent(_tempFilePath + ".v1.backup.tmp");
        DeleteIfPresent(_tempFilePath + ".migrate.tmp");
        DeleteIfPresent(_tempFilePath + ".v2.migrate.tmp");
    }

    [Test]
    public void SetAndGet_SingleCell_ReturnsWrittenValue()
    {
        using (var layer = new WorldLayer<ushort>(_tempFilePath, WIDTH_CHUNKS: 2, HEIGHT_CHUNKS: 2, operations: _operations, CHUNK_SIZE: 32))
        {
            layer.SetCell(5, 5, 42);
            Assert.AreEqual(42, layer.GetCellSync(5, 5));
            Assert.AreEqual(0, layer.GetCellSync(0, 0));
        }
    }

    [Test]
    public void SetRegion_NotifiesOnlyWhenAChunkIsMaterialized()
    {
        using var layer = new WorldLayer<ushort>(
            _tempFilePath,
            WIDTH_CHUNKS: 2,
            HEIGHT_CHUNKS: 2,
            operations: _operations,
            CHUNK_SIZE: 32);
        int notifications = 0;
        layer.ChunkLoaded += (_, _, _, _) => notifications++;
        ushort[] payload = new ushort[32 * 32];

        layer.SetRegion(0, 0, 32, 32, payload);
        layer.SetRegion(0, 0, 32, 32, payload);

        Assert.That(notifications, Is.EqualTo(1));
    }

    [Test]
    public void SetRegion_FullChunkPacketReplacesPersistedChunk()
    {
        const ushort persistedValue = 11;
        const ushort streamedValue = 22;

        using (var layer = new WorldLayer<ushort>(
            _tempFilePath,
            WIDTH_CHUNKS: 1,
            HEIGHT_CHUNKS: 1,
            operations: _operations,
            CHUNK_SIZE: 32))
        {
            layer.SetCell(0, 0, persistedValue);
            layer.Flush();
        }

        using var reopenedLayer = new WorldLayer<ushort>(
            _tempFilePath,
            WIDTH_CHUNKS: 1,
            HEIGHT_CHUNKS: 1,
            operations: _operations,
            CHUNK_SIZE: 32);
        ushort[] payload = new ushort[32 * 32];
        Array.Fill(payload, streamedValue);

        reopenedLayer.SetRegion(0, 0, 32, 32, payload);

        Assert.That(reopenedLayer.GetCellSync(0, 0), Is.EqualTo(streamedValue));
        Assert.That(reopenedLayer.GetCellSync(31, 31), Is.EqualTo(streamedValue));
    }

    [Test]
    public void SetRegion_PartialPacketPreservesPersistedCells()
    {
        const ushort persistedValue = 31;
        const ushort streamedValue = 41;

        using (var layer = new WorldLayer<ushort>(
            _tempFilePath,
            WIDTH_CHUNKS: 1,
            HEIGHT_CHUNKS: 1,
            operations: _operations,
            CHUNK_SIZE: 32))
        {
            layer.SetCell(0, 0, persistedValue);
            layer.Flush();
        }

        using var reopenedLayer = new WorldLayer<ushort>(
            _tempFilePath,
            WIDTH_CHUNKS: 1,
            HEIGHT_CHUNKS: 1,
            operations: _operations,
            CHUNK_SIZE: 32);

        reopenedLayer.SetRegion(1, 1, 1, 1, [streamedValue]);

        Assert.That(reopenedLayer.GetCellSync(0, 0), Is.EqualTo(persistedValue));
        Assert.That(reopenedLayer.GetCellSync(1, 1), Is.EqualTo(streamedValue));
    }

    [Test]
    public void FlushAndReopen_PersistsAdaptiveEncodedData()
    {
        const ushort tileTypeA = 101;
        const ushort tileTypeB = 202;

        // Write and flush
        using (var layer = new WorldLayer<ushort>(_tempFilePath, WIDTH_CHUNKS: 2, HEIGHT_CHUNKS: 2, operations: _operations, CHUNK_SIZE: 32))
        {
            // Write a uniform block
            for (int x = 0; x < 32; x++)
            {
                for (int y = 0; y < 16; y++)
                {
                    layer.SetCell(x, y, tileTypeA);
                }

                for (int y = 16; y < 32; y++)
                {
                    layer.SetCell(x, y, tileTypeB);
                }
            }

            layer.Flush();
        }

        // Reopen and verify
        using (var reopenedLayer = new WorldLayer<ushort>(_tempFilePath, WIDTH_CHUNKS: 2, HEIGHT_CHUNKS: 2, operations: _operations, CHUNK_SIZE: 32))
        {
            for (int x = 0; x < 32; x++)
            {
                for (int y = 0; y < 16; y++)
                {
                    Assert.AreEqual(tileTypeA, reopenedLayer.GetCellSync(x, y), $"Mismatch at ({x}, {y})");
                }

                for (int y = 16; y < 32; y++)
                {
                    Assert.AreEqual(tileTypeB, reopenedLayer.GetCellSync(x, y), $"Mismatch at ({x}, {y})");
                }
            }
        }
    }

    [Test]
    public void OutOfBounds_ThrowsArgumentOutOfRangeException()
    {
        using var layer = new WorldLayer<ushort>(_tempFilePath, WIDTH_CHUNKS: 2, HEIGHT_CHUNKS: 2, operations: _operations, CHUNK_SIZE: 32);

        Assert.Throws<ArgumentOutOfRangeException>(() => layer.GetCellSync(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => layer.GetCellSync(64, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => layer.SetCell(0, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => layer.SetCell(0, 64, 1));
    }

    [Test]
    public void ReadChunk_SparseChunk_ReturnsMissingWithoutStartingLoad()
    {
        using var layer = new WorldLayer<ushort>(
            _tempFilePath,
            WIDTH_CHUNKS: 1,
            HEIGHT_CHUNKS: 1,
            operations: _operations,
            CHUNK_SIZE: 32);

        ChunkReadResult<ushort> result = layer.ReadChunk(0);

        Assert.That(result.Status, Is.EqualTo(ChunkReadStatus.Missing));
        Assert.That(result.Data, Is.Null);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public void ReadChunk_MaterializedChunk_ReturnsAvailableData()
    {
        using var layer = new WorldLayer<ushort>(
            _tempFilePath,
            WIDTH_CHUNKS: 1,
            HEIGHT_CHUNKS: 1,
            operations: _operations,
            CHUNK_SIZE: 32);
        layer.SetCell(0, 0, 41);

        ChunkReadResult<ushort> result = layer.ReadChunk(0);

        Assert.That(result.Status, Is.EqualTo(ChunkReadStatus.Available));
        Assert.That(result.Data, Is.Not.Null);
        Assert.That(result.Data![0], Is.EqualTo(41));
        Assert.That(result.Error, Is.Null);
    }

    [UnityTest]
    public IEnumerator ReadChunk_CorruptOffset_TransitionsFromLoadingToFailed()
    {
        using var layer = new WorldLayer<ushort>(
            _tempFilePath,
            WIDTH_CHUNKS: 1,
            HEIGHT_CHUNKS: 1,
            operations: _operations,
            CHUNK_SIZE: 32);
        layer.GetChunkOffsets()[0] = long.MaxValue;

        ChunkReadResult<ushort> initial = layer.ReadChunk(0);

        Assert.That(initial.Status, Is.EqualTo(ChunkReadStatus.Loading));
        yield return UniTask.WaitUntil(
            () => layer.ReadChunk(0).Status == ChunkReadStatus.Failed).ToCoroutine();
        ChunkReadResult<ushort> failed = layer.ReadChunk(0);
        Assert.That(failed.Data, Is.Null);
        Assert.That(failed.Error, Is.TypeOf<InvalidDataException>());
    }

    [Test]
    public void DirtyChunkEviction_PersistsBeforeRemovingOnlyMemoryCopy()
    {
        const ushort firstChunkValue = 17;
        const ushort secondChunkValue = 29;

        using (var layer = new WorldLayer<ushort>(
            _tempFilePath,
            WIDTH_CHUNKS: 2,
            HEIGHT_CHUNKS: 1,
            operations: _operations,
            CHUNK_SIZE: 32,
            maxRamChunks: 1))
        {
            layer.SetCell(0, 0, firstChunkValue);
            layer.SetCell(32, 0, secondChunkValue);
        }

        using var reopenedLayer = new WorldLayer<ushort>(
            _tempFilePath,
            WIDTH_CHUNKS: 2,
            HEIGHT_CHUNKS: 1,
            operations: _operations,
            CHUNK_SIZE: 32,
            maxRamChunks: 1);
        Assert.That(reopenedLayer.GetCellSync(0, 0), Is.EqualTo(firstChunkValue));
        Assert.That(reopenedLayer.GetCellSync(32, 0), Is.EqualTo(secondChunkValue));
    }

    [Test]
    public void LegacyV0Header_IsMigratedAtomicallyAndBackedUp()
    {
        const ushort expected = 77;
        const int chunkSize = 32;
        ushort[] cells = new ushort[chunkSize * chunkSize];
        cells[4 + (3 * chunkSize)] = expected;
        using (var stream = new FileStream(_tempFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            writer.Write(1);
            writer.Write(1);
            writer.Write(chunkSize);
            writer.Write(0);
            writer.Write(24L);
            stream.Position = 24;
            WorldChunkRLECodec.EncodeChunk(writer, cells, cells.Length);
            writer.Flush();
            stream.Flush(true);
        }

        using (var migrated = new WorldLayer<ushort>(
            _tempFilePath,
            WIDTH_CHUNKS: 1,
            HEIGHT_CHUNKS: 1,
            operations: _operations,
            CHUNK_SIZE: 32))
        {
            Assert.That(migrated.GetCellSync(3, 4), Is.EqualTo(expected));
        }

        Assert.That(File.Exists(_tempFilePath + ".v0.backup"), Is.True);

        using var header = File.OpenRead(_tempFilePath);
        Assert.That(
            WorldLayerFileHeader.TryReadFormatVersion(header),
            Is.EqualTo(WorldLayerFileHeader.CurrentFormatVersion));
    }

    [UnityTest]
    public IEnumerator MigrateLegacyFileAsync_ConvertsV0RLEFile()
    {
        const int widthChunks = 1;
        const int heightChunks = 1;
        const int chunkSize = 4;
        byte[] expected = [2, 2, 4, 7, 7, 7, 1, 3, 3, 3, 3, 8, 5, 5, 5, 5];
        try
        {
            using (var stream = new FileStream(
                       _tempFilePath,
                       FileMode.Create,
                       FileAccess.ReadWrite,
                       FileShare.None))
            {
                using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
                writer.Write(widthChunks);
                writer.Write(heightChunks);
                writer.Write(chunkSize);
                writer.Write(0);
                writer.Write(24L);
                stream.Position = 24;
                WorldChunkRLECodec.EncodeChunk(writer, expected, expected.Length);
                writer.Flush();
                stream.Flush(true);
            }

            yield return WorldLayer<byte>.MigrateLegacyFileAsync(
                _tempFilePath,
                widthChunks,
                heightChunks,
                chunkSize).ToCoroutine();

            using var migrated = new FileStream(_tempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            long[] offsets = new long[1];
            Assert.That(
                WorldLayerFileHeader.TryReadFormatVersion(migrated),
                Is.EqualTo(WorldLayerFileHeader.CurrentFormatVersion));
            Assert.That(
                WorldLayerFileHeader.TryReadHeader(migrated, widthChunks, heightChunks, chunkSize, offsets),
                Is.True);
            migrated.Position = offsets[0];
            using var reader = new BinaryReader(migrated, System.Text.Encoding.UTF8, leaveOpen: true);
            CollectionAssert.AreEqual(
                expected,
                WorldChunkV2Codec.DecodeChunk<byte>(reader, expected.Length));
        }
        finally
        {
            DeleteIfPresent(_tempFilePath + ".v0.backup");
            DeleteIfPresent(_tempFilePath + ".v1.backup");
            DeleteIfPresent(_tempFilePath + ".v1.backup.tmp");
            DeleteIfPresent(_tempFilePath + ".migrate.tmp");
            DeleteIfPresent(_tempFilePath + ".v2.migrate.tmp");
            DeleteIfPresent(_tempFilePath);
        }
    }

    [UnityTest]
    public IEnumerator CorruptChunk_LoadsAndScansAsZeroes()
    {
        const int chunkSize = 4;
        const int chunkArea = chunkSize * chunkSize;
        long chunkOffset;
        using (var layer = new WorldLayer<ushort>(
                   _tempFilePath,
                   WIDTH_CHUNKS: 1,
                   HEIGHT_CHUNKS: 1,
                   operations: _operations,
                   CHUNK_SIZE: chunkSize))
        {
            layer.SetCell(0, 0, 123);
            layer.Flush(flushToDisk: true);
            chunkOffset = layer.GetChunkOffsets()[0];
        }

        using (var stream = new FileStream(_tempFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            stream.Position = chunkOffset + 16;
            int checksumByte = stream.ReadByte();
            stream.Position--;
            stream.WriteByte((byte)(checksumByte ^ 0x80));
            stream.Flush(true);
        }

        using var reopenedLayer = new WorldLayer<ushort>(
            _tempFilePath,
            WIDTH_CHUNKS: 1,
            HEIGHT_CHUNKS: 1,
            operations: _operations,
            CHUNK_SIZE: chunkSize);
        Assert.That(reopenedLayer.GetCellSync(0, 0), Is.Zero);

        int visitedRuns = 0;
        ushort visitedValue = ushort.MaxValue;
        int visitedCellCount = 0;
        yield return reopenedLayer.VisitStoredChunkRunsAsync(
            (_, value, count) =>
            {
                visitedValue = value;
                visitedCellCount = count;
                visitedRuns++;
            },
            progress: null).ToCoroutine();
        Assert.That(visitedRuns, Is.EqualTo(1));
        Assert.That(visitedValue, Is.Zero);
        Assert.That(visitedCellCount, Is.EqualTo(chunkArea));
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
