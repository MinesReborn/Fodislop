using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.Networking.Connection;
using Fodinae.Scripts.Networking.Connection.Client;
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

    public class ClientAssetLoader : SingletonMonoBehaviour<ClientAssetLoader>, IAssetLoader
    {
        public event Action<string, Texture2D> OnTextureLoaded;

        private readonly AssetCache _cache = new(LoadBytesFromServerInternal);

        private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _pendingRequests = new();
        private readonly ConcurrentQueue<RuntimeAssetEntryPacket> _requestQueue = new();
        private CancellationTokenSource _loopCts;

        private Texture2D _placeholderTexture;
        private Texture2D _errorTexture;

        protected override void OnAwake()
        {
            ServiceLocator.Register<IAssetLoader>(this);
            _placeholderTexture = new Texture2D(1, 1);
            _placeholderTexture.SetPixel(0, 0, Color.gray);
            _placeholderTexture.Apply();
            _placeholderTexture.name = "Placeholder_Texture";

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

        protected override void OnDestroyed()
        {
            _loopCts?.Cancel();
            _loopCts?.Dispose();
            var cm = ConnectionManager.InstanceIfExists;
            if (cm != null)
            {
                cm.OnPacketReceived -= OnPacketReceived;
            }
        }

        public UniTask<byte[]> GetAssetBytesAsync(string filename, CancellationToken cancellationToken = default, int timeoutSeconds = 5)
        {
            return _cache.GetBytesAsync(filename, cancellationToken, timeoutSeconds);
        }

        public async UniTask<string> GetAssetPathAsync(string filename, CancellationToken cancellationToken = default, int timeoutSeconds = 5)
        {
            var cleanFilename = filename.TrimStart('/').ToLowerInvariant();
            await GetAssetBytesAsync(cleanFilename, cancellationToken, timeoutSeconds);
            return GetAssetPath(cleanFilename);
        }

        public async UniTask<Texture2D> GetTextureAsync(string filename, CancellationToken cancellationToken = default)
        {
            var texture = await _cache.GetTextureAsync(filename, cancellationToken, timeoutSeconds: 5);

            if (texture != null && !cancellationToken.IsCancellationRequested)
            {
                OnTextureLoaded?.Invoke(filename, texture);
            }

            return texture;
        }

        public UniTask<AudioClip> GetAudioAsync(string filename, CancellationToken cancellationToken = default, int timeoutSeconds = 10)
        {
            return _cache.GetAudioAsync(filename, cancellationToken, timeoutSeconds);
        }

        public UniTask<Sprite[]> GetSpritesAsync(string filename, CancellationToken cancellationToken = default, int timeoutSeconds = 10)
        {
            return _cache.GetSpritesAsync(filename, cancellationToken, timeoutSeconds);
        }

        public UniTask<AnimatedSpriteData> GetAnimatedSpritesAsync(string filename, CancellationToken cancellationToken = default, int timeoutSeconds = 10)
        {
            return _cache.GetAnimatedSpritesAsync(filename, cancellationToken, timeoutSeconds);
        }

        public async UniTaskVoid LoadAndApplyTexture(Action<Texture2D> applyTextureAction, string filename, CancellationToken cancellationToken)
        {
            applyTextureAction(_placeholderTexture);

            var texture = await GetTextureAsync(filename, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

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

        public void ClearCache()
        {
            _cache.Clear();
        }

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
            filename = filename.TrimStart('/').ToLowerInvariant();

            // 1. Check local RAM/disk cache first when offline
            var cm = ConnectionManager.InstanceIfExists;
            var isConnected = cm != null && cm.Connection != null && cm.Connection.ConnectionStatus == MinesServer.Networking.Shared.ConnectionStatus.Connected;

            if (!isConnected)
            {
                if (HasAsset(filename))
                {
                    return await GetAssetAsync(filename);
                }

                var localBytes = TryLoadLocalProjectAsset(filename);
                if (localBytes != null && localBytes.Length > 0)
                {
                    return localBytes;
                }
            }

            // 2. Check local TextureStorageManager if available
            if (IsTextureFile(filename) && TextureStorageManager.Instance != null && TextureStorageManager.Instance.HasTexture(filename))
            {
                var localData = await TextureStorageManager.Instance.GetTextureData(filename);
                if (localData != null && localData.Length > 0)
                {
                    await SaveAssetAsync(filename, localData, null);
                    return localData;
                }
            }

            // 3. Try server network request if connected
            if (isConnected)
            {
                string etag = HasAsset(filename) ? await GetETagAsync(filename) : null;
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                try
                {
                    var result = await GetAssetBytesFromServer(filename, etag, cts.Token);
                    if (result != null && result.Length > 0)
                    {
                        return result;
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ClientAssetLoader] Error fetching asset {filename}: {ex.Message}");
                }
            }

            // 4. Fallback to cached asset or local project storage
            if (HasAsset(filename))
            {
                return await GetAssetAsync(filename);
            }

            var projectFallbackBytes = TryLoadLocalProjectAsset(filename);
            if (projectFallbackBytes != null && projectFallbackBytes.Length > 0)
            {
                return projectFallbackBytes;
            }

            if (IsTextureFile(filename) && TextureStorageManager.Instance != null)
            {
                var localData = await TextureStorageManager.Instance.GetTextureData(filename);
                if (localData != null && localData.Length > 0)
                {
                    await SaveAssetAsync(filename, localData, null);
                    return localData;
                }
            }

            return null;
        }

        private static byte[] TryLoadLocalProjectAsset(string filename)
        {
            try
            {
                string relativePath = filename.TrimStart('/');
                string fullPath = Path.Combine(Application.dataPath, "Textures", relativePath);
                if (File.Exists(fullPath))
                {
                    return File.ReadAllBytes(fullPath);
                }

                if (!fullPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    string pngPath = fullPath + ".png";
                    if (File.Exists(pngPath))
                    {
                        return File.ReadAllBytes(pngPath);
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool IsTextureFile(string filename)
        {
            if (string.IsNullOrEmpty(filename))
            {
                return false;
            }

            string ext = Path.GetExtension(filename).ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".webp" || ext == ".tga" || ext == ".bmp";
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

                if (_requestQueue.IsEmpty)
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

                if (batch.Count > 0)
                {
                    var cm = ConnectionManager.Instance;
                    if (cm != null && cm.Connection != null &&
                        cm.Connection.ConnectionStatus == MinesServer.Networking.Shared.ConnectionStatus.Connected)
                    {
                        var assetRequest = new RuntimeAssetRequestPacket(batch);
                        cm.Connection.SendAsync(new ClientPacket((uint)DateTimeOffset.UtcNow.Ticks, assetRequest));
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
            }
        }

        private async void OnPacketReceived(ServerPacket obj)
        {
            if (obj.Payload is RuntimeAssetPacket assetPacket)
            {
                string filename = assetPacket.Filename.TrimStart('/').ToLowerInvariant();
                if (_pendingRequests.TryRemove(filename, out var tcs))
                {
                    if (assetPacket.Contents.Length == 0 && !string.IsNullOrEmpty(assetPacket.ETag))
                    {
                        var cachedAsset = await GetAssetAsync(assetPacket.Filename).ConfigureAwait(false);
                        tcs.TrySetResult(cachedAsset);
                    }
                    else
                    {
                        var etag = Calculate(assetPacket.Contents);
                        await SaveAssetAsync(assetPacket.Filename, assetPacket.Contents, etag).ConfigureAwait(false);
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

            var cm = ConnectionManager.Instance;
            if (cm == null || cm.Connection == null ||
                cm.Connection.ConnectionStatus != MinesServer.Networking.Shared.ConnectionStatus.Connected)
            {
                try
                {
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
