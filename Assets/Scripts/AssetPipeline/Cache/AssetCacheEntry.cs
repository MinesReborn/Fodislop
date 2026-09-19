#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.World;
using UnityEngine;

namespace Kern;

internal sealed class AssetCacheEntry
{
    private readonly object _lock = new();
    private readonly string _filename;
    private readonly AssetCache _cache;

    // ── Raw bytes ──
    private byte[]? _bytes;
    private TaskCompletionSource<byte[]?>? _bytesPromise;

    // ── Derived formats (lazy, computed on first request) ──
    private Texture2D? _texture;
    private TaskCompletionSource<Texture2D?>? _texturePromise;

    private AudioClip? _audio;
    private TaskCompletionSource<AudioClip?>? _audioPromise;
    private bool _wavWarningLogged;

    private Sprite[]? _sprites;
    private TaskCompletionSource<Sprite[]?>? _spritePromise;

    // Stored alongside sprites for AnimatedSpriteData lookups
    private float _spriteFPS;
    private int _spriteFrameHeight;
    private int _spriteFrameCount;

    internal AssetCacheEntry(string filename, AssetCache cache)
    {
        _filename = filename;
        _cache = cache;
    }

    internal void ReleaseAllReferences()
    {
        lock (_lock)
        {
            ReleaseDecodedUnlocked();
            _bytes = null;
        }
    }

    internal void ReleaseDecodedReference()
    {
        lock (_lock)
        {
            ReleaseDecodedUnlocked();
        }
    }

    private void ReleaseDecodedUnlocked()
    {
        _texture = null;
        _sprites = null;
        _audio = null;
        _spriteFPS = 0f;
        _spriteFrameHeight = 0;
        _spriteFrameCount = 0;
    }

    internal long EstimateDecodedBytes()
    {
        lock (_lock)
        {
            HashSet<Texture2D> textures = [];
            if (_texture != null)
            {
                textures.Add(_texture);
            }

            if (_sprites != null)
            {
                for (int i = 0; i < _sprites.Length; i++)
                {
                    if (_sprites[i] != null && _sprites[i].texture != null)
                    {
                        textures.Add(_sprites[i].texture);
                    }
                }
            }

            long total = 0;
            foreach (var texture in textures)
            {
                total += (long)texture.width * texture.height * 4;
            }

            return total;
        }
    }

    private UniTask<T?> GetOrCreateAsync<T>(
        T? cached,
        ref TaskCompletionSource<T?>? promise,
        Func<UniTask<T?>> factory)
        where T : class
    {
        lock (_lock)
        {
            if (cached != null)
            {
                return UniTask.FromResult<T?>(cached);
            }

            if (promise != null)
            {
                return promise.Task.AsUniTask();
            }

            promise = new TaskCompletionSource<T?>();
        }

        return factory();
    }

    private void FailPromise<T>(ref TaskCompletionSource<T?>? promise, Exception ex)
        where T : class
    {
        ReleaseRawBytes();
        lock (_lock)
        {
            promise?.TrySetException(ex);
            promise = null;
        }
    }

    public UniTask<byte[]?> GetBytesAsync(Func<UniTask<byte[]?>> loader) =>
        GetOrCreateAsync(_bytes, ref _bytesPromise, () => LoadBytes(loader));

    public UniTask<Texture2D?> GetTextureAsync(Func<UniTask<byte[]?>> loader) =>
        GetOrCreateAsync(_texture, ref _texturePromise, () => DecodeTexture(loader));

    public UniTask<AudioClip?> GetAudioAsync(Func<UniTask<byte[]?>> loader) =>
        GetOrCreateAsync(_audio, ref _audioPromise, () => DecodeAudio(loader));

    public UniTask<Sprite[]?> GetSpritesAsync(Func<UniTask<byte[]?>> loader) =>
        GetOrCreateAsync(_sprites, ref _spritePromise, () => DecodeSprites(loader));

    public async UniTask<AnimatedSpriteData> GetAnimatedSpritesAsync(Func<UniTask<byte[]?>> loader)
    {
        var frames = await GetSpritesAsync(loader);
        if (frames == null)
        {
            throw new InvalidOperationException("Sprite frames were not decoded (null).");
        }

        lock (_lock)
        {
            return new AnimatedSpriteData(frames, _spriteFPS, _spriteFrameHeight);
        }
    }

    private async UniTask<byte[]?> LoadBytes(Func<UniTask<byte[]?>> loader)
    {
        try
        {
            var bytes = await loader();
            TaskCompletionSource<byte[]?>? promise;
            lock (_lock)
            {
                _bytes = bytes;
                promise = _bytesPromise;
                _bytesPromise = null;
            }

            if (bytes != null && bytes.Length > 0)
            {
                _cache.TrackAccess(_filename, bytes.Length);
            }

            promise?.TrySetResult(bytes);
            return bytes;
        }
        catch (Exception ex)
        {
            FailPromise(ref _bytesPromise, ex);
            throw;
        }
    }

