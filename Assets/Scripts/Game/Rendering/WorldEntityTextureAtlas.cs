#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using UnityEngine;

namespace Fodinae.Game;

internal sealed class WorldEntityTextureAtlas : IDisposable
{
    private const int AtlasSize = 2048;
    private const int Padding = 1;
    private readonly Dictionary<Texture2D, Rect> _rects = [];
    private int _cursorX;
    private int _cursorY;
    private int _rowHeight;

    public WorldEntityTextureAtlas()
    {
        int atlasSize = Mathf.Min(AtlasSize, SystemInfo.maxTextureSize);
        Texture = RuntimeTextureFactory.CreateRgba32NoMip(
            atlasSize,
            atlasSize,
            "WorldEntityAtlas",
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp);

        // Свежая текстура содержит то, что лежало в этой памяти GPU.
        // Между записями атласа есть пиксель отступа, и он оставался
        // незаполненным: сосед по атласу подтекал каймой по краю спрайта.
        // У скина 32x32 полоска в пиксель теряется, у частицы в несколько
        // пикселей это заметная часть картинки.
        var transparent = new Color32[atlasSize * atlasSize];
        Texture.SetPixels32(transparent);
        Texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
    }

    public Texture2D Texture { get; private set; }

    public Rect GetRect(Texture2D texture)
    {
        if (!_rects.TryGetValue(texture, out Rect rect))
        {
            throw new InvalidOperationException(
                $"Texture '{texture.name}' was not registered in the world-entity atlas.");
        }

        return rect;
    }

    public void EnsureTexture(Texture2D texture)
    {
        if (_rects.ContainsKey(texture))
        {
            return;
        }

        if (!RuntimeTextureFactory.SupportsTexture2DGpuCopy)
        {
            throw new InvalidOperationException(
                "The active graphics API does not support GPU texture copies required by the world-entity atlas.");
        }

        if (texture.graphicsFormat != Texture.graphicsFormat)
        {
            throw new InvalidOperationException(
                $"Texture '{texture.name}' has graphics format {texture.graphicsFormat}; " +
                $"the canonical world-entity atlas requires {Texture.graphicsFormat}.");
        }

        int paddedWidth = texture.width + (Padding * 2);
        int paddedHeight = texture.height + (Padding * 2);
        if (paddedWidth > Texture.width || paddedHeight > Texture.height)
        {
            throw new InvalidOperationException(
                $"Texture '{texture.name}' ({texture.width}x{texture.height}) " +
                $"does not fit the {Texture.width}x{Texture.height} world-entity atlas.");
        }

        if (_cursorX + paddedWidth > Texture.width)
        {
            _cursorX = 0;
            _cursorY += _rowHeight;
            _rowHeight = 0;
        }

        if (_cursorY + paddedHeight > Texture.height)
        {
            throw new InvalidOperationException(
                $"World-entity atlas is full while registering texture '{texture.name}'.");
        }

        int destinationX = _cursorX + Padding;
        int destinationY = _cursorY + Padding;
        Graphics.CopyTexture(
            texture,
            0,
            0,
            0,
            0,
            texture.width,
            texture.height,
            Texture,
            0,
            0,
            destinationX,
            destinationY);

        _rects.Add(
            texture,
            new Rect(
                (float)destinationX / Texture.width,
                (float)destinationY / Texture.height,
                (float)texture.width / Texture.width,
                (float)texture.height / Texture.height));
        _cursorX += paddedWidth;
        _rowHeight = Mathf.Max(_rowHeight, paddedHeight);
    }

    public void Dispose()
    {
        if (Texture != null)
        {
            UnityEngine.Object.Destroy(Texture);
            Texture = null!;
        }

        _rects.Clear();
    }
}
