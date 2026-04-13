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
            var tcs = new TaskCompletionSource<byte[]>();
            using var registration = cancellationToken.Register(() => {
                tcs.TrySetCanceled();
                _pendingRequests.TryRemove(filename, out _);
            });

            if (!_pendingRequests.TryAdd(filename, tcs))
            {
                if (_pendingRequests.TryGetValue(filename, out var existingTcs))
                {
                    return await existingTcs.Task;
                }
                return null;
            }

            // FIX: Gracefully handle offline/standalone mode!
            // If there's no connection, immediately fetch from local Texture Storage Manager instead of crashing.
            if (ConnectionManager.Instance == null || ConnectionManager.Instance.Connection == null ||
                ConnectionManager.Instance.Connection.ConnectionStatus != MinesServer.Networking.Shared.ConnectionStatus.Connected)
            {
                _pendingRequests.TryRemove(filename, out _);

                // Directly attempt to load from local storage
                var localData = await Fodinae.Assets.Scripts.Networking.Connection.Client.TextureStorageManager.Instance.GetTextureData(filename);
                if (localData != null)
                {
                    return localData;
                }

                throw new Exception($"No active connection and no local texture found for {filename}");
            }

            var assetEntry = new RuntimeAssetEntryPacket(filename, etag ?? "");
            var assetRequest = new RuntimeAssetRequestPacket(new List<RuntimeAssetEntryPacket> { assetEntry });
            ConnectionManager.Instance.Connection.SendAsync(new ClientPacket((uint)DateTimeOffset.UtcNow.Ticks, assetRequest));

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