    private async UniTask<Texture2D?> DecodeTexture(Func<UniTask<byte[]?>> loader)
    {
        try
        {
            var bytes = await GetBytesAsync(loader);
            if (bytes == null || bytes.Length == 0)
            {
                var emptyEx = new InvalidOperationException($"Empty or null bytes for texture '{_filename}'.");
                FailTexture(emptyEx);
                throw emptyEx;
            }

            await UniTask.SwitchToMainThread();

            var decoded = AssetCacheDecoder.DecodeTexture(bytes, _filename);

            TaskCompletionSource<Texture2D?>? texPromise;
            lock (_lock)
            {
                _texture = decoded.Texture;
                _spriteFPS = decoded.FPS;
                _spriteFrameHeight = decoded.FrameHeight;
                _spriteFrameCount = decoded.FrameCount;
                texPromise = _texturePromise;
                _texturePromise = null;
            }

            _cache.TrackDecoded(_filename, EstimateDecodedBytes());
            texPromise?.TrySetResult(decoded.Texture);
            ReleaseRawBytes();
            return decoded.Texture;
        }
        catch (Exception ex)
        {
            FailTexture(ex);
            throw;
        }
    }

    private void FailTexture(Exception ex) => FailPromise(ref _texturePromise, ex);

    private async UniTask<AudioClip?> DecodeAudio(Func<UniTask<byte[]?>> loader)
    {
        try
        {
            var bytes = await GetBytesAsync(loader);
            if (bytes == null || bytes.Length == 0)
            {
                var emptyEx = new InvalidOperationException($"Empty or null bytes for audio '{_filename}'.");
                FailAudio(emptyEx);
                throw emptyEx;
            }

            if (!_wavWarningLogged)
            {
                Debug.LogWarning(
                    $"[AssetCache] WAV decoding is unsupported for '{_filename}'; request will fail.");
                _wavWarningLogged = true;
            }

            var unsupportedEx = new NotSupportedException($"WAV decoding is not supported for '{_filename}'.");
            FailAudio(unsupportedEx);
            throw unsupportedEx;
        }
        catch (Exception ex)
        {
            FailAudio(ex);
            throw;
        }
    }

    private void FailAudio(Exception ex) => FailPromise(ref _audioPromise, ex);

    private async UniTask<Sprite[]?> DecodeSprites(Func<UniTask<byte[]?>> loader)
    {
        try
        {
            Texture2D? cachedAnimationTexture;
            float cachedFPS;
            int cachedFrameHeight;
            int cachedFrameCount;
            TaskCompletionSource<Sprite[]?>? cachedSpritePromise;
            lock (_lock)
            {
                cachedAnimationTexture = _texture;
                cachedFPS = _spriteFPS;
                cachedFrameHeight = _spriteFrameHeight;
                cachedFrameCount = _spriteFrameCount;
                cachedSpritePromise = _spritePromise;
            }

            if (cachedAnimationTexture != null && cachedFrameHeight > 0)
            {
                Sprite[] cachedSprites = AssetCacheDecoder.SliceAnimationFromTexture(
                    cachedAnimationTexture,
                    cachedFrameHeight,
                    cachedFrameCount);
                lock (_lock)
                {
                    _sprites = cachedSprites;
                    _spriteFPS = cachedFPS;
                    _spriteFrameCount = cachedFrameCount > 0
                        ? cachedFrameCount
                        : Mathf.Max(1, cachedAnimationTexture.height / cachedFrameHeight);
                    _spritePromise = null;
                }

                _cache.TrackDecoded(_filename, EstimateDecodedBytes());
                cachedSpritePromise?.TrySetResult(cachedSprites);
                return cachedSprites;
            }

            var bytes = await GetBytesAsync(loader);
            if (bytes == null || bytes.Length == 0)
            {
                var emptyEx = new InvalidOperationException($"Empty or null bytes for sprites '{_filename}'.");
                FailSprites(emptyEx);
                throw emptyEx;
            }

            await UniTask.SwitchToMainThread();

            var anim = AssetCacheDecoder.DecodeAnimationSprites(bytes, _filename);

            TaskCompletionSource<Sprite[]?>? spritePromise;
            lock (_lock)
            {
                _sprites = anim.Sprites;
                _spriteFPS = anim.FPS;
                _spriteFrameHeight = anim.FrameHeight;
                _spriteFrameCount = anim.FrameCount;
                _texture = anim.Atlas;
                spritePromise = _spritePromise;
                _spritePromise = null;
            }

            _cache.TrackDecoded(_filename, EstimateDecodedBytes());
            spritePromise?.TrySetResult(anim.Sprites);
            ReleaseRawBytes();
            return anim.Sprites;
        }
        catch (Exception ex)
        {
            FailSprites(ex);
            throw;
        }
    }

    private void FailSprites(Exception ex) => FailPromise(ref _spritePromise, ex);

    internal void ReleaseRawBytes()
    {
        lock (_lock)
        {
            if (_bytes == null)
            {
                return;
            }

            _bytes = null;
        }

        _cache.RemoveTrackedSize(_filename);
    }
}
