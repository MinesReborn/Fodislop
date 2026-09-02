#nullable enable

using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Fodinae.Core.Interfaces;

public interface IAssetLoader
{
    int PendingAssetCount { get; }

    int QueuedAssetCount { get; }

    bool IsKnownMissing(string filename);

    UniTask<byte[]?> GetAssetBytesAsync(
        string filename,
        CancellationToken cancellationToken = default,
        int timeoutSeconds = ProjectRuntimeContracts.AssetStreaming.AssetRequestTimeoutSeconds);

    UniTask<string> GetAssetPathAsync(
        string filename,
        CancellationToken cancellationToken = default,
        int timeoutSeconds = ProjectRuntimeContracts.AssetStreaming.AssetRequestTimeoutSeconds);

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
