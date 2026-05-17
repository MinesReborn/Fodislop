using Cysharp.Threading.Tasks;
using UnityEngine;
using System;
using System.Collections.Concurrent;
using System.Threading;
using MinesServer.Networking.Connection;
using MinesServer.Networking.Connection.Client;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Utilities;
using System.Threading.Tasks;
using System.Collections.Generic;
using static PersistentAssetCache;
using static ETagCalculator;
using MinesServer.Networking.Server.Packets.Utilities;
using MinesServer.Networking.Server.Packets;
using Fodinae.Assets.Scripts.Networking.Connection;

namespace Fodinae.Assets.Scripts
{
    public class ClientAssetLoader : MonoBehaviour
    {
        public event Action<string, Texture2D> OnTextureLoaded;

        private static ClientAssetLoader _instance;
        public static ClientAssetLoader Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<ClientAssetLoader>();
                    if (_instance == null)
                    {
                        var go = new GameObject("[ClientAssetLoader]");
                        _instance = go.AddComponent<ClientAssetLoader>();
                    }
                }
                return _instance;
            }
        }

        private ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _pendingRequests = new();
        private readonly ConcurrentQueue<RuntimeAssetEntryPacket> _requestQueue = new();
        private CancellationTokenSource _loopCts;

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
            DontDestroyOnLoad(gameObject);

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

            ConnectionManager.Instance.OnPacketReceived += OnPacketReceived;

            _loopCts = new CancellationTokenSource();
            ProcessBatchLoop(_loopCts.Token).Forget();
        }

        void OnDestroy()
        {
            if (_instance == this)
            {
                _loopCts?.Cancel();
                _loopCts?.Dispose();
                ConnectionManager.Instance.OnPacketReceived -= OnPacketReceived;
            }
        }

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
                    if (ConnectionManager.Instance?.Connection != null &&
                        ConnectionManager.Instance.Connection.ConnectionStatus == MinesServer.Networking.Shared.ConnectionStatus.Connected)
                    {
                        var assetRequest = new RuntimeAssetRequestPacket(batch);
                        ConnectionManager.Instance.Connection.SendAsync(new ClientPacket((uint)DateTimeOffset.UtcNow.Ticks, assetRequest));
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

        public async UniTask<Texture2D> GetTextureAsync(string filename, CancellationToken cancellationToken = default)
        {
            filename = filename.TrimStart('/');
            string etag = null;
            if (HasAsset(filename))
            {
                etag = GetETag(filename);
            }

            // Timeout after 5 seconds to prevent hanging
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            byte[] imageBytes = null;
            try
            {
                imageBytes = await GetAssetBytesFromServer(filename, etag, cts.Token);
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning($"[ClientAssetLoader] Timeout or cancelled while requesting texture: {filename}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ClientAssetLoader] Error fetching asset {filename}: {ex.Message}");
            }

            if (cancellationToken.IsCancellationRequested) return null;

            if (imageBytes != null && imageBytes.Length > 0)
            {
                var loadedTexture = await LoadTextureAsync(imageBytes, cancellationToken);
                if (loadedTexture != null)
                {
                    OnTextureLoaded?.Invoke(filename, loadedTexture);
                    return loadedTexture;
                }
            }
            else if (HasAsset(filename))
            {
                // Fallback to cache if network failed or server returned Not Modified
                var cachedAsset = GetAsset(filename);
                if (cachedAsset != null)
                {
                    var loadedTexture = await LoadTextureAsync(cachedAsset, cancellationToken);
                    if (loadedTexture != null)
                    {
                        return loadedTexture;
                    }
                }
            }

            return null;
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
            // If there's no connection, immediately fetch from local Texture Storage Manager instead of crashing.
            if (ConnectionManager.Instance == null || ConnectionManager.Instance.Connection == null ||
                ConnectionManager.Instance.Connection.ConnectionStatus != MinesServer.Networking.Shared.ConnectionStatus.Connected)
            {
                try
                {
                    // Directly attempt to load from local storage
                    var localData = await Fodinae.Assets.Scripts.Networking.Connection.Client.TextureStorageManager.Instance.GetTextureData(filename);
                    if (localData != null)
                    {
                        tcs.TrySetResult(localData);
                        _pendingRequests.TryRemove(filename, out _);
                        return localData;
                    }
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                    _pendingRequests.TryRemove(filename, out _);
                    throw;
                }

                var noConnEx = new Exception($"No active connection and no local texture found for {filename}");
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

        private async UniTask<Texture2D> LoadTextureAsync(byte[] imageData, CancellationToken cancellationToken)
        {
            await UniTask.SwitchToMainThread(cancellationToken);

            var type = Fodinae.Assets.Scripts.World.AnimationContainerDecoder.DetectType(imageData);
            if (type == Fodinae.Assets.Scripts.World.AnimationContainerDecoder.ContainerType.GIF ||
                type == Fodinae.Assets.Scripts.World.AnimationContainerDecoder.ContainerType.WebP)
            {
                var decoded = type == Fodinae.Assets.Scripts.World.AnimationContainerDecoder.ContainerType.GIF
                    ? Fodinae.Assets.Scripts.World.AnimationContainerDecoder.DecodeGif(imageData)
                    : Fodinae.Assets.Scripts.World.AnimationContainerDecoder.DecodeWebP(imageData);

                if (decoded.Atlas != null)
                {
                    decoded.Atlas.name = $"RuntimeAnim_{DateTime.Now.Ticks}|FPS={decoded.FPS}|FrameHeight={decoded.FrameHeight}";
                    decoded.Atlas.filterMode = FilterMode.Point;
                    return decoded.Atlas;
                }
            }

            // Fallback to standard Unity loading for PNG or if decoding failed
            Texture2D texture = new Texture2D(2, 2);
            if (texture.LoadImage(imageData))
            {
                texture.name = $"Runtime_{DateTime.Now.Ticks}";
                texture.filterMode = FilterMode.Point;
                return texture;
            }
            Destroy(texture);
            return null;
        }
    }
}
