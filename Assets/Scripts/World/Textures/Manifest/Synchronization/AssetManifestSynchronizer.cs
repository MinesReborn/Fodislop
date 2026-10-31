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

    public async UniTask<IReadOnlyDictionary<string, byte[]>> SynchronizeAsync(
        IReadOnlyList<WorldAssetDefinitionPacket> serverManifest,
        CancellationToken cancellationToken)
    {
        Dictionary<string, WorldAssetDefinitionPacket> expectedByFilename =
            CreateExpectedManifest(serverManifest);

        using var slots = new SemaphoreSlim(AssetStreamingLimits.MaximumConcurrentManifestAssetDownloads);
        WorldAssetDefinitionPacket[] definitions = expectedByFilename.Values.ToArray();
        UniTask<byte[]>[] downloads = definitions
            .Select(definition => SynchronizeAssetAsync(definition, slots, cancellationToken))
            .ToArray();
        byte[][] assetContents = await UniTask.WhenAll(downloads);
        cancellationToken.ThrowIfCancellationRequested();

        var loadedAssets = new Dictionary<string, byte[]>(definitions.Length, StringComparer.Ordinal);
        for (int i = 0; i < definitions.Length; i++)
        {
            WorldAssetDefinitionPacket definition = definitions[i];
            loadedAssets.Add(definition.Filename, assetContents[i]);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return loadedAssets;
    }

    private async UniTask<byte[]> SynchronizeAssetAsync(
        WorldAssetDefinitionPacket definition,
        SemaphoreSlim slots,
        CancellationToken cancellationToken)
    {
        await slots.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[]? cachedContents = await _persistentCache.GetAssetAsync(definition.Filename);
            if (cachedContents is { Length: > 0 } &&
                string.Equals(
                    ETagCalculator.Calculate(cachedContents),
                    definition.ETag,
                    StringComparison.OrdinalIgnoreCase))
            {
                return cachedContents;
            }

            byte[]? contents = await _assetLoader.GetAssetBytesAsync(
                definition.Filename,
                definition.ETag,
                cancellationToken);
            if (contents == null || contents.Length == 0)
            {
                throw new InvalidDataException(
                    $"World asset '{definition.Filename}' is absent from the local cache and server response.");
            }

            string actualETag = ETagCalculator.Calculate(contents) ??
                throw new InvalidDataException(
                    $"World asset '{definition.Filename}' did not produce an ETag.");
            if (!string.Equals(actualETag, definition.ETag, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"World asset '{definition.Filename}' does not match the server manifest ETag.");
            }

            await _persistentCache.SaveAssetAsync(definition.Filename, contents);
            return contents;
        }
        finally
        {
            slots.Release();
        }
    }

    private static Dictionary<string, WorldAssetDefinitionPacket> CreateExpectedManifest(
        IReadOnlyList<WorldAssetDefinitionPacket> serverManifest)
    {
        var expectedByFilename = new Dictionary<string, WorldAssetDefinitionPacket>(StringComparer.Ordinal);
        foreach (WorldAssetDefinitionPacket definition in serverManifest)
        {
            if (string.IsNullOrWhiteSpace(definition.Filename) || string.IsNullOrWhiteSpace(definition.ETag))
            {
                throw new InvalidDataException("World asset manifest contains an empty filename or ETag.");
            }

            if (expectedByFilename.TryGetValue(definition.Filename, out WorldAssetDefinitionPacket previous))
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
