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
public class WorldLayerRleTests
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
        DeleteIfPresent(_tempFilePath + ".migrate.tmp");
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
    public void FlushAndReopen_PersistsRLEEncodedData()
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
        using (var layer = new WorldLayer<ushort>(
            _tempFilePath,
            WIDTH_CHUNKS: 1,
            HEIGHT_CHUNKS: 1,
            operations: _operations,
            CHUNK_SIZE: 32))
        {
            layer.SetCell(3, 4, expected);
        }

        using (var stream = new FileStream(
            _tempFilePath,
            FileMode.Open,
            FileAccess.Write,
            FileShare.None))
        using (var writer = new BinaryWriter(stream))
        {
            stream.Seek(sizeof(int) * 3, SeekOrigin.Begin);
            writer.Write(0);
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

        using var header = new BinaryReader(File.OpenRead(_tempFilePath));
        header.BaseStream.Seek(sizeof(int) * 3, SeekOrigin.Begin);
        Assert.That(header.ReadInt32(), Is.EqualTo(1));
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
