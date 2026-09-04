#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Utilities;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Utilities;
using UnityEngine;

namespace Fodinae;

using static ETagCalculator;

/// <summary>
/// Batches outgoing network asset requests and dispatches incoming asset packets.
/// </summary>
public sealed class AssetBatchDispatcher : IDisposable
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _pendingRequests = new();
    private readonly ConcurrentQueue<RuntimeAssetEntryPacket> _requestQueue = new();
    private readonly ConcurrentDictionary<string, byte> _missingAssets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _reportedAssetFailures = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _loopCts = new();
    private bool _isDestroyed;
    private bool _batchLoopFailureLogged;

    public int PendingCount => _pendingRequests.Count;

    public int QueuedCount => _requestQueue.Count;

    public string[] GetPendingAssetNames() =>
        new List<string>(_pendingRequests.Keys).ToArray();

    public bool IsKnownMissing(string filename)
    {
        string clean = filename.TrimStart('/').ToLowerInvariant();
        return _missingAssets.ContainsKey(clean);
    }

    public void MarkMissing(string filename)
    {
        string clean = filename.TrimStart('/').ToLowerInvariant();
        _missingAssets.TryAdd(clean, 0);
    }

    public void RemoveMissing(string filename)
    {
        string clean = filename.TrimStart('/').ToLowerInvariant();
        _missingAssets.TryRemove(clean, out _);
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
                    ProjectRuntimeContracts.AssetStreaming.RequestBatchIntervalMilliseconds,
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
        bool isNew = false;
        var tcs = _pendingRequests.GetOrAdd(filename, _ =>
        {
            isNew = true;
            return new TaskCompletionSource<byte[]>();
        });

        if (!isNew)
        {
            return await tcs.Task;
        }

        using var registration = cancellationToken.Register(() =>
        {
            tcs.TrySetCanceled();
            _pendingRequests.TryRemove(filename, out _);
        });

        var connectionService = connectionProvider();
        if (!connectionService.IsConnected)
        {
            try
            {
                var tsm = textureStorageProvider();
                if (tsm != null)
                {
                    var localData = await tsm.GetTextureData(filename);
                    if (localData != null)
                    {
                        tcs.TrySetResult(localData);
                        _pendingRequests.TryRemove(filename, out _);
                        return localData;
                    }
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
                _pendingRequests.TryRemove(filename, out _);
                throw;
            }

            var noConnEx = new Exception($"No active connection and no local resource found for {filename}");
            tcs.TrySetException(noConnEx);
            _pendingRequests.TryRemove(filename, out _);
            throw noConnEx;
        }

        _requestQueue.Enqueue(new RuntimeAssetEntryPacket(filename, etag ?? string.Empty));

        try
        {
            return await tcs.Task;
        }
        catch
        {
            _pendingRequests.TryRemove(filename, out _);
            throw;
        }
    }

    public async UniTask HandleAssetPacketAsync(
        ServerPacket obj,
        IPersistentAssetCache persistentCache,
        Action<string> onDisconnect)
    {
        if (obj.Payload is not RuntimeAssetPacket assetPacket)
        {
            return;
        }

        string filename;
        try
        {
            filename = string.IsNullOrWhiteSpace(assetPacket.Filename)
                ? throw new InvalidDataException("Server returned an asset packet without a filename.")
                : assetPacket.Filename.TrimStart('/').ToLowerInvariant();
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
                byte[]? cachedAsset = await persistentCache.GetAssetAsync(filename);
                if (cachedAsset == null || cachedAsset.Length == 0)
                {
                    throw new InvalidDataException(
                        $"Asset '{filename}' is not cached and server returned empty contents.");
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
            await persistentCache.SaveAssetAsync(filename, contents, etag);
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

        if (filename.EndsWith(".webp.bytes", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string ext = Path.GetExtension(filename).ToLowerInvariant();
        return string.IsNullOrEmpty(ext) || ext == ".png" || ext == ".jpg" ||
            ext == ".jpeg" || ext == ".webp" || ext == ".gif" ||
            ext == ".exr";
    }

    public static bool IsAudioBank(string filename)
    {
        return string.Equals(
            Path.GetExtension(filename),
            ".bank",
            StringComparison.OrdinalIgnoreCase);
    }
}
