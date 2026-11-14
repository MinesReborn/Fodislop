#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using UnityEngine;
using UnityEngine.Networking;
using VContainer;

using Kern.AssetPipeline.Cache;

namespace Kern.AssetPipeline.Loading;

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
        Func<string, CancellationToken, UniTask<byte[]?>> bytesLoader,
        Func<IAsyncOperationSupervisor?> operations)
    {
        _cache = new AssetCache(bytesLoader, operations);
    }

    public UniTask<Texture2D?> GetTextureAsync(
        string url,
        CancellationToken cancellationToken = default) =>
        _cache.GetTextureAsync(url, cancellationToken);

    private static async UniTask<byte[]?> LoadBytesFromWeb(
        string url,
        CancellationToken cancellationToken)
    {
        using var timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(
            TimeSpan.FromSeconds(AssetStreamingLimits.WebRequestTimeoutSeconds));

        var download = new BoundedDownloadHandler(
            AssetStreamingLimits.MaximumWebAssetBytes);
        using UnityWebRequest request = new(url, UnityWebRequest.kHttpVerbGET, download, null);
        try
        {
            await request.SendWebRequest().WithCancellation(timeoutCancellation.Token);
        }
        catch (UnityWebRequestException) when (download.LimitExceeded)
        {
            // Обрыв по пределу: понятная ошибка — ниже, вместо сетевой.
        }

        if (download.LimitExceeded)
        {
            throw new InvalidOperationException(
                $"Web asset '{url}' exceeds {AssetStreamingLimits.MaximumWebAssetBytes} bytes.");
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            throw new InvalidOperationException(
                $"Web asset request failed for '{url}': " +
                $"{request.error} ({request.responseCode}).");
        }

        return download.ToArray();
    }

    // Адрес картинки задаёт сервер, ответ — чужой хост. Стандартный обработчик
    // копит весь ответ в памяти без предела; этот обрывает загрузку, как только
    // заявленный или фактический размер выходит за предел.
    private sealed class BoundedDownloadHandler : DownloadHandlerScript
    {
        private readonly int _limit;
        private readonly System.IO.MemoryStream _received = new();

        public BoundedDownloadHandler(int limit)
            : base(new byte[64 * 1024])
        {
            _limit = limit;
        }

        public bool LimitExceeded { get; private set; }

        public byte[] ToArray() => _received.ToArray();

        protected override void ReceiveContentLengthHeader(ulong contentLength)
        {
            LimitExceeded = contentLength > (ulong)_limit;
        }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (LimitExceeded || _received.Length + dataLength > _limit)
            {
                LimitExceeded = true;
                return false;
            }

            _received.Write(data, 0, dataLength);
            return true;
        }
    }
}
