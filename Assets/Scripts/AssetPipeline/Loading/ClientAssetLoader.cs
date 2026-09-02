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
using UnityEngine.Rendering;
using VContainer;

namespace Fodinae
{
    using static ETagCalculator;

    [DefaultExecutionOrder(-10000)]
    public class ClientAssetLoader : MonoBehaviour, IAssetLoader, IAssetSubscription
    {
        private AssetCache _cache = null!;

        private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _pendingRequests = new();
        private readonly ConcurrentQueue<RuntimeAssetEntryPacket> _requestQueue = new();
        private readonly ConcurrentDictionary<string, byte> _missingAssets = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, byte> _reportedAssetFailures = new(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource? _loopCts;
        private bool _batchLoopStarted;
        private bool _isDestroyed;
        private bool _batchLoopFailureLogged;

        private AssetCache Cache => _cache ??
            throw new ObjectDisposedException(nameof(ClientAssetLoader));

        public int PendingAssetCount => _pendingRequests.Count;
        public int QueuedAssetCount => _requestQueue.Count;

        public string[] GetPendingAssetNames()
        {
            return new List<string>(_pendingRequests.Keys).ToArray();
        }

        [Inject]
        private IConnectionService _connectionService = null!;
        [Inject]
        private ITextureStorageService _textureStorage = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;
        [Inject]
        private IPersistentAssetCache _persistentCache = null!;

        private IConnectionService ConnectionService =>
            _connectionService ??
            throw new InvalidOperationException(
                "ClientAssetLoader requires IConnectionService before loading assets.");

        private ITextureStorageService TextureStorage => _textureStorage;

        private bool _assetSubscriptionEstablished;
        private IConnectionService? _subscribedConnection;

        public bool IsAssetSubscriptionEstablished => _assetSubscriptionEstablished;

        protected void Awake()
        {
            _isDestroyed = false;
            _batchLoopFailureLogged = false;
            _cache = new AssetCache(LoadBytesFromServer, () => _operations);
            _loopCts = new CancellationTokenSource();
        }

        protected void Start()
        {
            if (_operations == null)
            {
                throw new InvalidOperationException(
                    "ClientAssetLoader requires IAsyncOperationSupervisor before startup.");
            }

            if (_batchLoopStarted)
            {
                return;
            }

            _batchLoopStarted = true;
            _operations.Run("asset_request_batch_loop", ProcessBatchLoop);
        }

        protected void OnDestroy()
        {
            // Mark teardown before cancelling: the loop can resume once between
            // cancellation and its next await continuation.
            _isDestroyed = true;
            _loopCts?.Cancel();
            _loopCts?.Dispose();
            if (_cache != null)
            {
                _cache.Clear();
                _cache = null!;
            }
            _missingAssets.Clear();
            _reportedAssetFailures.Clear();

            foreach (KeyValuePair<string, TaskCompletionSource<byte[]>> pending in _pendingRequests)
            {
                if (_pendingRequests.TryRemove(pending.Key, out TaskCompletionSource<byte[]>? request))
                {
                    request.TrySetCanceled();
                }
            }

            UnsubscribeFromConnection();
        }

        /// <summary>
        /// Binds the packet stream after VContainer injection. Unity may call
        /// Awake/OnEnable before [Inject] has populated the connection field,
        /// and OnDestroy may fire during domain reload before any injection.
        /// </summary>
        public void EnsureAssetSubscription()
        {
            if (_subscribedConnection != null)
            {
                _subscribedConnection.OnPacketReceived -= OnPacketReceived;
                _subscribedConnection = null;
            }

            if (_connectionService == null)
            {
                throw new InvalidOperationException(
                    "ClientAssetLoader requires IConnectionService before subscription.");
            }

            // Rebind after domain reloads: the connection service may be a new
            // instance while this loader and its boolean state survived.
            _connectionService.OnPacketReceived -= OnPacketReceived;
            _connectionService.OnPacketReceived += OnPacketReceived;
            _subscribedConnection = _connectionService;
            _assetSubscriptionEstablished = true;
            _missingAssets.Clear();
        }

        private void UnsubscribeFromConnection()
        {
            // Teardown-safe: unsubscribe even if the injected subscription was
            // never bound, so a stale delegate cannot leak across reconnects.
            // OnDestroy may fire during a domain reload before VContainer
            // injection populated the field, so the injected reference must be
            // null-checked before unsubscribing (NRE at teardown otherwise).
            if (_connectionService != null)
            {
                _connectionService.OnPacketReceived -= OnPacketReceived;
            }

            if (_subscribedConnection == null)
            {
                _assetSubscriptionEstablished = false;
                return;
            }

            _subscribedConnection.OnPacketReceived -= OnPacketReceived;
            _subscribedConnection = null;
            _assetSubscriptionEstablished = false;
        }

        public UniTask<byte[]?> GetAssetBytesAsync(
            string filename,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = ProjectRuntimeContracts.AssetStreaming.AssetRequestTimeoutSeconds)
        {
            string cleanFilename = filename.TrimStart('/').ToLowerInvariant();
            if (IsAudioBank(cleanFilename) && _missingAssets.ContainsKey(cleanFilename))
            {
                return UniTask.FromResult<byte[]?>(null);
            }

            return Cache.GetBytesAsync(cleanFilename, cancellationToken, timeoutSeconds);
        }

        public async UniTask<string> GetAssetPathAsync(
            string filename,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = ProjectRuntimeContracts.AssetStreaming.AssetRequestTimeoutSeconds)
        {
            var cleanFilename = filename.TrimStart('/').ToLowerInvariant();
            if (IsAudioBank(cleanFilename) && _missingAssets.ContainsKey(cleanFilename))
            {
                throw new FileNotFoundException(
                    $"Optional audio asset '{cleanFilename}' is unavailable.",
                    cleanFilename);
            }

            byte[]? bytes = await GetAssetBytesAsync(cleanFilename, cancellationToken, timeoutSeconds);
            if (bytes == null || bytes.Length == 0 || !_persistentCache.HasAsset(cleanFilename))
            {
                if (IsAudioBank(cleanFilename))
                {
                    _missingAssets.TryAdd(cleanFilename, 0);
                }

                throw new FileNotFoundException(
                    $"Required asset '{cleanFilename}' could not be loaded or persisted.",
                    cleanFilename);
            }

            return _persistentCache.GetAssetPath(cleanFilename);
        }

        public bool IsKnownMissing(string filename)
        {
            string cleanFilename = filename.TrimStart('/').ToLowerInvariant();
            return _missingAssets.ContainsKey(cleanFilename);
        }

        public async UniTask<Texture2D?> GetTextureAsync(string filename, CancellationToken cancellationToken = default)
        {
            Texture2D? texture = await Cache.GetTextureAsync(filename, cancellationToken);
            return texture ?? throw new FileNotFoundException(
                $"Required texture '{filename}' could not be loaded.",
                filename);
        }

        public UniTask<AudioClip?> GetAudioAsync(string filename, CancellationToken cancellationToken = default)
        {
            return Cache.GetAudioAsync(filename, cancellationToken);
        }

        public UniTask<Sprite[]?> GetSpritesAsync(string filename, CancellationToken cancellationToken = default)
        {
            return Cache.GetSpritesAsync(filename, cancellationToken);
        }

        public UniTask<AnimatedSpriteData> GetAnimatedSpritesAsync(string filename, CancellationToken cancellationToken = default)
        {
            return Cache.GetAnimatedSpritesAsync(filename, cancellationToken);
        }

        public async UniTaskVoid LoadAndApplyTexture(Action<Texture2D> applyTextureAction, string filename, CancellationToken cancellationToken)
        {
            Texture2D? texture = await GetTextureAsync(filename, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (texture == null)
            {
                throw new FileNotFoundException(
                    $"Required texture '{filename}' could not be applied.",
                    filename);
            }

            applyTextureAction(texture);
        }

        public void ClearCache()
        {
            _cache?.Clear();
            _missingAssets.Clear();
            _reportedAssetFailures.Clear();
        }

        private async UniTask<byte[]?> LoadBytesFromServer(string filename, CancellationToken ct, int timeoutSeconds)
        {
            filename = filename.TrimStart('/').ToLowerInvariant();

            // 1. Check local RAM/disk cache first when offline
            var connectionService = ConnectionService;
            var isConnected = connectionService.IsConnected;

            if (!isConnected)
            {
                if (_persistentCache.HasAsset(filename))
                {
                    return await _persistentCache.GetAssetAsync(filename);
                }
            }

            // 2. Check local TextureStorageManager if available
            if (IsTextureFile(filename))
            {
                var tsm = TextureStorage;
                bool tsmHas = tsm != null && tsm.HasTexture(filename);
                if (tsmHas && tsm != null)
                {
                    var localData = await tsm.GetTextureData(filename);
                    if (localData != null && localData.Length > 0)
                    {
                        await _persistentCache.SaveAssetAsync(filename, localData, string.Empty);
                        _reportedAssetFailures.TryRemove(filename, out _);
                        return localData;
                    }
                }
            }

            // 3. Try server network request if connected
            if (isConnected)
            {
                string? etag = _persistentCache.HasAsset(filename) ? await _persistentCache.GetETagAsync(filename) : null;
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                try
                {
                    var result = await GetAssetBytesFromServer(filename, etag ?? string.Empty, cts.Token);
                    if (result != null && result.Length > 0)
                    {
                        _reportedAssetFailures.TryRemove(filename, out _);
                        return result;
                    }
                }
                catch (OperationCanceledException)
                {
                    // cancellation is expected when requests are superseded
                }
                catch (Exception ex)
                {
                    if (IsAudioBank(filename))
                    {
                        if (_reportedAssetFailures.TryAdd(filename, 0))
                        {
                            Debug.Log(
                                $"[ClientAssetLoader] Optional audio asset '{filename}' unavailable; skipping.");
                        }
                    }
                    else if (_reportedAssetFailures.TryAdd(filename, 0))
                    {
                        Debug.LogWarning($"[ClientAssetLoader] Error fetching asset {filename}: {ex.Message}");
                    }
                }
            }

            // 4. Fallback to cached asset
            if (_persistentCache.HasAsset(filename))
            {
                byte[]? cached = await _persistentCache.GetAssetAsync(filename);
                if (cached != null && cached.Length > 0)
                {
                    _reportedAssetFailures.TryRemove(filename, out _);
                }

                return cached;
            }

            if (IsTextureFile(filename))
            {
                var tsm = TextureStorage;
                if (tsm != null)
                {
                    var localData = await tsm.GetTextureData(filename);
                    if (localData != null && localData.Length > 0)
                    {
                        await _persistentCache.SaveAssetAsync(filename, localData, string.Empty);
                        _reportedAssetFailures.TryRemove(filename, out _);
                        return localData;
                    }
                }
            }

            if (IsAudioBank(filename))
            {
                _missingAssets.TryAdd(filename, 0);
            }

            return null;
        }

        private static bool IsTextureFile(string filename)
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

        private static bool IsAudioBank(string filename)
        {
            return string.Equals(
                Path.GetExtension(filename),
                ".bank",
                StringComparison.OrdinalIgnoreCase);
        }

        private async UniTask ProcessBatchLoop(CancellationToken supervisorToken)
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
                        var connectionService = ConnectionService;
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

        private async void OnPacketReceived(ServerPacket obj)
        {
            // Outer try-catch is mandatory: in an async void method, any exception
            // that escapes all catch blocks is thrown on the SynchronizationContext
            // and crashes Unity.  The inner catch propagates to the TaskCompletionSource
            // for the caller, but if TrySetException returns false (TCS already
            // completed), the exception would be unhandled without this guard.
            try
            {
                await HandleAssetPacketAsync(obj);
            }
            catch (OperationCanceledException)
            {
                // Expected during teardown or domain reload.
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private async UniTask HandleAssetPacketAsync(ServerPacket obj)
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
                _connectionService.TriggerDisconnect(
                    $"Invalid runtime asset packet: {exception.Message}");
                return;
            }

            if (!_pendingRequests.TryRemove(filename, out var tcs))
            {
                return;
            }

            try
            {
                byte[]? contents = assetPacket.Contents;

                // A conditional asset response may omit the body entirely or
                // serialize it as an empty array. Both forms mean "use the
                // cached representation" when the server supplied an ETag.
                if ((contents == null || contents.Length == 0) &&
                    !string.IsNullOrEmpty(assetPacket.ETag))
                {
                    byte[]? cachedAsset = await _persistentCache.GetAssetAsync(filename);
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
                await _persistentCache.SaveAssetAsync(filename, contents, etag);
                _missingAssets.TryRemove(filename, out _);
                tcs.TrySetResult(contents);
            }
            catch (Exception exception)
            {
                tcs.TrySetException(exception);

                // A missing optional asset is a request-scoped failure. It must
                // propagate to the caller so required assets can fail fast, but
                // it must not disconnect an otherwise healthy game session.
                // Optional callers deliberately catch this and continue without
                // their visual decoration.
            }
        }

        private async UniTask<byte[]> GetAssetBytesFromServer(string filename, string etag, CancellationToken cancellationToken)
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

            var connectionService = ConnectionService;
            if (!connectionService.IsConnected)
            {
                try
                {
                    var tsm = TextureStorage;
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
    }
}
