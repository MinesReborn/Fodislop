#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets;
using UnityEngine;
using VContainer;

namespace Kern
{
    [DefaultExecutionOrder(-10000)]
    public class ClientAssetLoader : MonoBehaviour, IAssetLoader, IAssetSubscription
    {
        private AssetCache _cache = null!;
        private readonly AssetBatchDispatcher _dispatcher = new();
        private bool _batchLoopStarted;
        private bool _destroyed;

        private AssetCache _Cache => _cache ??
            throw new ObjectDisposedException(nameof(ClientAssetLoader));

        public int PendingAssetCount => _dispatcher.PendingCount;

        public int QueuedAssetCount => _dispatcher.QueuedCount;

        public string[] GetPendingAssetNames() => _dispatcher.GetPendingAssetNames();

        [Inject]
        private IConnectionService _connectionService = null!;
        [Inject]
        private ITextureStorageService _textureStorage = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;
        [Inject]
        private IPersistentAssetCache _persistentCache = null!;

        private IConnectionService _ConnectionService =>
            _connectionService ??
            throw new InvalidOperationException(
                "ClientAssetLoader requires IConnectionService before loading assets.");

        private ITextureStorageService _TextureStorage => _textureStorage;

        private bool _assetSubscriptionEstablished;
        private IConnectionService? _subscribedConnection;

        public bool IsAssetSubscriptionEstablished => _assetSubscriptionEstablished;

        protected void Awake()
        {
            _cache = new AssetCache(LoadBytesFromServer, () => _operations);
        }

        protected void Start()
        {
            TryStartBatchLoop();
        }

        protected void Update()
        {
            // Authored Bootstrap components can receive VContainer injection
            // after Unity invokes Start because this component has an early
            // execution order. Retry only the one-time startup until the
            // dependency is available; asset requests remain unavailable until
            // the supervised loop has actually started.
            TryStartBatchLoop();
        }

        private void TryStartBatchLoop()
        {
            if (_batchLoopStarted || _operations == null)
            {
                return;
            }

            _batchLoopStarted = true;
            _operations.Run(
                "asset_request_batch_loop",
                token => _dispatcher.ProcessBatchLoop(token, () => _ConnectionService));
        }

        protected void OnDestroy()
        {
            _destroyed = true;
            _dispatcher.Dispose();
            if (_cache != null)
            {
                _cache.Clear(collectUnusedAssets: false);
                _cache = null!;
            }

            UnsubscribeFromConnection();
        }

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
            _dispatcher.ClearMissing();
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

        public async UniTask<byte[]?> GetAssetBytesAsync(
            string filename,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = ProjectRuntimeContracts.AssetStreaming.AssetRequestTimeoutSeconds)
        {
            ThrowIfDestroyed(cancellationToken);
            using CancellationTokenSource linkedCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    destroyCancellationToken);
            string cleanFilename = filename.TrimStart('/').ToLowerInvariant();
            if (AssetBatchDispatcher.IsAudioBank(cleanFilename) && _dispatcher.IsKnownMissing(cleanFilename))
            {
                return null;
            }

            return await _Cache.GetBytesAsync(
                cleanFilename,
                linkedCancellation.Token,
                timeoutSeconds);
        }

        public async UniTask<string> GetAssetPathAsync(
            string filename,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = ProjectRuntimeContracts.AssetStreaming.AssetRequestTimeoutSeconds)
        {
            var cleanFilename = filename.TrimStart('/').ToLowerInvariant();
            if (AssetBatchDispatcher.IsAudioBank(cleanFilename) && _dispatcher.IsKnownMissing(cleanFilename))
            {
                throw new FileNotFoundException(
                    $"Optional audio asset '{cleanFilename}' is unavailable.",
                    cleanFilename);
            }

            byte[]? bytes = await GetAssetBytesAsync(cleanFilename, cancellationToken, timeoutSeconds);
            if (bytes == null || bytes.Length == 0 || !_persistentCache.HasAsset(cleanFilename))
            {
                if (AssetBatchDispatcher.IsAudioBank(cleanFilename))
                {
                    _dispatcher.MarkMissing(cleanFilename);
                }

                throw new FileNotFoundException(
                    $"Required asset '{cleanFilename}' could not be loaded or persisted.",
                    cleanFilename);
            }

            return _persistentCache.GetAssetPath(cleanFilename);
        }

        public bool IsKnownMissing(string filename) =>
            _dispatcher.IsKnownMissing(filename);

