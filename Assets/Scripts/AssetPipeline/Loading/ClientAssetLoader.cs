#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using MinesServer.Networking.Server.Packets;
using UnityEngine;
using VContainer;

namespace Fodinae
{
    [DefaultExecutionOrder(-10000)]
    public class ClientAssetLoader : MonoBehaviour, IAssetLoader, IAssetSubscription
    {
        private AssetCache _cache = null!;
        private readonly AssetBatchDispatcher _dispatcher = new();
        private bool _batchLoopStarted;

        private AssetCache Cache => _cache ??
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
            _cache = new AssetCache(LoadBytesFromServer, () => _operations);
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
            _operations.Run(
                "asset_request_batch_loop",
                token => _dispatcher.ProcessBatchLoop(token, () => ConnectionService));
        }

        protected void OnDestroy()
        {
            _dispatcher.Dispose();
            if (_cache != null)
            {
                _cache.Clear(collectUnusedAssets: false);
                _cache = null!;
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

        public UniTask<byte[]?> GetAssetBytesAsync(
            string filename,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = ProjectRuntimeContracts.AssetStreaming.AssetRequestTimeoutSeconds)
        {
            string cleanFilename = filename.TrimStart('/').ToLowerInvariant();
            if (AssetBatchDispatcher.IsAudioBank(cleanFilename) && _dispatcher.IsKnownMissing(cleanFilename))
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
            _dispatcher.ClearMissing();
            _dispatcher.ClearReportedFailures();
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
            if (AssetBatchDispatcher.IsTextureFile(filename))
            {
                var tsm = TextureStorage;
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
                string? etag = _persistentCache.HasAsset(filename) ? await _persistentCache.GetETagAsync(filename) : null;
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                try
                {
                    var result = await _dispatcher.RequestAssetBytesAsync(
                        filename,
                        etag ?? string.Empty,
                        cts.Token,
                        () => ConnectionService,
                        () => TextureStorage);

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
            if (_persistentCache.HasAsset(filename))
            {
                byte[]? cached = await _persistentCache.GetAssetAsync(filename);
                if (cached != null && cached.Length > 0)
                {
                    _dispatcher.RemoveReportedFailure(filename);
                }

                return cached;
            }

            if (AssetBatchDispatcher.IsTextureFile(filename))
            {
                var tsm = TextureStorage;
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

        private async void OnPacketReceived(ServerPacket obj)
        {
            // Outer try-catch is mandatory: in an async void method, any exception
            // that escapes all catch blocks is thrown on the SynchronizationContext
            // and crashes Unity.
            try
            {
                await _dispatcher.HandleAssetPacketAsync(
                    obj,
                    _persistentCache,
                    msg => _connectionService.TriggerDisconnect(msg));
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
    }
}
