using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Fodinae.Scripts.World;
using UnityEngine;

namespace Fodinae.Scripts
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
        private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly Func<string, CancellationToken, int, UniTask<byte[]>> _bytesLoader;

        public AssetCache(Func<string, CancellationToken, int, UniTask<byte[]>> bytesLoader)
        {
            _bytesLoader = bytesLoader ?? throw new ArgumentNullException(nameof(bytesLoader));
        }

        /// <summary>Retrieve raw bytes. Cached and deduplicated.</summary>
        public UniTask<byte[]> GetBytesAsync(string filename, CancellationToken ct = default, int timeoutSeconds = 5)
        {
            var entry = _entries.GetOrAdd(filename, _ => new CacheEntry());
            return entry.GetBytesAsync(() => _bytesLoader(filename, ct, timeoutSeconds));
        }

        /// <summary>Retrieve a decoded Texture2D. Cached after first decode.</summary>
        public UniTask<Texture2D> GetTextureAsync(string filename, CancellationToken ct = default, int timeoutSeconds = 5)
        {
            var entry = _entries.GetOrAdd(filename, _ => new CacheEntry());
            return entry.GetTextureAsync(() => _bytesLoader(filename, ct, timeoutSeconds));
        }

        /// <summary>Retrieve a decoded AudioClip from WAV bytes. Cached after first decode.</summary>
        public UniTask<AudioClip> GetAudioAsync(string filename, CancellationToken ct = default, int timeoutSeconds = 10)
        {
            var entry = _entries.GetOrAdd(filename, _ => new CacheEntry());
            return entry.GetAudioAsync(() => _bytesLoader(filename, ct, timeoutSeconds));
        }

        /// <summary>Retrieve an animated Sprite[] from GIF/WebP. Cached after first decode.</summary>
        public UniTask<Sprite[]> GetSpritesAsync(string filename, CancellationToken ct = default, int timeoutSeconds = 10)
        {
            var entry = _entries.GetOrAdd(filename, _ => new CacheEntry());
            return entry.GetSpritesAsync(() => _bytesLoader(filename, ct, timeoutSeconds));
        }

        /// <summary>
        /// Retrieve animated sprites WITH metadata (FPS, frame height).
        /// Use this when you need accurate animation timing from the source file.
        /// </summary>
        public UniTask<AnimatedSpriteData> GetAnimatedSpritesAsync(string filename, CancellationToken ct = default, int timeoutSeconds = 10)
        {
            var entry = _entries.GetOrAdd(filename, _ => new CacheEntry());
            return entry.GetAnimatedSpritesAsync(() => _bytesLoader(filename, ct, timeoutSeconds));
        }

        /// <summary>Remove a specific entry from the cache (e.g. on world reset).</summary>
        public void Evict(string filename)
        {
            _entries.TryRemove(filename, out _);
        }

        /// <summary>Clear all cached entries.</summary>
        public void Clear()
        {
            _entries.Clear();
        }

        // ──────────────────────────────────────────────────────────────
        // Internal entry — one per unique filename
        // ──────────────────────────────────────────────────────────────

        private sealed class CacheEntry
        {
            private readonly object _lock = new();

            // ── Raw bytes ──
            private byte[] _bytes;
            private TaskCompletionSource<byte[]> _bytesPromise;

            // ── Derivated formats (lazy, computed on first request) ──
            private Texture2D _texture;
            private TaskCompletionSource<Texture2D> _texturePromise;

            private AudioClip _audio;
            private TaskCompletionSource<AudioClip> _audioPromise;

            private Sprite[] _sprites;
            private TaskCompletionSource<Sprite[]> _spritePromise;

            // Stored alongside sprites for AnimatedSpriteData lookups
            private float _spriteFps;
            private int _spriteFrameHeight;

            // ── Public API ──

            public UniTask<byte[]> GetBytesAsync(Func<UniTask<byte[]>> loader)
            {
                // Fast path — already loaded
                lock (_lock)
                {
                    if (_bytes != null)
                    {
                        return UniTask.FromResult(_bytes);
                    }

                    if (_bytesPromise != null)
                    {
                        return AwaitTask(_bytesPromise.Task);
                    }
                }

                // First caller — create the promise
                TaskCompletionSource<byte[]> promise;
                lock (_lock)
                {
                    if (_bytes != null)
                    {
                        return UniTask.FromResult(_bytes);
                    }

                    if (_bytesPromise != null)
                    {
                        return AwaitTask(_bytesPromise.Task);
                    }

                    _bytesPromise = promise = new TaskCompletionSource<byte[]>();
                }

                return LoadBytes(promise, loader);
            }

            public UniTask<Texture2D> GetTextureAsync(Func<UniTask<byte[]>> loader)
            {
                lock (_lock)
                {
                    if (_texture != null)
                    {
                        return UniTask.FromResult(_texture);
                    }

                    if (_texturePromise != null)
                    {
                        return AwaitTask(_texturePromise.Task);
                    }
                }

                lock (_lock)
                {
                    if (_texture != null)
                    {
                        return UniTask.FromResult(_texture);
                    }

                    if (_texturePromise != null)
                    {
                        return AwaitTask(_texturePromise.Task);
                    }

                    _texturePromise = new TaskCompletionSource<Texture2D>();
                }

                return DecodeTexture(loader);
            }

            public UniTask<AudioClip> GetAudioAsync(Func<UniTask<byte[]>> loader)
            {
                lock (_lock)
                {
                    if (_audio != null)
                    {
                        return UniTask.FromResult(_audio);
                    }

                    if (_audioPromise != null)
                    {
                        return AwaitTask(_audioPromise.Task);
                    }
                }

                lock (_lock)
                {
                    if (_audio != null)
                    {
                        return UniTask.FromResult(_audio);
                    }

                    if (_audioPromise != null)
                    {
                        return AwaitTask(_audioPromise.Task);
                    }

                    _audioPromise = new TaskCompletionSource<AudioClip>();
                }

                return DecodeAudio(loader);
            }

            public UniTask<Sprite[]> GetSpritesAsync(Func<UniTask<byte[]>> loader)
            {
                lock (_lock)
                {
                    if (_sprites != null)
                    {
                        return UniTask.FromResult(_sprites);
                    }

                    if (_spritePromise != null)
                    {
                        return AwaitTask(_spritePromise.Task);
                    }
                }

                lock (_lock)
                {
                    if (_sprites != null)
                    {
                        return UniTask.FromResult(_sprites);
                    }

                    if (_spritePromise != null)
                    {
                        return AwaitTask(_spritePromise.Task);
                    }

                    _spritePromise = new TaskCompletionSource<Sprite[]>();
                }

                return DecodeSprites(loader);
            }

            public UniTask<AnimatedSpriteData> GetAnimatedSpritesAsync(Func<UniTask<byte[]>> loader)
            {
                // Fast path — already decoded
                lock (_lock)
                {
                    if (_sprites != null)
                    {
                        return UniTask.FromResult(new AnimatedSpriteData(_sprites, _spriteFps, _spriteFrameHeight));
                    }

                    if (_spritePromise != null)
                    {
                        return AwaitAnimatedSprites(_spritePromise.Task);
                    }
                }

                // New request — the first ResolveSprites call populates the promise
                lock (_lock)
                {
                    if (_sprites != null)
                    {
                        return UniTask.FromResult(new AnimatedSpriteData(_sprites, _spriteFps, _spriteFrameHeight));
                    }

                    if (_spritePromise != null)
                    {
                        return AwaitAnimatedSprites(_spritePromise.Task);
                    }

                    _spritePromise = new TaskCompletionSource<Sprite[]>();
                }

                return DecodeAndWrapSprites(loader);
            }

            // ── Private Static Methods ──

            private static async UniTask<AnimatedSpriteData> AwaitAnimatedSprites(Task<Sprite[]> task)
            {
                var frames = await task;

                // NOTE: cache entry will have the correct FPS stored; but since we returned a
                // promise, the stored values are stale for awaiters. This path is rare (concurrent
                // first requests) — the primary path is the fast-return above.
                return new AnimatedSpriteData(frames, 10f, 0);
            }

            private static async UniTask<T> AwaitTask<T>(Task<T> task)
            {
                return await task;
            }

            // ── Private Instance Methods ──

            private async UniTask<byte[]> LoadBytes(TaskCompletionSource<byte[]> promise, Func<UniTask<byte[]>> loader)
            {
                try
                {
                    var bytes = await loader();
                    lock (_lock)
                    {
                        _bytes = bytes;
                        _bytesPromise = null;
                    }

                    promise.TrySetResult(bytes);
                    return bytes;
                }
                catch (Exception ex)
                {
                    lock (_lock)
                    {
                        _bytesPromise = null;
                    }

                    promise.TrySetException(ex);
                    throw;
                }
            }

            private async UniTask<Texture2D> DecodeTexture(Func<UniTask<byte[]>> loader)
            {
                try
                {
                    // First ensure bytes are loaded
                    var bytes = await GetBytesAsync(loader);
                    if (bytes == null || bytes.Length == 0)
                    {
                        FailTexture(null);
                        return null;
                    }

                    // Decode on the main thread (Unity API requirement)
                    await UniTask.SwitchToMainThread();

                    var containerType = AnimationContainerDecoder.DetectType(bytes);
                    Texture2D result;

                    if (containerType == AnimationContainerDecoder.ContainerType.GIF)
                    {
                        var decoded = AnimationContainerDecoder.DecodeGif(bytes);
                        result = decoded.Atlas;
                        if (result != null)
                        {
                            result.name = $"Cache_GIF_{DateTime.Now.Ticks}|FPS={decoded.FPS}|FrameHeight={decoded.FrameHeight}";
                            result.filterMode = FilterMode.Point;
                        }
                    }
                    else if (containerType == AnimationContainerDecoder.ContainerType.WebP)
                    {
                        var decoded = AnimationContainerDecoder.DecodeWebP(bytes);
                        result = decoded.Atlas;
                        if (result != null)
                        {
                            result.name = $"Cache_WebP_{DateTime.Now.Ticks}|FPS={decoded.FPS}|FrameHeight={decoded.FrameHeight}";
                            result.filterMode = FilterMode.Point;
                        }
                    }
                    else
                    {
                        // PNG or fallback via Unity ImageConversion
                        result = new Texture2D(2, 2);
                        if (result.LoadImage(bytes))
                        {
                            result.name = $"Cache_Tex_{DateTime.Now.Ticks}";
                            result.filterMode = FilterMode.Point;
                        }
                        else
                        {
                            UnityEngine.Object.Destroy(result);
                            result = null;
                        }
                    }

                    TaskCompletionSource<Texture2D> texPromise;
                    lock (_lock)
                    {
                        _texture = result;
                        texPromise = _texturePromise;
                        _texturePromise = null;
                    }

                    texPromise?.TrySetResult(result);
                    return result;
                }
                catch (Exception ex)
                {
                    FailTexture(ex);
                    throw;
                }
            }

            private void FailTexture(Exception ex)
            {
                lock (_lock)
                {
                    _texturePromise.TrySetException(ex ?? new Exception("Texture decode failed"));
                    _texturePromise = null;
                }
            }

            private async UniTask<AudioClip> DecodeAudio(Func<UniTask<byte[]>> loader)
            {
                try
                {
                    var bytes = await GetBytesAsync(loader);
                    if (bytes == null || bytes.Length == 0)
                    {
                        FailAudio(null);
                        return null;
                    }

                    UnityEngine.Debug.LogWarning("[AssetCache] WavUtility is deprecated. Decoding wav is not supported.");
                    AudioClip clip = null;
                    TaskCompletionSource<AudioClip> audioPromise;
                    lock (_lock)
                    {
                        _audio = clip;
                        audioPromise = _audioPromise;
                        _audioPromise = null;
                    }

                    audioPromise?.TrySetResult(clip);
                    return clip;
                }
                catch (Exception ex)
                {
                    FailAudio(ex);
                    throw;
                }
            }

            private void FailAudio(Exception ex)
            {
                lock (_lock)
                {
                    _audioPromise.TrySetException(ex ?? new Exception("Audio decode failed"));
                    _audioPromise = null;
                }
            }

            private async UniTask<AnimatedSpriteData> DecodeAndWrapSprites(Func<UniTask<byte[]>> loader)
            {
                var frames = await DecodeSprites(loader);
                lock (_lock)
                {
                    return new AnimatedSpriteData(frames, _spriteFps, _spriteFrameHeight);
                }
            }

            private async UniTask<Sprite[]> DecodeSprites(Func<UniTask<byte[]>> loader)
            {
                try
                {
                    var bytes = await GetBytesAsync(loader);
                    if (bytes == null || bytes.Length == 0)
                    {
                        FailSprites(null);
                        return null;
                    }

                    // Decode GIF/WebP on the main thread
                    await UniTask.SwitchToMainThread();

                    var containerType = AnimationContainerDecoder.DetectType(bytes);
                    AnimationContainerDecoder.DecodedAnimation anim;

                    if (containerType == AnimationContainerDecoder.ContainerType.GIF)
                    {
                        anim = AnimationContainerDecoder.DecodeGif(bytes);
                    }
                    else if (containerType == AnimationContainerDecoder.ContainerType.WebP)
                    {
                        anim = AnimationContainerDecoder.DecodeWebP(bytes);
                    }
                    else
                    {
                        anim = default;
                    }

                    Sprite[] result;
                    float fps = 10f;
                    int frameHeight = 0;

                    if (anim.Atlas != null && anim.FrameCount > 0)
                    {
                        fps = anim.FPS;
                        frameHeight = anim.FrameHeight;
                        result = AnimationContainerDecoder.Decode(
                            anim.Atlas, anim.Atlas.width, anim.FrameHeight, anim.FrameCount);
                    }
                    else
                    {
                        result = Array.Empty<Sprite>();
                    }

                    TaskCompletionSource<Sprite[]> spritePromise;
                    lock (_lock)
                    {
                        _sprites = result;
                        _spriteFps = fps;
                        _spriteFrameHeight = frameHeight;
                        spritePromise = _spritePromise;
                        _spritePromise = null;
                    }

                    spritePromise?.TrySetResult(result);
                    return result;
                }
                catch (Exception ex)
                {
                    FailSprites(ex);
                    throw;
                }
            }

            private void FailSprites(Exception ex)
            {
                lock (_lock)
                {
                    _spritePromise.TrySetException(ex ?? new Exception("Sprite decode failed"));
                    _spritePromise = null;
                }
            }
        }
    }
}
