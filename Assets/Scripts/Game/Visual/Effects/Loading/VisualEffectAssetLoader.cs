#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World;
using UnityEngine;

namespace Kern.Game.Visual.Effects.Loading;

// Чем оказался визуальный эффект серверного события: набором кадров или
// одиночной картинкой. Пустое значение означает, что эффекта нет.
internal readonly struct LoadedVisualEffect
{
    public Sprite[]? Frames { get; init; }

    public float FrameDuration { get; init; }

    // Спрайт создан здесь и принадлежит вызывающему: он его и освобождает.
    public Sprite? StaticSprite { get; init; }

}

// Загружает кадры анимации или статичную картинку визуального эффекта.
internal sealed class VisualEffectAssetLoader
{
    private readonly IAssetLoader _assetLoader;

    public VisualEffectAssetLoader(IAssetLoader assetLoader)
    {
        _assetLoader = assetLoader ?? throw new ArgumentNullException(nameof(assetLoader));
    }

    public async UniTask<LoadedVisualEffect> LoadAsync(string visualEffectName, CancellationToken token)
    {
        string filename = $"Visual/{visualEffectName.ToLowerInvariant()}.png";
        try
        {
            AnimatedSpriteData animData = await _assetLoader.GetAnimatedSpritesAsync(filename, token);
            if (token.IsCancellationRequested)
            {
                return default;
            }

            if (animData.Frames != null && animData.Frames.Length > 0)
            {
                return new LoadedVisualEffect
                {
                    Frames = animData.Frames,
                    FrameDuration = animData.FrameDuration,
                };
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Анимация отсутствует; попробуем статичную картинку.
        }

        try
        {
            Texture2D? texture = await _assetLoader.GetTextureAsync(filename, token);
            if (token.IsCancellationRequested)
            {
                return default;
            }

            if (texture != null)
            {
                return new LoadedVisualEffect
                {
                    StaticSprite = Sprite.Create(
                        texture,
                        new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f),
                        RenderingConstants.PIXELS_PER_UNIT),
                };
            }
        }
        catch (Exception)
        {
            // Для этого эффекта нет статичной картинки.
        }

        return default;
    }
}
