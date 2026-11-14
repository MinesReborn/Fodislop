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

using Kern.AssetPipeline.Cache;

namespace Kern.AssetPipeline.Loading
{
    [DefaultExecutionOrder(-10000)]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Kern", sourceAssembly: null, sourceClassName: "ClientAssetLoader")]
    public class ClientAssetLoader : MonoBehaviour, IAssetLoader, IAssetSubscription
    {
        private AssetCache _cache = null!;
        private readonly AssetBatchDispatcher _dispatcher = new();
        private bool _batchLoopStarted;
        private bool _destroyed;

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

        protected void OnEnable()
        {
            Application.lowMemory += OnLowMemory;
        }

        protected void OnDisable()
        {
            Application.lowMemory -= OnLowMemory;
        }

        // Телефон шлёт предупреждение раньше, чем убивает процесс: отпускаем
        // то, что восстанавливается без сети.
        private void OnLowMemory()
        {
            _cache?.ReleaseDecodedForLowMemory();
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
                token => _dispatcher.ProcessBatchLoop(token, () => ConnectionService));
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
                _subscribedConnection.OnTransportDisconnected -= OnTransportDisconnected;
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
            _connectionService.OnTransportDisconnected -= OnTransportDisconnected;
            _connectionService.OnTransportDisconnected += OnTransportDisconnected;
            _subscribedConnection = _connectionService;
            _assetSubscriptionEstablished = true;
            _dispatcher.ClearMissing();
        }

        private void OnTransportDisconnected()
        {
            // Ждать ответа больше нечего: соединение умерло, и все
            // незавершённые запросы фейлятся сразу.
            _dispatcher.FailPendingOnDisconnect();
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
                _connectionService.OnTransportDisconnected -= OnTransportDisconnected;
            }

            if (_subscribedConnection == null)
            {
                _assetSubscriptionEstablished = false;
                return;
            }

