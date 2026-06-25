using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Fodinae.Scripts.Networking.Connection;
using Fodinae.Scripts.World;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Utilities;
using MinesServer.Networking.Connection;
using MinesServer.Networking.Connection.Client;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Utilities;
using UnityEngine;

namespace Fodinae.Scripts
{
    using static ETagCalculator;
    using static PersistentAssetCache;

    /// <summary>
    /// Singleton MonoBehaviour for network-driven asset loading (server texture/audio/VFX streaming).
    ///
    /// This is the "local CDN":
    ///   • raw bytes (byte[]) — for consumers that need the original payload
    ///   • Texture2D — decoded from PNG, GIF atlas, or WebP
    ///   • AudioClip — decoded from WAV
    ///   • Sprite[] — animated sprite frames from GIF/WebP
    ///
    /// All formats are cached in RAM via <see cref="AssetCache"/> after the first server round-trip.
    /// Concurrent requests for the same asset coalesce into a single network call.
    /// </summary>
    public class ClientAssetLoader : MonoBehaviour
    {
        public event Action<string, Texture2D> OnTextureLoaded;

        private static ClientAssetLoader _instance;
        private static bool _isQuitting = false;

        public static ClientAssetLoader InstanceIfExists => _instance;

        public static ClientAssetLoader Instance
        {
            get
            {
                if (_isQuitting) return null;
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<ClientAssetLoader>();
                    if (_instance == null && !_isQuitting)
                    {
                        var go = new GameObject("[ClientAssetLoader]");
                        _instance = go.AddComponent<ClientAssetLoader>();

                        // System Grouping
                        if (Application.isPlaying)
                        {
                            var parent = GameObject.Find("[Systems]") ?? new GameObject("[Systems]");
                            UnityEngine.Object.DontDestroyOnLoad(parent);
                            go.transform.SetParent(parent.transform);
                        }
                    }
                }
                return _instance;
            }
        }

        // ── RAM cache ──
        private readonly AssetCache _cache = new(LoadBytesFromServerInternal);

        // ── Network request dedup & batching ──
        private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _pendingRequests = new();
        private readonly ConcurrentQueue<RuntimeAssetEntryPacket> _requestQueue = new();
        private CancellationTokenSource _loopCts;

        // ── Fallback textures ──
        private Texture2D _placeholderTexture;
        private Texture2D _errorTexture;

        void Awake()
        {
            // Singleton pattern
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);

