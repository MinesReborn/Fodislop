#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Kern.Core.Interfaces;

public interface IAssetLoader
{
    int PendingAssetCount { get; }

    int QueuedAssetCount { get; }

    bool IsKnownMissing(string filename);

    /// <summary>
    /// Файлы, которые сервер заменил посреди игры: тот, кто держит их картинки, перезагружает свои.
    /// </summary>
    event Action<IReadOnlyCollection<string>>? AssetsChanged;

    /// <summary>
    /// Сервер заменил эти файлы: прежние копии из памяти отбрасываются, и следующее обращение берёт новые из кэша на
    /// диске. Потом подписчики <see cref="AssetsChanged"/> перезагружают то, что уже показывают.
    /// </summary>
    void ReplaceAssets(IReadOnlyCollection<string> filenames);

    UniTask<byte[]?> GetAssetBytesAsync(
        string filename,
        CancellationToken cancellationToken = default);

    UniTask<byte[]?> GetAssetBytesAsync(
        string filename,
        string expectedETag,
        CancellationToken cancellationToken = default);

    UniTask<string> GetAssetPathAsync(
        string filename,
        CancellationToken cancellationToken = default);

    UniTask<Texture2D?> GetTextureAsync(string filename, CancellationToken cancellationToken = default);

    UniTask<AnimatedSpriteData> GetAnimatedSpritesAsync(
        string filename,
        CancellationToken cancellationToken = default);
}

public interface IAssetSubscription
{
    bool IsAssetSubscriptionEstablished { get; }

    void EnsureAssetSubscription();
}
