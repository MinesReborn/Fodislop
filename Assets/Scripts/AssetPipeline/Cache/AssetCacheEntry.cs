#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.World;
using UnityEngine;

namespace Fodinae;

/// <summary>
/// Thread-safe holder for a single cached asset (raw bytes + derived formats).
/// Deduplicates in-flight requests and handles async decoding.
/// </summary>
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
    private float _spriteFps;
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
            _texture = null;
            _sprites = null;
            _audio = null;
            _spriteFps = 0f;
            _spriteFrameHeight = 0;
            _spriteFrameCount = 0;
            _bytes = null;
        }
    }

    internal void ReleaseDecodedReference()
    {
        lock (_lock)
        {
            _texture = null;
            _sprites = null;
            _audio = null;
            _spriteFps = 0f;
            _spriteFrameHeight = 0;
            _spriteFrameCount = 0;
        }
    }

    internal long EstimateDecodedBytes()
    {
        lock (_lock)
        {
            var textures = new HashSet<Texture2D>();
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

    public UniTask<byte[]?> GetBytesAsync(Func<UniTask<byte[]?>> loader)
    {
        TaskCompletionSource<byte[]?> promise;
        lock (_lock)
        {
            if (_bytes != null)
            {
                return UniTask.FromResult<byte[]?>(_bytes);
            }

            if (_bytesPromise != null)
            {
                return AwaitTask(_bytesPromise.Task);
            }

            _bytesPromise = promise = new TaskCompletionSource<byte[]?>();
        }

        return LoadBytes(promise, loader);
    }

    public UniTask<Texture2D?> GetTextureAsync(Func<UniTask<byte[]?>> loader)
    {
        lock (_lock)
        {
            if (_texture != null)
            {
                return UniTask.FromResult<Texture2D?>(_texture);
            }

            if (_texturePromise != null)
            {
                return AwaitTask(_texturePromise.Task);
            }

            _texturePromise = new TaskCompletionSource<Texture2D?>();
        }

        return DecodeTexture(loader);
    }

    public UniTask<AudioClip?> GetAudioAsync(Func<UniTask<byte[]?>> loader)
    {
        lock (_lock)
        {
            if (_audio != null)
            {
                return UniTask.FromResult<AudioClip?>(_audio);
            }

            if (_audioPromise != null)
            {
                return AwaitTask(_audioPromise.Task);
            }

            _audioPromise = new TaskCompletionSource<AudioClip?>();
        }

        return DecodeAudio(loader);
    }

    public UniTask<Sprite[]?> GetSpritesAsync(Func<UniTask<byte[]?>> loader)
    {
        lock (_lock)
        {
            if (_sprites != null)
            {
                return UniTask.FromResult<Sprite[]?>(_sprites);
            }

            if (_spritePromise != null)
            {
                return AwaitTask(_spritePromise.Task);
            }

            _spritePromise = new TaskCompletionSource<Sprite[]?>();
        }

        return DecodeSprites(loader);
    }

    public UniTask<AnimatedSpriteData> GetAnimatedSpritesAsync(Func<UniTask<byte[]?>> loader)
    {
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

            _spritePromise = new TaskCompletionSource<Sprite[]?>();
        }

        return DecodeAndWrapSprites(loader);
    }

    private async UniTask<AnimatedSpriteData> AwaitAnimatedSprites(Task<Sprite[]?> task)
    {
        var frames = await task;
        if (frames == null)
        {
            throw new InvalidOperationException("Sprite frames were not decoded (null).");
        }

        lock (_lock)
        {
            return new AnimatedSpriteData(
                frames,
                _spriteFps,
                _spriteFrameHeight);
        }
    }

    private static async UniTask<T> AwaitTask<T>(Task<T> task)
    {
        return await task;
    }

    private async UniTask<byte[]?> LoadBytes(TaskCompletionSource<byte[]?> promise, Func<UniTask<byte[]?>> loader)
    {
        try
        {
            var bytes = await loader();
            lock (_lock)
            {
                _bytes = bytes;
                _bytesPromise = null;
            }

            if (bytes != null && bytes.Length > 0)
            {
                _cache.TrackAccess(_filename, bytes.Length);
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
                _spriteFps = decoded.Fps;
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

    private void FailTexture(Exception ex)
    {
        ReleaseRawBytes();
        lock (_lock)
        {
            _texturePromise?.TrySetException(ex);
            _texturePromise = null;
        }
    }

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

            AudioClip? clip = null;
            TaskCompletionSource<AudioClip?>? audioPromise;
            lock (_lock)
            {
                _audio = clip;
                audioPromise = _audioPromise;
                _audioPromise = null;
            }

            var unsupportedEx = new NotSupportedException($"WAV decoding is not supported for '{_filename}'.");
            audioPromise?.TrySetException(unsupportedEx);
            ReleaseRawBytes();
            throw unsupportedEx;
        }
        catch (Exception ex)
        {
            FailAudio(ex);
            throw;
        }
    }

    private void FailAudio(Exception ex)
    {
        ReleaseRawBytes();
        lock (_lock)
        {
            _audioPromise?.TrySetException(ex);
            _audioPromise = null;
        }
    }

    private async UniTask<AnimatedSpriteData> DecodeAndWrapSprites(Func<UniTask<byte[]?>> loader)
    {
        var frames = await DecodeSprites(loader);
        lock (_lock)
        {
            if (frames == null)
            {
                throw new InvalidOperationException("Sprite frames were not decoded (null).");
            }

            return new AnimatedSpriteData(frames, _spriteFps, _spriteFrameHeight);
        }
    }

    private async UniTask<Sprite[]?> DecodeSprites(Func<UniTask<byte[]?>> loader)
    {
        try
        {
            Texture2D? cachedAnimationTexture;
            float cachedFps;
            int cachedFrameHeight;
            int cachedFrameCount;
            TaskCompletionSource<Sprite[]?>? cachedSpritePromise;
            lock (_lock)
            {
                cachedAnimationTexture = _texture;
                cachedFps = _spriteFps;
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
                    _spriteFps = cachedFps;
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
                _spriteFps = anim.Fps;
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

    private void FailSprites(Exception ex)
    {
        ReleaseRawBytes();
        lock (_lock)
        {
            _spritePromise?.TrySetException(ex);
            _spritePromise = null;
        }
    }

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
