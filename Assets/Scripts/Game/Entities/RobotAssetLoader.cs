#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using UnityEngine;

namespace Fodinae.Game;

/// <summary>
/// Handles asynchronous loading of skin, tail, and clan textures for a Robot entity.
/// </summary>
public sealed class RobotAssetLoader
{
    private const string TAG = "[Robot]";
    private readonly IAssetLoader _assetLoader;
    private readonly IAsyncOperationSupervisor _operations;
    private readonly CancellationToken _destroyToken;
    private CancellationTokenSource? _cts;

    public RobotAssetLoader(
        IAssetLoader assetLoader,
        IAsyncOperationSupervisor operations,
        CancellationToken destroyToken)
    {
        _assetLoader = assetLoader;
        _operations = operations;
        _destroyToken = destroyToken;
    }

    public void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void LoadMetadataAssets(
        string skinPath,
        string tailPath,
        byte clanId,
        bool isLocalPlayer,
        Action<Sprite?> onSkinLoaded,
        Action<Texture2D?> onTailLoaded,
        Action<Sprite?> onClanLoaded)
    {
        Cancel();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(_destroyToken);
        CancellationToken entityToken = _cts.Token;

        _operations.Run(
            "load_robot_metadata_assets",
            supervisorToken => LoadMetadataAssetsAsync(
                skinPath,
                tailPath,
                clanId,
                isLocalPlayer,
                onSkinLoaded,
                onTailLoaded,
                onClanLoaded,
                entityToken,
                supervisorToken));
    }

    public void LoadClanBadge(byte clanId, Action<Sprite?> onClanLoaded)
    {
        if (_cts == null)
        {
            return;
        }

        CancellationToken entityToken = _cts.Token;
        _operations.Run(
            "load_robot_clan_badge",
            supervisorToken => RunWithLinkedCancellationAsync(
                token => LoadClanAsync(clanId, onClanLoaded, token),
                entityToken,
                supervisorToken));
    }

    private async UniTask LoadMetadataAssetsAsync(
        string skinPath,
        string tailPath,
        byte clanId,
        bool isLocalPlayer,
        Action<Sprite?> onSkinLoaded,
        Action<Texture2D?> onTailLoaded,
        Action<Sprite?> onClanLoaded,
        CancellationToken entityToken,
        CancellationToken supervisorToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            entityToken,
            supervisorToken);
        CancellationToken token = linkedCancellation.Token;
        UniTask clanTask = isLocalPlayer
            ? UniTask.CompletedTask
            : LoadClanAsync(clanId, onClanLoaded, token);

        await UniTask.WhenAll(
            LoadSkinAsync(skinPath, onSkinLoaded, token),
            LoadTailAsync(tailPath, onTailLoaded, token),
            clanTask);
    }

    private static async UniTask RunWithLinkedCancellationAsync(
        Func<CancellationToken, UniTask> operation,
        CancellationToken entityToken,
        CancellationToken supervisorToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            entityToken,
            supervisorToken);
        await operation(linkedCancellation.Token);
    }

    private async UniTask LoadSkinAsync(
        string skinPath,
        Action<Sprite?> onSkinLoaded,
        CancellationToken token)
    {
        if (string.IsNullOrEmpty(skinPath))
        {
            return;
        }

        Texture2D? skinTexture = await TryLoadOptionalTextureAsync(_assetLoader, skinPath, token);
        if (token.IsCancellationRequested)
        {
            return;
        }

        if (skinTexture == null)
        {
            onSkinLoaded(null);
            return;
        }

        Sprite skinSprite = Sprite.Create(
            skinTexture,
            new Rect(0, 0, skinTexture.width, skinTexture.height),
            new Vector2(0.5f, 0.5f),
            skinTexture.width);
        onSkinLoaded(skinSprite);
    }

    private async UniTask LoadTailAsync(
        string tailPath,
        Action<Texture2D?> onTailLoaded,
        CancellationToken token)
    {
        if (string.IsNullOrEmpty(tailPath))
        {
            onTailLoaded(null);
            return;
        }

        Texture2D? tailTexture = await TryLoadOptionalTextureAsync(_assetLoader, tailPath, token);
        if (token.IsCancellationRequested)
        {
            return;
        }

        onTailLoaded(tailTexture);
    }

    private async UniTask LoadClanAsync(
        byte clanId,
        Action<Sprite?> onClanLoaded,
        CancellationToken token)
    {
        if (clanId == 0)
        {
            return;
        }

        string clanPath = $"/Clan/{clanId}";
        Texture2D? clanTexture = await TryLoadOptionalTextureAsync(_assetLoader, clanPath, token);
        if (token.IsCancellationRequested || clanTexture == null)
        {
            return;
        }

        Sprite clanSprite = Sprite.Create(
            clanTexture,
            new Rect(0, 0, clanTexture.width, clanTexture.height),
            new Vector2(0f, 0.5f),
            clanTexture.width);
        onClanLoaded(clanSprite);
    }

    public static async UniTask<Texture2D?> TryLoadOptionalTextureAsync(
        IAssetLoader loader,
        string filename,
        CancellationToken cancellationToken)
    {
        try
        {
            return await loader.GetTextureAsync(filename, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"{TAG} Optional texture '{filename}' was skipped: {exception.Message}");
            return null;
        }
    }

    public static Sprite CreateEditorPreviewSprite()
    {
        Texture2D placeholder = Texture2D.whiteTexture;
        return Sprite.Create(
            placeholder,
            new Rect(0, 0, placeholder.width, placeholder.height),
            new Vector2(0.5f, 0.5f),
            ProjectRuntimeContracts.PreviewVisuals.RobotPixelsPerUnit);
    }
}
