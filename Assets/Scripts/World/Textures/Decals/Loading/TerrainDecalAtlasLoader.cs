#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.AssetPipeline.Loading;
using Kern;
using Kern.Core.Interfaces;
using Kern.World.Terrain;
using UnityEngine;

namespace Kern.World.Textures;

internal sealed class TerrainDecalAtlasLoader : IDisposable
{
    private const int MaximumArrayBytes = 64 * 1024 * 1024;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _activeLoad;
    private Texture2DArray? _textureArray;
    private int _loadGeneration;
    private bool _disposed;

    public Texture2DArray? TextureArray => _textureArray;

    public void Load(
        IReadOnlyDictionary<string, byte[]> worldAssetBytes,
        IReadOnlyList<string> textureNames,
        IAsyncOperationSupervisor operations,
        Action? onTextureLoaded)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TerrainDecalAtlasLoader));
        }

        Clear();
        string[] snapshot = textureNames.ToArray();
        int generation = ++_loadGeneration;
        var generationLifetime = new CancellationTokenSource();
        _activeLoad = generationLifetime;
        _textureArray = CreateTransparentArray(snapshot.Length);
        operations.Run(
            "load_terrain_decal_atlases",
            cancellationToken => LoadAsync(
                worldAssetBytes,
                snapshot,
                onTextureLoaded,
                generation,
                generationLifetime,
                cancellationToken));
    }

    public void Clear()
    {
        if (_disposed)
        {
            return;
        }

        _loadGeneration++;
        _activeLoad?.Cancel();
        _activeLoad?.Dispose();
        _activeLoad = null;
        DestroyTextureArray(_textureArray);
        _textureArray = null;
    }

    private async UniTask LoadAsync(
        IReadOnlyDictionary<string, byte[]> worldAssetBytes,
        string[] textureNames,
        Action? onTextureLoaded,
        int generation,
        CancellationTokenSource generationLifetime,
        CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token,
            generationLifetime.Token);
        CancellationToken token = linkedCancellation.Token;
        var loaded = new List<(byte TextureIndex, Texture2D Texture)>(textureNames.Length);
        try
        {
            foreach (string textureName in textureNames)
            {
                token.ThrowIfCancellationRequested();
                if (!worldAssetBytes.TryGetValue(textureName, out byte[]? bytes))
                {
                    throw new InvalidDataException(
                        $"Cell configuration references decal texture '{textureName}' absent from the loaded world asset manifest.");
                }

                if (bytes == null || bytes.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"Required decal texture '{textureName}' has no loaded bytes.");
                }

                await UniTask.SwitchToMainThread(token);
                Texture2D texture = RuntimeTextureFactory.DecodeEncodedImageToRGBA32NoMip(
                    bytes,
                    textureName,
                    RuntimeTextureColorSpace.Srgb,
                    FilterMode.Point,
                    TextureWrapMode.Clamp,
                    makeNoLongerReadable: true);
                loaded.Add((TerrainDecalCatalog.TextureIndex(textureName), texture));

            }

            token.ThrowIfCancellationRequested();
            await UniTask.SwitchToMainThread(token);
            if (generation != _loadGeneration)
            {
                return;
            }

            Texture2DArray? newArray = null;
            if (loaded.Count > 0)
            {
                int sliceCount = textureNames.Length + 1;
                long requiredBytes = (long)TerrainDecalFactory.TextureWidth *
                    TerrainDecalFactory.TextureHeight * sliceCount * 4;
                if (requiredBytes > MaximumArrayBytes)
                {
                    throw new InvalidDataException(
                        $"Decal texture array requires {requiredBytes} bytes; limit is {MaximumArrayBytes}.");
                }

                newArray = RuntimeTextureArrayFactory.CreateRGBA32NoMip(
                    TerrainDecalFactory.TextureWidth,
                    TerrainDecalFactory.TextureHeight,
                    sliceCount,
                    "ServerTerrainDecals",
                    RuntimeTextureColorSpace.Srgb,
                    FilterMode.Point,
                    TextureWrapMode.Clamp);
                foreach ((byte textureIndex, Texture2D texture) in loaded)
                {
                    token.ThrowIfCancellationRequested();
                    Graphics.CopyTexture(texture, 0, 0, newArray, textureIndex, 0);
                }
            }

            token.ThrowIfCancellationRequested();
            DestroyTextureArray(_textureArray);
            _textureArray = newArray;
            onTextureLoaded?.Invoke();
        }
        catch (OperationCanceledException) when (
            _lifetime.IsCancellationRequested || generationLifetime.IsCancellationRequested)
        {
        }
        catch
        {
            throw;
        }
        finally
        {
            if (loaded.Count > 0)
            {
                await UniTask.SwitchToMainThread();
                foreach ((byte _, Texture2D texture) in loaded)
                {
                    if (texture != null)
                    {
                        DestroyTexture(texture);
                    }
                }
            }

            if (generation == _loadGeneration && ReferenceEquals(_activeLoad, generationLifetime))
            {
                _activeLoad.Dispose();
                _activeLoad = null;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _loadGeneration++;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _activeLoad?.Cancel();
        _activeLoad?.Dispose();
        _activeLoad = null;
        DestroyTextureArray(_textureArray);
        _textureArray = null;
    }

    private static void DestroyTextureArray(Texture2DArray? texture)
    {
        if (texture == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(texture);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static Texture2DArray? CreateTransparentArray(int textureCount)
    {
        if (textureCount == 0)
        {
            return null;
        }

        int sliceCount = textureCount + 1;
        Texture2DArray texture = RuntimeTextureArrayFactory.CreateRGBA32NoMip(
            1,
            1,
            sliceCount,
            "EmptyServerTerrainDecals",
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp);
        Color32[] transparentPixel = { new(0, 0, 0, 0) };
        for (int slice = 0; slice < sliceCount; slice++)
        {
            texture.SetPixels32(transparentPixel, slice);
        }

        texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return texture;
    }

    private static void DestroyTexture(Texture2D texture)
    {
        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(texture);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