            _subscribedConnection.OnPacketReceived -= OnPacketReceived;
            _subscribedConnection.OnTransportDisconnected -= OnTransportDisconnected;
            _subscribedConnection = null;
            _assetSubscriptionEstablished = false;
        }

        public async UniTask<byte[]?> GetAssetBytesAsync(
            string filename,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDestroyed(cancellationToken);
            // Ответы ассетов диспетчатся только подписанному загрузчику:
            // серверные окна до входа в мир грузят картинки раньше игровой
            // фазы ассетов, поэтому подписка нужна с первого запроса, а не
            // с игровой фазы. Идемпотентно; повторная установка игровой
            // фазой ничего не меняет.
            EnsureAssetSubscription();
            using CancellationTokenSource linkedCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    destroyCancellationToken);
            if (AssetBatchDispatcher.IsAudioBank(filename) && _dispatcher.IsKnownMissing(filename))
            {
                return null;
            }

            return await Cache.GetBytesAsync(
                filename,
                linkedCancellation.Token);
        }

        public async UniTask<byte[]?> GetAssetBytesAsync(
            string filename,
            string expectedETag,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDestroyed(cancellationToken);
            using CancellationTokenSource linkedCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    destroyCancellationToken);
            return await LoadBytesFromServer(
                filename,
                linkedCancellation.Token,
                expectedETag);
        }

        public async UniTask<string> GetAssetPathAsync(
            string filename,
            CancellationToken cancellationToken = default)
        {
            if (AssetBatchDispatcher.IsAudioBank(filename) && _dispatcher.IsKnownMissing(filename))
            {
                throw new FileNotFoundException(
                    $"Optional audio asset '{filename}' is unavailable.",
                    filename);
            }

            byte[]? bytes = await GetAssetBytesAsync(filename, cancellationToken);
            if (bytes == null || bytes.Length == 0 || !_persistentCache.HasAsset(filename))
            {
                if (AssetBatchDispatcher.IsAudioBank(filename))
                {
                    _dispatcher.MarkMissing(filename);
                }

                throw new FileNotFoundException(
                    $"Required asset '{filename}' could not be loaded or persisted.",
                    filename);
            }

            return _persistentCache.GetAssetPath(filename);
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
            Texture2D? texture = await Cache.GetTextureAsync(
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
            return await Cache.GetAudioAsync(filename, linkedCancellation.Token);
        }

        public async UniTask<Sprite[]?> GetSpritesAsync(string filename, CancellationToken cancellationToken = default)
        {
            ThrowIfDestroyed(cancellationToken);
            using CancellationTokenSource linkedCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    destroyCancellationToken);
            return await Cache.GetSpritesAsync(filename, linkedCancellation.Token);
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
            return await Cache.GetAnimatedSpritesAsync(filename, linkedCancellation.Token);
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
        public event Action<IReadOnlyCollection<string>>? AssetsChanged;

        public void ReplaceAssets(IReadOnlyCollection<string> filenames)
        {
            if (filenames.Count == 0)
            {
                return;
            }

            _cache?.Invalidate(filenames);
            _dispatcher.ClearMissing();
            AssetsChanged?.Invoke(filenames);
        }

        public void ClearCache()
        {
            _cache?.Clear();
            _dispatcher.ClearMissing();
            _dispatcher.ClearReportedFailures();
        }

        private UniTask<byte[]?> LoadBytesFromServer(string filename, CancellationToken ct) =>
            LoadBytesFromServer(filename, ct, expectedETag: null);

        private async UniTask<byte[]?> LoadBytesFromServer(
            string filename,
            CancellationToken ct,
            string? expectedETag)
        {
            // Ответы ассетов диспетчатся только подписанному загрузчику.
            // Серверные окна до входа в мир грузят картинки раньше игровой
            // фазы ассетов, поэтому подписка ставится на каждый запрос сети;
            // идемпотентно, игровой фазе повтор не мешает.
            EnsureAssetSubscription();

            // 1. Check local RAM/disk cache first when offline
            var connectionService = ConnectionService;
            var isConnected = connectionService.IsConnected;

            if (!isConnected)
            {
                byte[]? cached = await _persistentCache.GetAssetAsync(filename);
                if (cached is { Length: > 0 } && MatchesExpectedETag(cached, expectedETag))
                {
                    return cached;
                }
            }

            // 2. Check local TextureStorageManager if available
            if (AssetBatchDispatcher.IsTextureFile(filename) &&
                (!isConnected || string.IsNullOrEmpty(expectedETag)))
            {
                var tsm = TextureStorage;
                bool tsmHas = tsm != null && tsm.HasTexture(filename);
                if (tsmHas && tsm != null)
                {
                    var localData = await tsm.GetTextureData(filename);
                    if (localData != null && localData.Length > 0)
                    {
                        if (!_persistentCache.HasAsset(filename) ||
                            !MatchesExpectedETag(localData, expectedETag))
                        {
                            await _persistentCache.SaveAssetAsync(filename, localData);
                        }

                        _dispatcher.RemoveReportedFailure(filename);
                        return localData;
                    }
                }
            }

            // 3. Try server network request if connected
            if (isConnected)
            {
                string? etag = await _persistentCache.GetETagAsync(filename);
                try
                {
                    var result = await _dispatcher.RequestAssetBytesAsync(
                        filename,
                        etag ?? string.Empty,
                        ct,
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
            byte[]? cachedFallback = await _persistentCache.GetAssetAsync(filename);
            if (cachedFallback is { Length: > 0 } &&
                MatchesExpectedETag(cachedFallback, expectedETag))
            {
                _dispatcher.RemoveReportedFailure(filename);
                return cachedFallback;
            }

            if (AssetBatchDispatcher.IsTextureFile(filename))
            {
                var tsm = TextureStorage;
                if (tsm != null)
                {
                    var localData = await tsm.GetTextureData(filename);
                    if (localData != null && localData.Length > 0)
                    {
                        await _persistentCache.SaveAssetAsync(filename, localData);
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

        private static bool MatchesExpectedETag(byte[] contents, string? expectedETag) =>
            string.IsNullOrEmpty(expectedETag) ||
            string.Equals(
                ETagCalculator.Calculate(contents),
                expectedETag,
                StringComparison.OrdinalIgnoreCase);

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
