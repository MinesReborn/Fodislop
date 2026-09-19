#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Connection.Client;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Networking;

public sealed class DummyWorldSimulationStateTests
{
    [Test]
    public void EmptyState_ReturnsUnloadedCellsAndNoConfiguration()
    {
        using var state = new DummyWorldSimulationState(new StubSupervisor(), new Kern.Tests.Networking.UnavailableDummyWorldMapSource());

        Assert.That(state.HasLayer, Is.False);
        Assert.That(state.GetCell(10, 20), Is.EqualTo(CellType.Unloaded));
        Assert.That(state.GetCellConfig(CellType.Empty), Is.Null);
    }

    [UnityEngine.TestTools.UnityTest]
    public System.Collections.IEnumerator FailedInitialization_CanBeRetried()
    {
        using var state = new DummyWorldSimulationState(new StubSupervisor(), new Kern.Tests.Networking.UnavailableDummyWorldMapSource());
        int attempts = 0;

        async UniTask FailOnce()
        {
            attempts++;
            await UniTask.Yield();
            throw new InvalidOperationException("injected failure");
        }

        async UniTask<bool> FailsAsExpected()
        {
            try
            {
                await state.EnsureInitializedAsync(FailOnce);
                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        bool failedAsExpected = false;
        yield return FailsAsExpected().ToCoroutine(result => failedAsExpected = result);
        Assert.That(failedAsExpected, Is.True);

        yield return state.EnsureInitializedAsync(() =>
            {
                attempts++;
                return UniTask.CompletedTask;
            })
            .ToCoroutine();

        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public async Task SuccessfulInitialization_IsIdempotentUntilReset()
    {
        using var state = new DummyWorldSimulationState(new StubSupervisor(), new Kern.Tests.Networking.UnavailableDummyWorldMapSource());
        int calls = 0;
        UniTask Initialize()
        {
            calls++;
            return UniTask.CompletedTask;
        }

        await state.EnsureInitializedAsync(Initialize);
        await state.EnsureInitializedAsync(Initialize);
        state.Reset();
        await state.EnsureInitializedAsync(Initialize);

        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public void StartupInventory_SeedsCatalogAndDoublesBattery()
    {
        Dictionary<ItemType, long> inventory =
            DummyWorldStartupResponder.CreateInitialInventory(new StubItemCatalog());

        Assert.That(inventory[ItemType.Rem], Is.EqualTo(1));
        Assert.That(inventory[ItemType.Battery], Is.EqualTo(2));
    }

    private sealed class StubSupervisor : IAsyncOperationSupervisor
    {
        public int ActiveCount => 0;

        public void Run(string operationName, Func<CancellationToken, UniTask> operation)
        {
            throw new AssertionException($"Unexpected operation '{operationName}'.");
        }

        public UniTask StopAsync(CancellationToken cancellationToken = default)
        {
            return UniTask.CompletedTask;
        }
    }

    private sealed class StubItemCatalog : IItemCatalog
    {
        public IEnumerable<ItemType> AllTypes => [ItemType.Rem, ItemType.Battery];

        public string GetName(ItemType type) => type.ToString();

        public string GetDescription(ItemType type) => string.Empty;

        public Texture2D? GetIcon(ItemType type) => null;
    }
}
