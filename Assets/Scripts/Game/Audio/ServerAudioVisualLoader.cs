#nullable enable

using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World;
using UnityEngine;

namespace Kern.Game;

// Чем оказался визуальный эффект серверного события: набором кадров,
// одиночной картинкой или данными Effekseer. Пустое значение означает, что
// эффекта нет — это законный случай, а не ошибка.
internal readonly struct ServerAudioVisual
{
    public Sprite[]? Frames { get; init; }

    public float FrameDuration { get; init; }

    // Спрайт создан здесь и принадлежит вызывающему: он его и освобождает.
    public Sprite? StaticSprite { get; init; }

    public byte[]? EffectBytes { get; init; }
}

// Поиск визуального эффекта по имени: кадры, картинка, данные Effekseer —
// в этом порядке.
//
// Отдельный тип, потому что это работа с хранилищем ассетов, а не поведение
// события. Событие получает результат и решает, чем ему стать; раньше оба
// занятия лежали в одном методе, и разобрать, где кончается загрузка и
// начинается проигрывание, было нельзя.
internal sealed class ServerAudioVisualLoader
{
    private readonly IAssetLoader _assetLoader;

    public ServerAudioVisualLoader(IAssetLoader assetLoader)
    {
        _assetLoader = assetLoader ?? throw new ArgumentNullException(nameof(assetLoader));
    }

    public async UniTask<ServerAudioVisual> LoadAsync(string visualEffectName, CancellationToken token)
    {
        string filename = $"VFX/{visualEffectName.ToLowerInvariant()}";
        byte[]? bytes = await _assetLoader.GetAssetBytesAsync(filename, token);
        if (token.IsCancellationRequested || bytes == null || bytes.Length == 0)
        {
            return default;
        }

        AnimationContainerDecoder.ContainerType containerType =
            AnimationContainerDecoder.DetectType(bytes);
        if (containerType is AnimationContainerDecoder.ContainerType.WebP)
        {
            AnimatedSpriteData animData = await _assetLoader.GetAnimatedSpritesAsync(filename, token);
            if (token.IsCancellationRequested)
            {
                return default;
            }

            if (animData.Frames != null && animData.Frames.Length > 0)
            {
                return new ServerAudioVisual
                {
                    Frames = animData.Frames,
                    FrameDuration = animData.FrameDuration,
                };
            }
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
                return new ServerAudioVisual
                {
                    StaticSprite = Sprite.Create(
                        texture,
                        new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f),
                        RenderingConstants.PIXELS_PER_UNIT),
                };
            }
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or InvalidOperationException)
        {
            // Non-image VFX payloads are passed to the Effekseer loader below.
        }

        return new ServerAudioVisual { EffectBytes = bytes };
    }
}
