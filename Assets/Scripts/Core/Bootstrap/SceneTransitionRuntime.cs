#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core.Interfaces;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fodinae.Core;

internal static class SceneTransitionRuntime
{
    private static readonly TimeSpan PreviousSceneCleanupTimeout = TimeSpan.FromSeconds(10);

    public static async UniTask<Exception?> TryCleanupPreviousSceneAsync(
        Scene previousScene,
        Func<Scene, UniTask> prepareForUnload,
        TimeSpan? cleanupTimeout = null)
    {
        TimeSpan effectiveTimeout = cleanupTimeout ?? PreviousSceneCleanupTimeout;
        if (effectiveTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cleanupTimeout),
                "Scene cleanup timeout must be positive.");
        }

        string previousSceneName = previousScene.IsValid()
            ? previousScene.name
            : "<invalid>";
        Exception? lastFailure = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
            try
            {
                await prepareForUnload(previousScene).AttachExternalCancellation(timeoutCts.Token);
                if (previousScene.IsValid() && previousScene.isLoaded)
                {
                    await SceneManager.UnloadSceneAsync(previousScene)
                        .ToUniTask()
                        .AttachExternalCancellation(timeoutCts.Token);
                }

                return null;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                return new TimeoutException(
                    $"Cleanup of previous scene '{previousSceneName}' timed out after " +
                    $"{effectiveTimeout.TotalSeconds:F1} seconds.");
            }
            catch (Exception exception)
            {
                lastFailure = exception;
                if (attempt == 0)
                {
                    await UniTask.Yield();
                }
            }
        }

        return lastFailure;
    }

    public static void PublishSafely(
        Action<SceneTransitionStatus>? handlers,
        SceneTransitionStatus status)
    {
        if (handlers == null)
        {
            return;
        }

        foreach (Delegate subscriber in handlers.GetInvocationList())
        {
            try
            {
                ((Action<SceneTransitionStatus>)subscriber)(status);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[Bootstrap] Transition observer failed while handling " +
                    $"'{status.TargetSceneName}'/{status.Phase}: {exception}");
            }
        }
    }
}
