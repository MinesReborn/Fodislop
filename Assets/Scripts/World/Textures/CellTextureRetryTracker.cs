#nullable enable

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.World.Textures;

/// <summary>
/// Tracks in-flight texture requests and suppresses repeated failure logs with backoff.
/// </summary>
public sealed class CellTextureRetryTracker
{
    private const double FailedCellTextureRetrySeconds = 30.0;
    private static readonly Stopwatch RetryClock = Stopwatch.StartNew();

    private readonly ConcurrentDictionary<CellType, byte> _inFlightRequests = new();
    private readonly ConcurrentDictionary<CellType, double> _retryTimes = new();

    public int PendingRequestsCount => _inFlightRequests.Count;

    public bool ShouldThrottle(CellType cellType)
    {
        if (_retryTimes.TryGetValue(cellType, out double retryAfterSeconds) &&
            RetryClock.Elapsed.TotalSeconds < retryAfterSeconds)
        {
            return true;
        }

        return !_inFlightRequests.TryAdd(cellType, 0);
    }

    public async UniTask RunTrackedRequestAsync(
        CellType cellType,
        Func<CellType, CancellationToken, UniTask> requestAction,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await requestAction(cellType, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _retryTimes.TryRemove(cellType, out _);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            bool firstFailure = !_retryTimes.ContainsKey(cellType);
            _retryTimes[cellType] = RetryClock.Elapsed.TotalSeconds + FailedCellTextureRetrySeconds;

            if (firstFailure)
            {
                UnityEngine.Debug.LogWarning(
                    $"[WorldTextureManager] Texture for cell type {cellType} could not be " +
                    $"loaded: {exception.Message}. Retrying at most every " +
                    $"{FailedCellTextureRetrySeconds:F0}s.");
            }
        }
        finally
        {
            _inFlightRequests.TryRemove(cellType, out _);
        }
    }
}
