#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Kern.Networking.Offline;

internal static class DummyWorldInitialization
{
    public static async UniTask InitializeAsync(
        DummyWorldSimulationState worldState,
        DummyWorldStartupResponder worldStartup,
        string worldCodeName,
        int lifecycleVersion,
        string playerName,
        long playerLevel,
        long playerCurrency,
        ushort mockBotId,
        CancellationToken cancellationToken,
        Action<string> disconnect)
    {
        try
        {
            await worldState.EnsureInitializedAsync(() => worldStartup.InitializeAsync(
                worldCodeName, lifecycleVersion, playerName, playerLevel, playerCurrency, mockBotId));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Disconnect/reset cancels the active map open without stopping the application supervisor.
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            Debug.LogException(new InvalidOperationException(
                "[DummyConnection] Offline world initialization failed before the world became ready.",
                exception));
            disconnect($"Offline world initialization failed: {exception.Message}");
        }
    }
}
