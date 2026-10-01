#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using UnityEngine;
using UnityEngine.Networking;
using VContainer;

namespace Kern;

// RAM-only loader for window images served over http(s). It reuses the
// same AssetCache as the server asset loader — in-flight request
// deduplication, raw-byte and decoded-texture caching, LRU eviction — but
// has no persistent (disk) cache and sources bytes from the web client
// instead of the server connection.
public interface IWebAssetLoader
{
    UniTask<Texture2D?> GetTextureAsync(string url, CancellationToken cancellationToken = default);
}

public sealed class WebAssetLoader : IWebAssetLoader
{
    private readonly AssetCache _cache;

    [Inject]
    public WebAssetLoader(IAsyncOperationSupervisor operations)
        : this(LoadBytesFromWeb, () => operations)
    {
    }

    internal WebAssetLoader(
        Func<string, CancellationToken, int, UniTask<byte[]?>> bytesLoader,
        Func<IAsyncOperationSupervisor?> operations)
    {
        _cache = new AssetCache(bytesLoader, operations);
    }

    public UniTask<Texture2D?> GetTextureAsync(
        string url,
        CancellationToken cancellationToken = default) =>
        _cache.GetTextureAsync(
            url,
            cancellationToken,
            ProjectRuntimeContracts.AssetStreaming.LargeAssetRequestTimeoutSeconds);

    private static async UniTask<byte[]?> LoadBytesFromWeb(
        string url,
        CancellationToken cancellationToken,
        int timeoutSeconds)
    {
        using var timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        using UnityWebRequest request = UnityWebRequest.Get(url);
        await request.SendWebRequest().WithCancellation(timeoutCancellation.Token);
        if (request.result != UnityWebRequest.Result.Success)
        {
            throw new InvalidOperationException(
                $"Web asset request failed for '{url}': " +
                $"{request.error} ({request.responseCode}).");
        }

        return request.downloadHandler.data;
    }
}
