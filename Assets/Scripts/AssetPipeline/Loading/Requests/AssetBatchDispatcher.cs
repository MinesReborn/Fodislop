#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Utilities;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Utilities;
using UnityEngine;

using Kern.AssetPipeline.Cache;

namespace Kern.AssetPipeline.Loading;

using static ETagCalculator;

public sealed class AssetBatchDispatcher : IDisposable
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _pendingRequests = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<RuntimeAssetEntryPacket> _requestQueue = new();
    private readonly ConcurrentDictionary<string, byte> _missingAssets = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _reportedAssetFailures = new(StringComparer.Ordinal);
    private CancellationTokenSource? _loopCts = new();
    private bool _isDestroyed;
    private bool _batchLoopFailureLogged;

    public int PendingCount => _pendingRequests.Count;

    public int QueuedCount => _requestQueue.Count;

    public string[] GetPendingAssetNames() =>
        new List<string>(_pendingRequests.Keys).ToArray();

    public bool IsKnownMissing(string filename)
    {
        return _missingAssets.ContainsKey(filename);
    }

    public void MarkMissing(string filename)
    {
        _missingAssets.TryAdd(filename, 0);
    }
    public void ClearMissing() => _missingAssets.Clear();

    public bool TryReportFailure(string filename) =>
        _reportedAssetFailures.TryAdd(filename, 0);

    public void RemoveReportedFailure(string filename) =>
        _reportedAssetFailures.TryRemove(filename, out _);

    public void ClearReportedFailures() => _reportedAssetFailures.Clear();

    public void CancelPending()
    {
        _isDestroyed = true;
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _loopCts = null;

        foreach (KeyValuePair<string, TaskCompletionSource<byte[]>> pending in _pendingRequests)
        {
            if (_pendingRequests.TryRemove(pending.Key, out TaskCompletionSource<byte[]>? request))
            {
                request.TrySetCanceled();
            }
        }
    }

    // Активное соединение обязано ответить на запрос ассета: ответ либо
    // придёт, либо транспорт умрёт и поднимет дисконнект. Разрыв —
    // единственный честный признак того, что ответа не будет, поэтому по
    // нему ожидания фейлятся сразу, а не по настенному таймеру, способному
    // убить запрос с уже летящими по сети байтами.
    public void FailPendingOnDisconnect()
    {
        foreach (KeyValuePair<string, TaskCompletionSource<byte[]>> pending in _pendingRequests)
        {
            if (_pendingRequests.TryRemove(pending.Key, out TaskCompletionSource<byte[]>? request))
            {
                request.TrySetException(new InvalidOperationException(
                    $"Connection lost while waiting for asset '{pending.Key}'."));
            }
        }

        _requestQueue.Clear();
    }

    public void Dispose()
    {
        CancelPending();
        _missingAssets.Clear();
        _reportedAssetFailures.Clear();
    }

    public async UniTask ProcessBatchLoop(CancellationToken supervisorToken, Func<IConnectionService> connectionProvider)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            supervisorToken,
            _loopCts?.Token ?? CancellationToken.None);
        CancellationToken ct = linkedCancellation.Token;

        while (!ct.IsCancellationRequested && !_isDestroyed)
        {
            try
            {
                await UniTask.Delay(
                    AssetStreamingLimits.RequestBatchIntervalMilliseconds,
                    cancellationToken: ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (_isDestroyed || ct.IsCancellationRequested || _requestQueue.IsEmpty)
            {
                continue;
            }

            List<RuntimeAssetEntryPacket> batch = new();
            while (_requestQueue.TryDequeue(out var entry))
            {
                if (_pendingRequests.TryGetValue(entry.Filename, out var tcs) && !tcs.Task.IsCompleted)
                {
                    if (!batch.Exists(x => x.Filename == entry.Filename))
                    {
                        batch.Add(entry);
                    }
                }
            }

            if (batch.Count > 0 && !_isDestroyed && !ct.IsCancellationRequested)
            {
                try
                {
                    var connectionService = connectionProvider();
                    if (connectionService.IsConnected)
                    {
                        var assetRequest = new RuntimeAssetRequestPacket(batch);
                        connectionService.Send(new ClientPacket((uint)DateTimeOffset.UtcNow.Ticks, assetRequest));
                    }
                    else
                    {
                        foreach (var entry in batch)
                        {
                            if (_pendingRequests.TryRemove(entry.Filename, out var tcs))
                            {
                                tcs.TrySetException(new Exception("Connection lost while sending asset request batch"));
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException) when (_isDestroyed || ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    if (!_batchLoopFailureLogged)
                    {
                        Debug.LogWarning($"[ClientAssetLoader] Asset request batch deferred: {exception.Message}");
                        _batchLoopFailureLogged = true;
                    }

                    foreach (var entry in batch)
                    {
                        if (_pendingRequests.TryRemove(entry.Filename, out var tcs))
                        {
                            tcs.TrySetException(exception);
                        }
                    }
                }
            }
        }
    }

    public async UniTask<byte[]> RequestAssetBytesAsync(
        string filename,
        string etag,
        CancellationToken cancellationToken,
        Func<IConnectionService> connectionProvider,
        Func<ITextureStorageService?> textureStorageProvider)
    {
        var candidate = new TaskCompletionSource<byte[]>();
        var tcs = _pendingRequests.GetOrAdd(filename, candidate);
        bool isNew = ReferenceEquals(candidate, tcs);

        if (!isNew)
        {
            return await tcs.Task;
        }

        using var registration = cancellationToken.Register(() =>
        {
            tcs.TrySetCanceled();
            TryRemovePending(filename, tcs);
        });

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var connectionService = connectionProvider();
            if (!connectionService.IsConnected)
            {
                var tsm = textureStorageProvider();
                if (tsm != null)
                {
                    var localData = await tsm.GetTextureData(filename);
                    if (localData != null)
                    {
                        tcs.TrySetResult(localData);
                        TryRemovePending(filename, tcs);
                        return localData;
                    }
                }

                throw new InvalidOperationException(
                    $"No active connection and no local resource found for {filename}.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            _requestQueue.Enqueue(new RuntimeAssetEntryPacket(filename, etag ?? string.Empty));
            return await tcs.Task;
        }
        catch (Exception exception)
        {
            tcs.TrySetException(exception);
            TryRemovePending(filename, tcs);
            return await tcs.Task;
        }
    }

    private bool TryRemovePending(string filename, TaskCompletionSource<byte[]> request) =>
        ((ICollection<KeyValuePair<string, TaskCompletionSource<byte[]>>>)_pendingRequests)
            .Remove(new KeyValuePair<string, TaskCompletionSource<byte[]>>(filename, request));

    public async UniTask HandleAssetPacketAsync(
        ServerPacket obj,
        IPersistentAssetCache persistentCache,
        Action<string> onDisconnect,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (obj.Payload is not RuntimeAssetPacket assetPacket)
        {
            return;
        }

        string filename;
        try
        {
            filename = string.IsNullOrWhiteSpace(assetPacket.Filename)
                ? throw new InvalidDataException("Server returned an asset packet without a filename.")
                : assetPacket.Filename;
        }
        catch (Exception exception)
        {
            onDisconnect($"Invalid runtime asset packet: {exception.Message}");
            return;
        }

        if (!_pendingRequests.TryRemove(filename, out var tcs))
        {
            return;
        }

        try
        {
            byte[]? contents = assetPacket.Contents;

            if ((contents == null || contents.Length == 0) &&
                !string.IsNullOrEmpty(assetPacket.ETag))
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[]? cachedAsset = await persistentCache.GetAssetAsync(filename);
                cancellationToken.ThrowIfCancellationRequested();
                if (cachedAsset == null || cachedAsset.Length == 0)
                {
                    throw new InvalidDataException(
                        $"Asset '{filename}' is not cached and server returned empty contents.");
                }

                string? cachedETag = Calculate(cachedAsset);
                if (!string.Equals(
                    cachedETag,
                    assetPacket.ETag,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Server returned an empty response for '{filename}', but the cached asset " +
                        $"does not match ETag '{assetPacket.ETag}'.");
                }

                tcs.TrySetResult(cachedAsset);
                return;
            }

            if (contents == null || contents.Length == 0)
            {
                throw new InvalidDataException(
                    $"Server returned empty asset contents for '{filename}' without a usable ETag/cache entry.");
            }

            string etag = Calculate(contents) ??
                throw new InvalidDataException(
                    $"Asset '{filename}' produced no ETag after download.");
            if (!string.IsNullOrEmpty(assetPacket.ETag) &&
                !string.Equals(etag, assetPacket.ETag, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Server returned asset '{filename}' with ETag '{assetPacket.ETag}', " +
                    $"but its contents produce ETag '{etag}'.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            await persistentCache.SaveAssetAsync(filename, contents);
            cancellationToken.ThrowIfCancellationRequested();
            _missingAssets.TryRemove(filename, out _);
            tcs.TrySetResult(contents);
        }
        catch (Exception exception)
        {
            tcs.TrySetException(exception);
        }
    }

    public static bool IsTextureFile(string filename)
    {
        if (string.IsNullOrEmpty(filename))
        {
            return false;
        }

        string ext = Path.GetExtension(filename);
        return string.IsNullOrEmpty(ext) || ext == ".png";
    }

    public static bool IsAudioBank(string filename)
    {
        return string.Equals(
            Path.GetExtension(filename),
            ".bank",
            StringComparison.Ordinal);
    }
}
