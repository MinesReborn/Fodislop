#nullable enable

using System;
using System.Collections;
using System.IO;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Lifecycle;
using Kern.Persistence;
using Kern.Rendering;
using Kern.World;
using MinesServer.Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Kern.Tests.PlayMode;

// Отказ диска посреди игры: ни одна правка мира не теряется, файл карты не
// портится, и после восстановления диска следующее сохранение дописывает всё.
// Асинхронный путь сохранения (пул потоков и возврат на главный поток) живёт
// только в игровом цикле, поэтому тесты в PlayMode.
[TestFixture]
public sealed class DiskFailurePlayModeTests
{
    private const string WorldCode = "disk_failure";
    private const int WorldWidth = 64;
    private const int WorldHeight = 64;
    private static readonly CellType _After = CellType.DeepObsidianRock;

    private string _dataRoot = null!;
    private AsyncOperationSupervisor _operations = null!;

    [SetUp]
    public void SetUp()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), $"kern_disk_failure_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataRoot);
        _operations = new AsyncOperationSupervisor();
    }

    [TearDown]
    public void TearDown()
    {
        _operations.Dispose();
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    [Test]
    public void FailedFlush_KeepsChangesDirtyAndTheNextFlushPersistsThem()
    {
        var disk = new FaultyDisk();
        var storage = Open(disk);
        try
        {
            storage.SetCell(1, 1, _After);
            disk.FailWrites = true;

            Assert.Throws<IOException>(() => storage.Flush(durable: true));
            Assert.That(storage.HasDirtyChunks, Is.True, "A failed write dropped the dirty chunk.");
            Assert.That(storage.GetCell(1, 1), Is.EqualTo(_After), "A failed write rolled back the change in memory.");

            disk.FailWrites = false;
            storage.Flush(durable: true);
            Assert.That(storage.HasDirtyChunks, Is.False);
        }
        finally
        {
            storage.Dispose();
        }

        Assert.That(ReadCell(1, 1), Is.EqualTo(_After));
    }

    [UnityTest]
    public IEnumerator FailedAsyncFlush_KeepsChangesDirtyAndTheNextFlushPersistsThem()
    {
        var disk = new FaultyDisk();
        var storage = Open(disk);
        try
        {
            storage.SetCell(2, 2, _After);
            disk.FailWrites = true;

            UniTask failed = storage.FlushAsync(durable: false).Preserve();
            yield return PlayModeHarness.WaitUntil(() => failed.Status.IsCompleted(), 10f, "Async flush hung on a failing disk.");
            Assert.Throws<IOException>(() => failed.GetAwaiter().GetResult());
            Assert.That(storage.HasDirtyChunks, Is.True, "A failed async write dropped the dirty chunk.");

            disk.FailWrites = false;
            yield return PlayModeHarness.Await(storage.FlushAsync(durable: true), 10f);
            Assert.That(storage.HasDirtyChunks, Is.False);
        }
        finally
        {
            storage.Dispose();
        }

        Assert.That(ReadCell(2, 2), Is.EqualTo(_After));
    }

    // Выход из игры (MapManager.OnApplicationQuit) делает синхронный Flush,
    // пока автосохранение ещё пишет в пуле потоков. Раньше это был дедлок:
    // главный поток ждал семафор, а запись ждала главный поток, чтобы его
    // отпустить, и редактор зависал намертво.
    [UnityTest]
    public IEnumerator SyncFlushDuringAsyncFlush_CompletesWithoutDeadlock()
    {
        var disk = new FaultyDisk();
        var storage = Open(disk);
        try
        {
            storage.SetCell(6, 6, _After);
            disk.WriteDelayMilliseconds = 200;
            UniTask background = storage.FlushAsync(durable: false).Preserve();

            storage.SetCell(7, 7, _After);
            storage.Flush(durable: true);

            yield return PlayModeHarness.Await(background, 10f);
            disk.WriteDelayMilliseconds = 0;
            Assert.That(storage.HasDirtyChunks, Is.False);
        }
        finally
        {
            storage.Dispose();
        }

        Assert.That(ReadCell(6, 6), Is.EqualTo(_After));
        Assert.That(ReadCell(7, 7), Is.EqualTo(_After));
    }

    [Test]
    public void TornWrite_DoesNotCorruptPreviouslySavedWorld()
    {
        var disk = new FaultyDisk();
        var storage = Open(disk);
        try
        {
            storage.SetCell(3, 3, _After);
            storage.Flush(durable: true);

            // Диск отказывает посреди записи следующего чанка: часть байтов
            // уже в файле, таблица смещений ещё старая.
            storage.SetCell(40, 40, _After);
            disk.FailAfterBytes = 7;
            Assert.Throws<IOException>(() => storage.Flush(durable: true));

            disk.FailAfterBytes = null;
            storage.Flush(durable: true);
        }
        finally
        {
            storage.Dispose();
        }

        Assert.That(ReadCell(3, 3), Is.EqualTo(_After));
        Assert.That(ReadCell(40, 40), Is.EqualTo(_After));
    }

    [Test]
    public void DiskFailingUntilShutdown_ReportsLossButKeepsFileReadable()
    {
        var disk = new FaultyDisk();
        var storage = Open(disk);
        storage.SetCell(4, 4, _After);
        storage.Flush(durable: true);
        storage.SetCell(5, 5, _After);
        disk.FailWrites = true;

        // Закрытие не может записать последнюю правку: об этом обязано быть
        // исключение, а не тихая потеря.
        Assert.Throws<IOException>(() => storage.Dispose());

        Assert.That(ReadCell(4, 4), Is.EqualTo(_After), "The failing shutdown corrupted already saved data.");
        Assert.That(ReadCell(5, 5), Is.Not.EqualTo(_After));
    }

    [Test]
    public void ConfigSaveFailure_LeavesPreviousConfigIntact()
    {
        string configPath = Path.Combine(_dataRoot, "Config", "client_config.json");
        var repository = new ClientConfigRepository(configPath);
        ClientConfig original = ClientConfigDefaults.Create(GraphicsQualityProfileLoader.LoadRequired());
        repository.Save(original);
        string saved = File.ReadAllText(configPath);

        // Каталог на месте временного файла — запись не может начаться.
        Directory.CreateDirectory(configPath + ".tmp");
        original.Audio.MasterVolume = 0.123f;

        Assert.Throws<IOException>(() => repository.Save(original));
        Assert.That(File.ReadAllText(configPath), Is.EqualTo(saved));
    }

    private MapStorage Open(FaultyDisk disk)
    {
        var storage = new MapStorage(_operations, _dataRoot, path => disk.Wrap(WorldLayer<CellType>.OpenMapFile(path)));
        storage.InitWorld(WorldCode, WorldWidth, WorldHeight);
        return storage;
    }

    private CellType ReadCell(int x, int y)
    {
        var storage = new MapStorage(_operations, _dataRoot);
        try
        {
            storage.InitWorld(WorldCode, WorldWidth, WorldHeight);
            return storage.GetCell(x, y);
        }
        finally
        {
            storage.Dispose();
        }
    }

    // Диск, который отказывает по команде теста: на каждой записи или после
    // заданного числа байтов, как при закончившемся месте.
    private sealed class FaultyDisk
    {
        public bool FailWrites { get; set; }

        public int? FailAfterBytes { get; set; }

        // Медленный диск: задержка на сбросе буферов, один раз на запись.
        public int WriteDelayMilliseconds { get; set; }

        public Stream Wrap(Stream inner) => new FaultyStream(inner, this);

        private sealed class FaultyStream(Stream inner, FaultyDisk disk) : Stream
        {
            public override bool CanRead => inner.CanRead;

            public override bool CanSeek => inner.CanSeek;

            public override bool CanWrite => inner.CanWrite;

            public override long Length => inner.Length;

            public override long Position
            {
                get => inner.Position;
                set => inner.Position = value;
            }

            public override void Flush()
            {
                if (disk.WriteDelayMilliseconds > 0)
                {
                    System.Threading.Thread.Sleep(disk.WriteDelayMilliseconds);
                }

                ThrowIfFailing();
                inner.Flush();
            }

            public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

            public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

            public override void SetLength(long value)
            {
                ThrowIfFailing();
                inner.SetLength(value);
            }

            public override void Write(byte[] buffer, int offset, int count) =>
                Write(new ReadOnlySpan<byte>(buffer, offset, count));

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                if (disk.FailAfterBytes is int budget)
                {
                    int written = Math.Min(budget, buffer.Length);
                    inner.Write(buffer[..written]);
                    disk.FailAfterBytes = budget - written;
                    if (written < buffer.Length)
                    {
                        throw new IOException("Injected disk failure: no space left on device.");
                    }

                    return;
                }

                ThrowIfFailing();
                inner.Write(buffer);
            }

            public override void WriteByte(byte value) => Write(new[] { value }, 0, 1);

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    inner.Dispose();
                }

                base.Dispose(disposing);
            }

            private void ThrowIfFailing()
            {
                if (disk.FailWrites)
                {
                    throw new IOException("Injected disk failure.");
                }
            }
        }
    }
}
