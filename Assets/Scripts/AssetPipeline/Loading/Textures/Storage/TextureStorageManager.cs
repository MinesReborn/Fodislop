#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.World;
using UnityEngine;
using VContainer;

using Kern.Diagnostics.Frames;

namespace Kern.AssetPipeline.Loading
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Kern.AssetPipeline", sourceAssembly: null, sourceClassName: "TextureStorageManager")]
    public class TextureStorageManager : MonoBehaviour, ITextureStorageService
    {
        // Декодирование и копия в RGBA32 идут на главном потоке: маркер
        // нужен, чтобы провис кадра на приезде текстур был виден в FrameStall.
        private static readonly Unity.Profiling.ProfilerMarker s_decodeMarker = new("Kern.Textures.Decode");
        private static readonly string[] s_supportedExtensions = [".png", ".jpg", ".jpeg", ".exr"];

        private int _lastDecodeFrame = -1;

        [Inject]
        private IRuntimeAssetPaths _runtimeAssetPaths = null!;
        [SerializeField]
        private bool _enableDebugLogging = false;

        private readonly ConcurrentDictionary<string, Texture2D> _textureCache =
            new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, byte[]> _rawBytesCache =
            new(StringComparer.Ordinal);

        private void StoreInRawBytesCache(string filename, byte[] bytes)
        {
            _rawBytesCache[filename] = bytes;

            string ext = Path.GetExtension(filename);
            if (!string.IsNullOrEmpty(ext))
            {
                string withoutExt = filename.Substring(0, filename.Length - ext.Length);
                _rawBytesCache.TryAdd(withoutExt, bytes);
            }
            else
            {
                foreach (string extension in s_supportedExtensions)
                {
                    string withExtension = filename + extension;
                    if (_runtimeAssetPaths.GetTextureBytes(withExtension) != null)
                    {
                        _rawBytesCache.TryAdd(withExtension, bytes);
                        break;
                    }
                }
            }
        }

        private void StoreInTextureCache(string filename, Texture2D texture)
        {
            _textureCache[filename] = texture;

            string ext = Path.GetExtension(filename);
            if (!string.IsNullOrEmpty(ext))
            {
                string withoutExt = filename.Substring(0, filename.Length - ext.Length);
                _textureCache.TryAdd(withoutExt, texture);
            }
            else
            {
                foreach (string extension in s_supportedExtensions)
                {
                    string withExtension = filename + extension;
                    if (_runtimeAssetPaths.GetTextureBytes(withExtension) != null)
                    {
                        _textureCache.TryAdd(withExtension, texture);
                        break;
                    }
                }
            }
        }

        /// <param name="filename">The texture filename (e.g. "cells/Rock.png", "clan/4.png").</param>
        /// <returns>Loaded Texture2D.</returns>
        public async UniTask<Texture2D?> GetTextureAsync(
            string filename,
            CancellationToken cancellationToken = default)
        {
            string validatedFilename = ValidateRelativeTexturePath(filename);

            // Return cached texture if available
            if (_textureCache.TryGetValue(validatedFilename, out var cachedTexture) &&
                cachedTexture != null)
            {
                return cachedTexture;
            }

            // Try to load from disk
            var rawData = await LoadTextureFromStorage(
                validatedFilename,
                cancellationToken);

            if (rawData == null)
            {
                throw new FileNotFoundException(
                    $"Required texture '{validatedFilename}' was not found in texture storage.",
                    validatedFilename);
            }

            if (rawData.Length == 0)
            {
                throw new InvalidDataException(
                    $"Texture '{validatedFilename}' is empty.");
            }

            await UniTask.SwitchToMainThread(cancellationToken);

            // Не больше одного декодирования за кадр. Декодирование и копия в
            // RGBA32 стоят миллисекунды на главном потоке, а текстуры
            // приезжают пачками: несколько штук подряд в одном кадре давали
            // фриз. Очередь по кадрам растягивает ту же работу без пиков.
            while (_lastDecodeFrame == Time.frameCount)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            _lastDecodeFrame = Time.frameCount;
            Texture2D texture;
            using (s_decodeMarker.Auto())
            {
                texture = DecodeTexture(validatedFilename, rawData);
            }

            Kern.Diagnostics.Frames.FrameEventLog.Record(
                $"декодирована {validatedFilename} {texture.width}×{texture.height}");

            bool cacheOwnsTexture = false;
            try
            {
                texture.name = validatedFilename;
                RuntimeTextureFactory.ApplySampling(
                    texture,
                    FilterMode.Point,
                    TextureWrapMode.Clamp);
                Texture2D storedTexture = _textureCache.GetOrAdd(
                    validatedFilename,
                    texture);
                cacheOwnsTexture = ReferenceEquals(storedTexture, texture);
                StoreInTextureCache(validatedFilename, storedTexture);
                return storedTexture;
            }
            finally
            {
                if (!cacheOwnsTexture)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }
        }

        private static Texture2D DecodeTexture(string filename, byte[] data)
        {
            if (!IsSupportedEncodedImage(filename))
            {
                throw new NotSupportedException(
                    $"Texture storage cannot decode '{Path.GetExtension(filename)}' as a raster image: '{filename}'.");
            }

            bool makeNoLongerReadable = RuntimeTextureFactory.SupportsTexture2DGPUCopy;
            return RuntimeTextureFactory.DecodeEncodedImageToRGBA32NoMip(
                data,
                filename,
                RuntimeTextureColorSpace.Srgb,
                FilterMode.Point,
                TextureWrapMode.Clamp,
                makeNoLongerReadable: makeNoLongerReadable);
        }

        /// <param name="filename">The texture filename.</param>
        /// <returns>Image bytes, or null if not found.</returns>
        public async UniTask<byte[]?> GetTextureData(string filename, CancellationToken cancellationToken = default)
        {
            var data = await LoadTextureFromStorage(filename, cancellationToken);
            if (data != null)
            {
                OnTextureLoaded?.Invoke(filename);
            }

            return data;
        }

        public event Action<string>? OnTextureLoaded;

        private UniTask<byte[]?> LoadTextureFromStorage(
            string filename,
            CancellationToken cancellationToken = default)
        {
            string validatedFilename = ValidateRelativeTexturePath(filename);
            cancellationToken.ThrowIfCancellationRequested();

            if (_rawBytesCache.TryGetValue(validatedFilename, out byte[]? cachedBytes))
            {
                return UniTask.FromResult<byte[]?>(cachedBytes);
            }

            byte[]? bytes = _runtimeAssetPaths.GetTextureBytes(validatedFilename);
            if (bytes == null || bytes.Length == 0)
            {
                if (_enableDebugLogging)
                {
                    Debug.LogWarning(
                        $"[TextureStorageManager] File not found for: {validatedFilename}");
                }

                return UniTask.FromResult<byte[]?>(null);
            }

            StoreInRawBytesCache(validatedFilename, bytes);
            return UniTask.FromResult<byte[]?>(bytes);
        }

        private static string ValidateRelativeTexturePath(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                throw new ArgumentException(
                    "Texture filename cannot be null or whitespace.",
                    nameof(filename));
            }

            if (Path.IsPathRooted(filename))
            {
                throw new ArgumentException(
                    $"Texture filename must be relative: '{filename}'.",
                    nameof(filename));
            }

            if (filename.Contains('\\'))
            {
                throw new ArgumentException(
                    $"Texture filename must use forward slashes: '{filename}'.",
                    nameof(filename));
            }

            string[] segments = filename.Split('/');
            if (segments.Length == 0 ||
                segments.Any(segment =>
                    string.IsNullOrWhiteSpace(segment) ||
                    segment == "." ||
                    segment == ".."))
            {
                throw new ArgumentException(
                    $"Texture filename contains an invalid path segment: '{filename}'.",
                    nameof(filename));
            }

            return filename;
        }

        public void ClearCache()
        {
            // Loaded textures can still be referenced by renderers and UI when
            // this service is rebuilt after a domain reload. Clearing ownership
            // must not invalidate those live Unity objects.
            _textureCache.Clear();
            _rawBytesCache.Clear();

            if (_enableDebugLogging)
            {
                Debug.Log("[TextureStorageManager] Cache cleared");
            }
        }

        protected void OnDestroy()
        {
            ClearCache();
        }

        public bool HasTexture(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                return false;
            }

            string validatedFilename;
            try
            {
                validatedFilename = ValidateRelativeTexturePath(filename);
            }
            catch
            {
                return false;
            }

            if (_rawBytesCache.ContainsKey(validatedFilename) || _textureCache.ContainsKey(validatedFilename))
            {
                return true;
            }

            byte[]? bytes = _runtimeAssetPaths.GetTextureBytes(validatedFilename);
            if (bytes is { Length: > 0 })
            {
                StoreInRawBytesCache(validatedFilename, bytes);
                return true;
            }

            return false;
        }

        public async UniTask PreloadTexturesAsync(
            IReadOnlyList<string> filenames,
            CancellationToken cancellationToken = default)
        {
            if (filenames == null || filenames.Count == 0)
            {
                return;
            }

            var toDecode = new List<(string Name, byte[] Data)>();

            foreach (string filename in filenames)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(filename))
                {
                    continue;
                }

                string normalized;
                try
                {
                    normalized = ValidateRelativeTexturePath(filename);
                }
                catch
                {
                    continue;
                }

                if (!IsSupportedEncodedImage(normalized))
                {
                    throw new InvalidDataException(
                        $"Unrendered texture '{normalized}' has unsupported format " +
                        $"'{Path.GetExtension(normalized)}'; expected PNG, JPEG or EXR.");
                }

                if (_rawBytesCache.TryGetValue(normalized, out byte[]? cachedBytes))
                {
                    if (!_textureCache.ContainsKey(normalized))
                    {
                        toDecode.Add((normalized, cachedBytes));
                    }

                    continue;
                }

                byte[]? data = await LoadTextureFromStorage(normalized, cancellationToken);
                if (data != null && data.Length > 0 && !_textureCache.ContainsKey(normalized))
                {
                    toDecode.Add((normalized, data));
                }
            }

            if (toDecode.Count == 0 || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await UniTask.SwitchToMainThread(cancellationToken);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            const long frameBudgetMs = 4;

            for (int i = 0; i < toDecode.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var (texName, rawBytes) = toDecode[i];
                if (_textureCache.ContainsKey(texName))
                {
                    continue;
                }

                Texture2D texture;
                using (s_decodeMarker.Auto())
                {
                    texture = DecodeTexture(texName, rawBytes);
                }

                texture.name = texName;
                RuntimeTextureFactory.ApplySampling(
                    texture,
                    FilterMode.Point,
                    TextureWrapMode.Clamp);

                StoreInTextureCache(texName, texture);

                if (stopwatch.ElapsedMilliseconds >= frameBudgetMs && !Application.isBatchMode)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                    stopwatch.Restart();
                }
            }
        }

        private static bool IsSupportedEncodedImage(string filename)
        {
            string extension = Path.GetExtension(filename);
            return string.IsNullOrEmpty(extension) ||
                s_supportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        public string GetCacheStats()
        {
            return $"Texture Cache: {_textureCache.Count} textures, {_rawBytesCache.Count} raw entries";
        }
    }
}