                // Ensure parented if created in scene
                var parent = GameObject.Find("[Systems]") ?? new GameObject("[Systems]");
                UnityEngine.Object.DontDestroyOnLoad(parent);
                transform.SetParent(parent.transform);
            }

            _isQuitting = false;

            // Create a 1x1 gray placeholder texture
            _placeholderTexture = new Texture2D(1, 1);
            _placeholderTexture.SetPixel(0, 0, Color.gray);
            _placeholderTexture.Apply();
            _placeholderTexture.name = "Placeholder_Texture";

            // Create a 1x1 red error texture
            _errorTexture = new Texture2D(1, 1);
            _errorTexture.SetPixel(0, 0, Color.red);
            _errorTexture.Apply();
            _errorTexture.name = "Error_Texture";

            if (ConnectionManager.Instance != null)
            {
                ConnectionManager.Instance.OnPacketReceived += OnPacketReceived;
            }

            _loopCts = new CancellationTokenSource();
            ProcessBatchLoop(_loopCts.Token).Forget();
        }

        private void OnApplicationQuit()
        {
            _isQuitting = true;
        }

        void OnDestroy()
        {
            if (_instance == this)
            {
                _loopCts?.Cancel();
                _loopCts?.Dispose();
                var cm = ConnectionManager.InstanceIfExists;
                if (cm != null)
                {
                    cm.OnPacketReceived -= OnPacketReceived;
                }
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  Public API — delegates to _cache
        // ════════════════════════════════════════════════════════════════

        /// <summary>Retrieve raw bytes for an asset (cached in RAM after first load).</summary>
        public UniTask<byte[]> GetAssetBytesAsync(string filename, CancellationToken cancellationToken = default, int timeoutSeconds = 5)
        {
            return _cache.GetBytesAsync(filename, cancellationToken, timeoutSeconds);
        }

        /// <summary>Retrieve a decoded Texture2D (cached in RAM after first decode).</summary>
        public async UniTask<Texture2D> GetTextureAsync(string filename, CancellationToken cancellationToken = default)
        {
            var texture = await _cache.GetTextureAsync(filename, cancellationToken, timeoutSeconds: 5);

            if (texture != null && !cancellationToken.IsCancellationRequested)
            {
                OnTextureLoaded?.Invoke(filename, texture);
            }

            return texture;
        }

        /// <summary>Retrieve a decoded AudioClip from WAV bytes (cached in RAM after first decode).</summary>
        public UniTask<AudioClip> GetAudioAsync(string filename, CancellationToken cancellationToken = default, int timeoutSeconds = 10)
        {
            return _cache.GetAudioAsync(filename, cancellationToken, timeoutSeconds);
        }

        /// <summary>Retrieve animated Sprite[] from GIF/WebP (cached in RAM after first decode).</summary>
        public UniTask<Sprite[]> GetSpritesAsync(string filename, CancellationToken cancellationToken = default, int timeoutSeconds = 10)
        {
            return _cache.GetSpritesAsync(filename, cancellationToken, timeoutSeconds);
        }

        /// <summary>
        /// Retrieve animated sprites WITH source metadata (FPS, frame height).
        /// Preferred over <see cref="GetSpritesAsync"/> when you need accurate animation timing.
        /// </summary>
        public UniTask<AnimatedSpriteData> GetAnimatedSpritesAsync(string filename, CancellationToken cancellationToken = default, int timeoutSeconds = 10)
        {
            return _cache.GetAnimatedSpritesAsync(filename, cancellationToken, timeoutSeconds);
        }

        /// <summary>
        /// Load a texture and apply it via callback. Shows placeholder then swaps in the real texture.
        /// Supports cancellation via the provided CancellationToken (e.g. from GetCancellationTokenOnDestroy).
        /// </summary>
        public async UniTaskVoid LoadAndApplyTexture(Action<Texture2D> applyTextureAction, string filename, CancellationToken cancellationToken)
        {
            applyTextureAction(_placeholderTexture);

            var texture = await GetTextureAsync(filename, cancellationToken);

            if (cancellationToken.IsCancellationRequested) return;

            if (texture != null)
            {
                applyTextureAction(texture);
            }
            else
            {
                if (!HasAsset(filename))
                {
                    Debug.LogError($"Failed to load texture for '{filename}'. Applying error texture.");
                    applyTextureAction(_errorTexture);
                }
            }
        }

        /// <summary>Clear the RAM cache. Called on world reset.</summary>
        public void ClearCache()
        {
            _cache.Clear();
        }

        // ════════════════════════════════════════════════════════════════
        //  Internal: byte loading from server (used by AssetCache)
        // ════════════════════════════════════════════════════════════════

        private static async UniTask<byte[]> LoadBytesFromServerInternal(string filename, CancellationToken ct, int timeoutSeconds)
        {
            var instance = Instance;
            if (instance == null)
            {
                Debug.LogError("[ClientAssetLoader] Cannot load bytes: instance is null");
                return null;
            }

            return await instance.LoadBytesFromServer(filename, ct, timeoutSeconds);
        }

        private async UniTask<byte[]> LoadBytesFromServer(string filename, CancellationToken ct, int timeoutSeconds)
        {
            filename = filename.TrimStart('/');
            string etag = null;
            if (HasAsset(filename))
            {
                etag = GetETag(filename);
            }

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                return await GetAssetBytesFromServer(filename, etag, cts.Token);
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning($"[ClientAssetLoader] Timeout or cancelled while requesting asset: {filename}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ClientAssetLoader] Error fetching asset {filename}: {ex.Message}");
            }
            finally
            {
                cts.Dispose();
            }

            // Fallback to disk cache on network failure
            if (HasAsset(filename))
            {
                return GetAsset(filename);
            }

            return null;
        }

        // ════════════════════════════════════════════════════════════════
        //  Network layer (unchanged logic)
        // ════════════════════════════════════════════════════════════════

        private async UniTaskVoid ProcessBatchLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await UniTask.Delay(50, cancellationToken: ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (_requestQueue.IsEmpty) continue;

                List<RuntimeAssetEntryPacket> batch = new();
                while (_requestQueue.TryDequeue(out var entry))
                {
                    // Check if the request is still relevant (not cancelled or already fulfilled)
                    if (_pendingRequests.TryGetValue(entry.Filename, out var tcs) && !tcs.Task.IsCompleted)
                    {
                        // Avoid duplicates in the same batch
                        if (!batch.Exists(x => x.Filename == entry.Filename))
                        {
                            batch.Add(entry);
                        }
                    }
                }

                if (batch.Count > 0)
                {
                    var cm = ConnectionManager.Instance;
                    if (cm?.Connection != null &&
                        cm.Connection.ConnectionStatus == MinesServer.Networking.Shared.ConnectionStatus.Connected)
                    {
                        var assetRequest = new RuntimeAssetRequestPacket(batch);
                        cm.Connection.SendAsync(new ClientPacket((uint)DateTimeOffset.UtcNow.Ticks, assetRequest));
                    }
                    else
                    {
                        // Connection lost while batching, fail the batch
                        foreach (var entry in batch)
                        {
                            if (_pendingRequests.TryRemove(entry.Filename, out var tcs))
                            {
                                tcs.TrySetException(new Exception("Connection lost while sending asset request batch"));
                            }
                        }
                    }
                }
            }
        }

        private void OnPacketReceived(ServerPacket obj)
        {
            if (obj.Payload is RuntimeAssetPacket assetPacket)
            {
                string filename = assetPacket.Filename.TrimStart('/');
                if (_pendingRequests.TryRemove(filename, out var tcs))
                {
                    if (assetPacket.Contents.Length == 0 && !string.IsNullOrEmpty(assetPacket.ETag))
                    {
                        // Asset is up to date, load from cache
                        var cachedAsset = GetAsset(assetPacket.Filename);
                        tcs.TrySetResult(cachedAsset);
                    }
                    else
                    {
                        var etag = Calculate(assetPacket.Contents);
                        SaveAsset(assetPacket.Filename, assetPacket.Contents, etag);
                        tcs.TrySetResult(assetPacket.Contents);
                    }
                }
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

            // FIX: Gracefully handle offline/standalone mode!
            // If there's no connection, immediately fetch from local storage instead of crashing.
            var cm = ConnectionManager.Instance;
            if (cm == null || cm.Connection == null ||
                cm.Connection.ConnectionStatus != MinesServer.Networking.Shared.ConnectionStatus.Connected)
            {
                try
                {
                    // Directly attempt to load from local storage
                    var tsm = Fodinae.Scripts.Networking.Connection.Client.TextureStorageManager.Instance;
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

            _requestQueue.Enqueue(new RuntimeAssetEntryPacket(filename, etag ?? ""));

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
