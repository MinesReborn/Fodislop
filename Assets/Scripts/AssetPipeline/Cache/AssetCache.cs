#nullable enable

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using UnityEngine;

namespace Fodinae
{
    /// <summary>
    /// Thread-safe RAM cache for server assets.
    /// Stores raw bytes + lazily-decoded derived formats (Texture2D, AudioClip, Sprite[]).
    /// Deduplicates concurrent in-flight requests: N callers asking for the same file
    /// share one network round-trip and one format conversion.
    ///
    /// This is the "local CDN" — assets are loaded once from the server, then served
    /// from RAM in any requested format until the application quits.
    /// </summary>
    public sealed class AssetCache
    {
        private readonly ConcurrentDictionary<string, AssetCacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, long> _entrySizes = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentQueue<string> _accessOrder = new();
        private readonly ConcurrentDictionary<string, long> _decodedEntrySizes = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentQueue<string> _decodedAccessOrder = new();
        private readonly Func<string, CancellationToken, int, UniTask<byte[]?>> _bytesLoader;

        // Asked for at call time, not stored at construction: the cache is built
        // in the loader's Awake, while the supervisor is injected by Start.
        private readonly Func<IAsyncOperationSupervisor?> _operations;
        private long _totalBytes;
        private long _maxBytes = ProjectRuntimeContracts.AssetStreaming.AssetCacheCapacityBytes;
        private long _maxDecodedBytes = ProjectRuntimeContracts.AssetStreaming.DecodedAssetCacheCapacityBytes;
        private long _totalDecodedBytes;
        private int _unloadUnusedAssetsRequested;

        // Wall clock rather than Time.unscaledTime: cache maintenance runs from
        // asset-decode continuations that are not guaranteed to be on the main
        // thread, and Unity's time API is.
        private static readonly System.Diagnostics.Stopwatch UnusedAssetsClock =
            System.Diagnostics.Stopwatch.StartNew();
        private const double MinimumSecondsBetweenUnusedAssetCollections = 30.0;
        private double _nextUnusedAssetsCollectionSeconds;

        public AssetCache(
            Func<string, CancellationToken, int, UniTask<byte[]?>> bytesLoader,
            Func<IAsyncOperationSupervisor?> operations)
        {
            _bytesLoader = bytesLoader ?? throw new ArgumentNullException(nameof(bytesLoader));
            _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        }

        /// <summary>Retrieve raw bytes. Cached and deduplicated.</summary>
        public UniTask<byte[]?> GetBytesAsync(
            string filename,
            CancellationToken ct = default,
            int timeoutSeconds = ProjectRuntimeContracts.AssetStreaming.AssetRequestTimeoutSeconds)
        {
            var entry = _entries.GetOrAdd(filename, name => new AssetCacheEntry(name, this));
            return entry.GetBytesAsync(() => _bytesLoader(filename, ct, timeoutSeconds));
        }

        /// <summary>Retrieve a decoded Texture2D. Cached after first decode.</summary>
        public UniTask<Texture2D?> GetTextureAsync(
            string filename,
            CancellationToken ct = default,
            int timeoutSeconds = ProjectRuntimeContracts.AssetStreaming.AssetRequestTimeoutSeconds)
        {
            var entry = _entries.GetOrAdd(filename, name => new AssetCacheEntry(name, this));
            return entry.GetTextureAsync(() => _bytesLoader(filename, ct, timeoutSeconds));
        }

        /// <summary>Retrieve a decoded AudioClip from WAV bytes. Cached after first decode.</summary>
        public UniTask<AudioClip?> GetAudioAsync(
            string filename,
            CancellationToken ct = default,
            int timeoutSeconds = ProjectRuntimeContracts.AssetStreaming.LargeAssetRequestTimeoutSeconds)
        {
            var entry = _entries.GetOrAdd(filename, name => new AssetCacheEntry(name, this));
            return entry.GetAudioAsync(() => _bytesLoader(filename, ct, timeoutSeconds));
        }

        /// <summary>Retrieve an animated Sprite[] from GIF/WebP. Cached after first decode.</summary>
        public UniTask<Sprite[]?> GetSpritesAsync(
            string filename,
            CancellationToken ct = default,
            int timeoutSeconds = ProjectRuntimeContracts.AssetStreaming.LargeAssetRequestTimeoutSeconds)
        {
            var entry = _entries.GetOrAdd(filename, name => new AssetCacheEntry(name, this));
            return entry.GetSpritesAsync(() => _bytesLoader(filename, ct, timeoutSeconds));
        }

        /// <summary>
        /// Retrieve animated sprites WITH metadata (FPS, frame height).
        /// Use this when you need accurate animation timing from the source file.
        /// </summary>
        public UniTask<AnimatedSpriteData> GetAnimatedSpritesAsync(
            string filename,
            CancellationToken ct = default,
            int timeoutSeconds = ProjectRuntimeContracts.AssetStreaming.LargeAssetRequestTimeoutSeconds)
        {
            var entry = _entries.GetOrAdd(filename, name => new AssetCacheEntry(name, this));
            return entry.GetAnimatedSpritesAsync(() => _bytesLoader(filename, ct, timeoutSeconds));
        }

