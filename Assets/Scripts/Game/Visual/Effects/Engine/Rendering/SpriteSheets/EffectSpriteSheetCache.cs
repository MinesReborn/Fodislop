#nullable enable

using System;
using System.Collections.Generic;
using Kern.AssetPipeline.Loading;
using Kern.World;
using UnityEngine;

namespace Kern.Game.Visual.Effects.Engine;

/// <summary>Loads authored sprite sheets once and slices them into row-major animation frames.</summary>
public sealed class EffectSpriteSheetCache : IDisposable
{
    private readonly Dictionary<(string Resource, int Columns, int Rows), Sprite[]> _framesBySheet = new();
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.Ordinal);
    private bool _disposed;

    public Sprite GetSprite(string spriteSheet, int columns, int rows, int frameIndex)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(EffectSpriteSheetCache));
        }

        if (string.IsNullOrWhiteSpace(spriteSheet))
        {
            throw new ArgumentException("An effect sprite sheet path is required.", nameof(spriteSheet));
        }

        if (columns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columns), columns, "Sheet columns must be positive.");
        }

        if (rows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rows), rows, "Sheet rows must be positive.");
        }

        if (!_framesBySheet.TryGetValue((spriteSheet, columns, rows), out Sprite[]? frames))
        {
            frames = BuildFrames(spriteSheet, columns, rows);
            _framesBySheet.Add((spriteSheet, columns, rows), frames);
        }

        if ((uint)frameIndex >= (uint)frames.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameIndex), frameIndex,
                $"Sheet '{spriteSheet}' has {frames.Length} frames.");
        }

        return frames[frameIndex];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Sprite[] frames in _framesBySheet.Values)
        {
            foreach (Sprite frame in frames)
            {
                DestroyObject(frame);
            }
        }

        foreach (Texture2D texture in _textures.Values)
        {
            DestroyObject(texture);
        }

        _framesBySheet.Clear();
        _textures.Clear();
    }

    private Sprite[] BuildFrames(string resource, int columns, int rows)
    {
        Texture2D texture = LoadTexture(resource);
        if (texture.width % columns != 0 || texture.height % rows != 0)
        {
            throw new InvalidOperationException(
                $"Effect sprite sheet '{resource}' is {texture.width}x{texture.height} and does not divide " +
                $"into {columns}x{rows} cells.");
        }

        int cellWidth = texture.width / columns;
        int cellHeight = texture.height / rows;
        var frames = new Sprite[columns * rows];
        for (int frameIndex = 0; frameIndex < frames.Length; frameIndex++)
        {
            int column = frameIndex % columns;
            int rowFromTop = frameIndex / columns;
            var rect = new Rect(
                column * cellWidth,
                (rows - 1 - rowFromTop) * cellHeight,
                cellWidth,
                cellHeight);
            Sprite sprite = Sprite.Create(
                texture,
                rect,
                new Vector2(0.5f, 0.5f),
                RenderingConstants.PIXELS_PER_UNIT,
                extrude: 0,
                SpriteMeshType.FullRect);
            sprite.name = $"{resource}[{frameIndex}]";
            frames[frameIndex] = sprite;
        }

        return frames;
    }

    private Texture2D LoadTexture(string resource)
    {
        if (_textures.TryGetValue(resource, out Texture2D? cached))
        {
            return cached;
        }

        TextAsset image = Resources.Load<TextAsset>(resource)
            ?? throw new InvalidOperationException($"Missing effect sprite sheet: {resource}.");
        Texture2D texture = RuntimeTextureFactory.DecodeEncodedImageToRGBA32NoMip(
            image.bytes,
            resource,
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp,
            makeNoLongerReadable: false)
            ?? throw new InvalidOperationException($"Cannot decode effect sprite sheet: {resource}.");
        _textures.Add(resource, texture);
        return texture;
    }

    private static void DestroyObject(UnityEngine.Object value)
    {
        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(value);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
