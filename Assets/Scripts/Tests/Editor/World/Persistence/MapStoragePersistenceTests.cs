#nullable enable

using System;
using System.Collections;
using System.IO;
using Cysharp.Threading.Tasks;
using Kern.Core.Lifecycle;
using Kern.World;
using MinesServer.Data;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Kern.Tests.World;

[TestFixture]
public sealed class MapStoragePersistenceTests
{
    [UnityTest]
    public IEnumerator ConcurrentFlushesAndAsyncDispose_PreserveWorldData()
    {
        string worldCode = $"persistence_test_{Guid.NewGuid():N}";
        string mapPath = string.Empty;
        string backupPath = string.Empty;
        using var operations = new AsyncOperationSupervisor();
        var storage = new MapStorage(operations);
        var reopened = new MapStorage(operations);
        CellType expected = (CellType)123;

        try
        {
            storage.InitWorld(worldCode, width: 64, height: 32);
            mapPath = storage.MapFilePath;
            backupPath = storage.BackupMapFilePath;
            storage.SetCell(0, 0, expected);

            yield return UniTask.WhenAll(
                storage.FlushAsync(durable: false),
                storage.FlushAsync(durable: true)).ToCoroutine();
            yield return storage.DisposeAsync().ToCoroutine();

            reopened.InitWorld(worldCode, width: 64, height: 32);
            Assert.That(reopened.GetCell(0, 0), Is.EqualTo(expected));
        }
        finally
        {
            reopened.Dispose();
            storage.Dispose();
            DeleteIfPresent(mapPath);
            DeleteIfPresent(backupPath);
        }
    }

    private static void DeleteIfPresent(string path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