        /// <summary>Remove a specific entry from the cache (e.g. on world reset).</summary>
        public void Evict(string filename)
        {
            if (!_entries.TryRemove(filename, out var entry))
            {
                return;
            }

            entry.ReleaseAllReferences();
            RemoveTrackedSize(filename);
            if (_decodedEntrySizes.TryRemove(filename, out var decodedSize))
            {
                Interlocked.Add(ref _totalDecodedBytes, -decodedSize);
            }

            RebuildAccessOrder();
        }

        /// <summary>Clear all cached entries.</summary>
        public void Clear()
        {
            foreach (var entry in _entries.Values.ToArray())
            {
                entry.ReleaseAllReferences();
            }

            _entries.Clear();
            _entrySizes.Clear();
            _decodedEntrySizes.Clear();
            while (_accessOrder.TryDequeue(out _))
            {
            }

            while (_decodedAccessOrder.TryDequeue(out _))
            {
            }

            Interlocked.Exchange(ref _totalBytes, 0);
            Interlocked.Exchange(ref _totalDecodedBytes, 0);

            RequestUnusedAssetsCollection(force: true);
        }

        /// <summary>Set the maximum cache size in bytes. Default is 256 MB.</summary>
        public void SetMaxSize(long maxBytes)
        {
            _maxBytes = maxBytes;
            EvictIfNeeded();
        }

        public void SetMaxDecodedSize(long maxBytes)
        {
            _maxDecodedBytes = Math.Max(0, maxBytes);
            TrimDecodedIfNeeded();
        }

        internal void TrackAccess(string filename, long byteSize)
        {
            if (_entrySizes.TryAdd(filename, byteSize))
            {
                _accessOrder.Enqueue(filename);
                Interlocked.Add(ref _totalBytes, byteSize);
            }

            EvictIfNeeded();
        }

        internal void TrackDecoded(string filename, long decodedSize)
        {
            if (decodedSize <= 0)
            {
                return;
            }

            if (_decodedEntrySizes.TryAdd(filename, decodedSize))
            {
                _decodedAccessOrder.Enqueue(filename);
                Interlocked.Add(ref _totalDecodedBytes, decodedSize);
            }

            TrimDecodedIfNeeded();
        }

        private void TrimDecodedIfNeeded()
        {
            bool trimmed = false;
            while (Interlocked.Read(ref _totalDecodedBytes) > _maxDecodedBytes &&
                   _decodedAccessOrder.TryDequeue(out var oldest))
            {
                if (!_decodedEntrySizes.TryRemove(oldest, out var size))
                {
                    continue;
                }

                if (_entries.TryGetValue(oldest, out var entry))
                {
                    entry.ReleaseDecodedReference();
                }

                Interlocked.Add(ref _totalDecodedBytes, -size);
                trimmed = true;
            }

            if (trimmed)
            {
                RequestUnusedAssetsCollection();
            }
        }

        private void RequestUnusedAssetsCollection(bool force = false)
        {
            double now = UnusedAssetsClock.Elapsed.TotalSeconds;
            if (!force && now < _nextUnusedAssetsCollectionSeconds)
            {
                return;
            }

            if (Interlocked.Exchange(ref _unloadUnusedAssetsRequested, 1) != 0)
            {
                return;
            }

            _nextUnusedAssetsCollectionSeconds =
                now + MinimumSecondsBetweenUnusedAssetCollections;

            // The explicit UniTask wrap lets GC roots settle and gives the
            // engine a deterministic frame boundary to drop unused objects,
            // instead of the implicit completion of AsyncOperation.completed,
            // which runs on the main thread outside any frame boundary. The
            // supervisor owns that wait: an unsupervised one would keep calling
            // into the engine after teardown has begun.
            IAsyncOperationSupervisor? operations = _operations();
            if (operations == null)
            {
                // Before the supervisor is injected there is nobody to own the
                // wait. Collection is an optimisation, so the request is dropped
                // rather than run unattended; the flag is cleared so the next
                // trim can ask again.
                Interlocked.Exchange(ref _unloadUnusedAssetsRequested, 0);
                return;
            }

            operations.Run("asset_cache_unload_unused", RunUnusedAssetsCollectionAsync);
        }

        private async UniTask RunUnusedAssetsCollectionAsync(CancellationToken cancellationToken)
        {
            try
            {
                var op = Resources.UnloadUnusedAssets();
                await op.ToUniTask(cancellationToken: cancellationToken);
                await UniTask.Yield(cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AssetCache] UnloadUnusedAssets failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _unloadUnusedAssetsRequested, 0);
            }
        }

        internal void RemoveTrackedSize(string filename)
        {
            if (_entrySizes.TryRemove(filename, out var size))
            {
                Interlocked.Add(ref _totalBytes, -size);
            }
        }

        private void RebuildAccessOrder()
        {
            while (_accessOrder.TryDequeue(out _))
            {
            }

            foreach (var filename in _entrySizes.Keys)
            {
                _accessOrder.Enqueue(filename);
            }
        }

        private void EvictIfNeeded()
        {
            while (Interlocked.Read(ref _totalBytes) > _maxBytes && _accessOrder.Count > 0)
            {
                if (!_accessOrder.TryDequeue(out var oldest))
                {
                    break;
                }

                if (_entries.TryGetValue(oldest, out var entry))
                {
                    entry.ReleaseRawBytes();
                }
                else
                {
                    RemoveTrackedSize(oldest);
                }
            }
        }
    }
}