        public async UniTask<Texture2D?> GetTextureAsync(string filename, CancellationToken cancellationToken = default)
        {
            ThrowIfDestroyed(cancellationToken);
            using CancellationTokenSource linkedCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    destroyCancellationToken);
            Texture2D? texture = await _Cache.GetTextureAsync(
                filename,
                linkedCancellation.Token);
            return texture ?? throw new FileNotFoundException(
                $"Required texture '{filename}' could not be loaded.",
                filename);
        }

        public async UniTask<AudioClip?> GetAudioAsync(string filename, CancellationToken cancellationToken = default)
        {
            ThrowIfDestroyed(cancellationToken);
            using CancellationTokenSource linkedCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    destroyCancellationToken);
            return await _Cache.GetAudioAsync(filename, linkedCancellation.Token);
        }

        public async UniTask<Sprite[]?> GetSpritesAsync(string filename, CancellationToken cancellationToken = default)
        {
            ThrowIfDestroyed(cancellationToken);
            using CancellationTokenSource linkedCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    destroyCancellationToken);
            return await _Cache.GetSpritesAsync(filename, linkedCancellation.Token);
        }

        public async UniTask<AnimatedSpriteData> GetAnimatedSpritesAsync(
            string filename,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDestroyed(cancellationToken);
            using CancellationTokenSource linkedCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    destroyCancellationToken);
            return await _Cache.GetAnimatedSpritesAsync(filename, linkedCancellation.Token);
        }

        private void ThrowIfDestroyed(CancellationToken cancellationToken)
        {
            if (!_destroyed)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new OperationCanceledException(
                "ClientAssetLoader was destroyed while an asset request was active.",
                cancellationToken);
        }
        public void ClearCache()
        {
            _cache?.Clear();
            _dispatcher.ClearMissing();
            _dispatcher.ClearReportedFailures();
        }

        private async UniTask<byte[]?> LoadBytesFromServer(string filename, CancellationToken ct, int timeoutSeconds)
        {
            filename = filename.TrimStart('/').ToLowerInvariant();

            // 1. Check local RAM/disk cache first when offline
            var connectionService = _ConnectionService;
            var isConnected = connectionService.IsConnected;

            if (!isConnected)
            {
                byte[]? cached = await _persistentCache.GetAssetAsync(filename);
                if (cached != null && cached.Length > 0)
                {
                    return cached;
                }
            }

            // 2. Check local TextureStorageManager if available
            if (AssetBatchDispatcher.IsTextureFile(filename))
            {
                var tsm = _TextureStorage;
                bool tsmHas = tsm != null && tsm.HasTexture(filename);
                if (tsmHas && tsm != null)
                {
                    var localData = await tsm.GetTextureData(filename);
                    if (localData != null && localData.Length > 0)
                    {
                        await _persistentCache.SaveAssetAsync(filename, localData, string.Empty);
                        _dispatcher.RemoveReportedFailure(filename);
                        return localData;
                    }
                }
            }

            // 3. Try server network request if connected
            if (isConnected)
            {
                string? etag = await _persistentCache.GetETagAsync(filename);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                try
                {
                    var result = await _dispatcher.RequestAssetBytesAsync(
                        filename,
                        etag ?? string.Empty,
                        cts.Token,
                        () => _ConnectionService,
                        () => _TextureStorage);

                    if (result != null && result.Length > 0)
                    {
                        _dispatcher.RemoveReportedFailure(filename);
                        return result;
                    }
                }
                catch (OperationCanceledException)
                {
                    // cancellation is expected when requests are superseded
                }
                catch (Exception ex)
                {
                    if (AssetBatchDispatcher.IsAudioBank(filename))
                    {
                        if (_dispatcher.TryReportFailure(filename))
                        {
                            Debug.Log(
                                $"[ClientAssetLoader] Optional audio asset '{filename}' unavailable; skipping.");
                        }
                    }
                    else if (_dispatcher.TryReportFailure(filename))
                    {
                        Debug.LogWarning($"[ClientAssetLoader] Error fetching asset {filename}: {ex.Message}");
                    }
                }
            }

            // 4. Fallback to cached asset
            byte[]? cachedFallback = await _persistentCache.GetAssetAsync(filename);
            if (cachedFallback != null && cachedFallback.Length > 0)
            {
                _dispatcher.RemoveReportedFailure(filename);
                return cachedFallback;
            }

            if (AssetBatchDispatcher.IsTextureFile(filename))
            {
                var tsm = _TextureStorage;
                if (tsm != null)
                {
                    var localData = await tsm.GetTextureData(filename);
                    if (localData != null && localData.Length > 0)
                    {
                        await _persistentCache.SaveAssetAsync(filename, localData, string.Empty);
                        _dispatcher.RemoveReportedFailure(filename);
                        return localData;
                    }
                }
            }

            if (AssetBatchDispatcher.IsAudioBank(filename))
            {
                _dispatcher.MarkMissing(filename);
            }

            return null;
        }

        private void OnPacketReceived(ServerPacket obj)
        {
            if (_destroyed)
            {
                return;
            }

            try
            {
                _operations.Run(
                    "asset_packet_processing",
                    cancellationToken => HandleAssetPacketAsync(obj, cancellationToken));
            }
            catch (ObjectDisposedException)
            {
                // The supervisor can finish disposing between event dispatch and
                // this callback during scene teardown.
            }
        }

        private async UniTask HandleAssetPacketAsync(
            ServerPacket packet,
            CancellationToken supervisorToken)
        {
            using CancellationTokenSource linkedCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    supervisorToken,
                    destroyCancellationToken);

            await _dispatcher.HandleAssetPacketAsync(
                packet,
                _persistentCache,
                message => _connectionService.TriggerDisconnect(message),
                linkedCancellation.Token);
        }
    }
}
