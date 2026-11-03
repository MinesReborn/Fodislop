#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.AssetPipeline.Cache;
using Kern.Core;
using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Connection;

namespace Kern.World.Textures;

internal sealed class AssetManifestSynchronizer
{
    private readonly IPersistentAssetCache _persistentCache;
    private readonly IAssetLoader _assetLoader;

    public AssetManifestSynchronizer(
        IPersistentAssetCache persistentCache,
        IAssetLoader assetLoader)
    {
        _persistentCache = persistentCache;
        _assetLoader = assetLoader;
    }

    private const int MaxDownloadAttempts = 2;

    private long _bytesDone;

    // Сначала сверяем кэш (onCheckProgress: проверено, всего): то, что лежит на диске и совпадает по MD5, скачиванием не
    // считается. onProgress получает вес реально скачиваемого (готово, всего); (0, 0) — всё уже в кэше, показывать нечего.
    public async UniTask<IReadOnlyDictionary<string, byte[]>> SynchronizeAsync(
        IReadOnlyList<AssetsDefinitionPacket> serverManifest,
        CancellationToken cancellationToken,
        Action<long, long>? onProgress = null,
        Func<long>? receivedBytes = null,
        Action<int, int>? onCheckProgress = null)
    {
        Dictionary<string, AssetsDefinitionPacket> expectedByFilename =
            CreateExpectedManifest(serverManifest);

        using var slots = new SemaphoreSlim(AssetStreamingLimits.MaximumConcurrentManifestAssetDownloads);
        AssetsDefinitionPacket[] definitions = expectedByFilename.Values.ToArray();

        // Проверка кэша — тоже работа, и на больших файлах заметная: показываем «проверено из скольких»
        int filesChecked = 0;
        onCheckProgress?.Invoke(0, definitions.Length);

        async UniTask<byte[]?> CheckCachedAsync(AssetsDefinitionPacket definition)
        {
            byte[]? hit = await TryReadCachedAsync(definition, slots, cancellationToken);
            onCheckProgress?.Invoke(Interlocked.Increment(ref filesChecked), definitions.Length);
            return hit;
        }

        byte[]?[] cached = await UniTask.WhenAll(definitions.Select(CheckCachedAsync));
        cancellationToken.ThrowIfCancellationRequested();

        long bytesTotal = 0;
        for (int i = 0; i < definitions.Length; i++)
        {
            if (cached[i] == null)
            {
                bytesTotal += definitions[i].SizeBytes;
            }
        }

        // Показанный вес только растёт: его двигают и готовые файлы, и счётчик принятых байт
        long published = 0;
        void Publish(long value)
        {
            value = Math.Min(value, bytesTotal);
            if (value > published)
            {
                published = value;
                onProgress?.Invoke(value, bytesTotal);
            }
        }

        onProgress?.Invoke(0, bytesTotal);

        async UniTask<byte[]> DownloadCountedAsync(AssetsDefinitionPacket definition)
        {
            byte[] contents = await DownloadAsync(definition, slots, cancellationToken);
            Publish(Interlocked.Add(ref _bytesDone, definition.SizeBytes));
            return contents;
        }

        // Крупный файл приходит одним пакетом, и целые файлы дают скачки. Между ними вес двигает счётчик принятых
        // транспортом байт; до готовности он не доходит до конца (в счёт попадает и прочий трафик мира)
        using var pumpCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        UniTask pumpTask = receivedBytes != null && bytesTotal > 0
            ? RunTrafficPumpAsync(receivedBytes, bytesTotal, Publish, pumpCancellation.Token)
            : UniTask.CompletedTask;

        var assetContents = new byte[definitions.Length][];
        var downloads = new List<(int Index, UniTask<byte[]> Task)>();
        byte[][] downloaded;
        try
        {
            for (int i = 0; i < definitions.Length; i++)
            {
                byte[]? hit = cached[i];
                if (hit != null)
                {
                    assetContents[i] = hit;
                }
                else
                {
                    downloads.Add((i, DownloadCountedAsync(definitions[i])));
                }
            }

            downloaded = await UniTask.WhenAll(downloads.Select(download => download.Task));
        }
        finally
        {
            pumpCancellation.Cancel();
            await pumpTask;
        }

        for (int i = 0; i < downloads.Count; i++)
        {
            assetContents[downloads[i].Index] = downloaded[i];
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Всё качается по манифесту: пришёл новый — в кэше остаётся только он, остальное сносится сразу
        int removedFiles = _persistentCache.RetainOnly(
            definitions.Select(definition => definition.Filename).ToArray());
        if (removedFiles > 0)
        {
            UnityEngine.Debug.Log($"[AssetManifestSynchronizer] Removed {removedFiles} cached files that are not in the world manifest.");
        }

        var loadedAssets = new Dictionary<string, byte[]>(definitions.Length, StringComparer.Ordinal);
        for (int i = 0; i < definitions.Length; i++)
        {
            loadedAssets.Add(definitions[i].Filename, assetContents[i]);
        }

        return loadedAssets;
    }

    private static async UniTask RunTrafficPumpAsync(
        Func<long> receivedBytes,
        long bytesTotal,
        Action<long> publish,
        CancellationToken cancellationToken)
    {
        long baseline = receivedBytes();
        long ceiling = bytesTotal - bytesTotal / 100;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await UniTask.Delay(100, cancellationToken: cancellationToken);
                long received = receivedBytes();
                if (received < baseline)
                {
                    baseline = 0;                       // транспорт сменился при переподключении — счёт начался заново
                }

                publish(Math.Min(received - baseline, ceiling));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Файл из кэша, если он цел и совпадает с серверным манифестом; иначе null — его надо скачать.
    private async UniTask<byte[]?> TryReadCachedAsync(
        AssetsDefinitionPacket definition,
        SemaphoreSlim slots,
        CancellationToken cancellationToken)
    {
        await slots.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // GetAssetAsync возвращает файл только если он прошёл проверку по манифесту кэша (MD5),
            // порченый удаляет и отдаёт null. Остаётся сверить записанный ETag с серверным — хеш заново не считаем
            byte[]? cachedContents = await _persistentCache.GetAssetAsync(definition.Filename);
            if (cachedContents is { Length: > 0 } &&
                string.Equals(
                    _persistentCache.GetRecordedETag(definition.Filename),
                    definition.ETag,
                    StringComparison.OrdinalIgnoreCase))
            {
                return cachedContents;
            }

            return null;
        }
        finally
        {
            slots.Release();
        }
    }

    private async UniTask<byte[]> DownloadAsync(
        AssetsDefinitionPacket definition,
        SemaphoreSlim slots,
        CancellationToken cancellationToken)
    {
        await slots.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Пустой ответ или несовпавший MD5 — повреждённая передача: одна повторная докачка, потом ошибка
            for (int attempt = 1; ; attempt++)
            {
                byte[]? received = await _assetLoader.GetAssetBytesAsync(
                    definition.Filename,
                    definition.ETag,
                    cancellationToken);
                string? actualETag = received is { Length: > 0 }
                    ? await UniTask.RunOnThreadPool(() => ETagCalculator.Calculate(received), cancellationToken: cancellationToken)
                    : null;
                if (received != null &&
                    string.Equals(actualETag, definition.ETag, StringComparison.OrdinalIgnoreCase))
                {
                    // Загрузчик уже сохранил файл при получении пакета; второй раз пишем, только если запись не совпала
                    if (!string.Equals(
                            _persistentCache.GetRecordedETag(definition.Filename),
                            actualETag,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        await _persistentCache.SaveAssetAsync(definition.Filename, received);
                    }

                    return received;
                }

                if (attempt >= MaxDownloadAttempts)
                {
                    throw new InvalidDataException(received is { Length: > 0 }
                        ? $"World asset '{definition.Filename}' does not match the server manifest ETag."
                        : $"World asset '{definition.Filename}' is absent from the local cache and server response.");
                }
            }
        }
        finally
        {
            slots.Release();
        }
    }

    private static Dictionary<string, AssetsDefinitionPacket> CreateExpectedManifest(
        IReadOnlyList<AssetsDefinitionPacket> serverManifest)
    {
        var expectedByFilename = new Dictionary<string, AssetsDefinitionPacket>(StringComparer.Ordinal);
        foreach (AssetsDefinitionPacket definition in serverManifest)
        {
            if (string.IsNullOrWhiteSpace(definition.Filename) || string.IsNullOrWhiteSpace(definition.ETag))
            {
                throw new InvalidDataException("World asset manifest contains an empty filename or ETag.");
            }

            if (expectedByFilename.TryGetValue(definition.Filename, out AssetsDefinitionPacket previous))
            {
                if (!string.Equals(previous.ETag, definition.ETag, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"World asset manifest contains conflicting ETags for '{definition.Filename}'.");
                }

                continue;
            }

            expectedByFilename.Add(definition.Filename, definition);
        }

        return expectedByFilename;
    }
}
