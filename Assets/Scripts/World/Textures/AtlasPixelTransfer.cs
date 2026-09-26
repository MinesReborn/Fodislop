#nullable enable

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World;

/// <summary>Owns atlas CPU pixel storage and CPU/GPU transfer mechanics.</summary>
internal sealed class AtlasPixelTransfer
{
    private readonly int _size;
    private readonly int _padding;
    private Color32[]? _pixels;

    public AtlasPixelTransfer(int size, int padding)
    {
        _size = size;
        _padding = padding;
    }

    public void Apply(
        Texture2D atlasTexture,
        List<(CellType type, Texture2D texture, Rectangle rect)> dirtyTextures)
    {
        if (dirtyTextures.Count > 0 && RuntimeTextureFactory.SupportsTexture2DGpuCopy)
        {
            foreach (var (_, texture, rect) in dirtyTextures)
            {
                UploadGpuTexture(atlasTexture, texture, rect);
            }
        }
        else if (dirtyTextures.Count > 0)
        {
            EnsurePixelBuffer();
            foreach (var (_, texture, rect) in dirtyTextures)
            {
                CopyPixelsToAtlasArray(texture.GetPixels32(), texture.width, texture.height, rect);
            }

            atlasTexture.SetPixels32(_pixels!);
            atlasTexture.Apply(false, false);
        }
    }

    private void EnsurePixelBuffer()
    {
        _pixels ??= new Color32[_size * _size];
    }

    private async UniTask CopyTexturesToAtlas(
        Texture2D? atlasTexture,
        List<(Texture2D texture, Rectangle rect)> textures)
    {
        if (RuntimeTextureFactory.SupportsTexture2DGpuCopy)
        {
            await UniTask.SwitchToMainThread();
            if (atlasTexture != null)
            {
                foreach (var (texture, rect) in textures)
                {
                    UploadGpuTexture(atlasTexture, texture, rect);
                }
            }

            return;
        }

        const int BATCH_SIZE = 10;
        EnsurePixelBuffer();

        for (int i = 0; i < textures.Count; i += BATCH_SIZE)
        {
            int batchEnd = Math.Min(i + BATCH_SIZE, textures.Count);
            var pixelDataList = new List<(Color32[] pixels, int width, int height, Rectangle rect)>(batchEnd - i);

            for (int textureIndex = i; textureIndex < batchEnd; textureIndex++)
            {
                var (tex, rect) = textures[textureIndex];
                if (tex != null)
                {
                    pixelDataList.Add((tex.GetPixels32(), tex.width, tex.height, rect));
                }
            }

            await UniTask.SwitchToThreadPool();
            await UniTask.SwitchToThreadPool();

            foreach (var data in pixelDataList)
            {
                CopyPixelsToAtlasArray(data.pixels, data.width, data.height, data.rect);
            }

            await UniTask.SwitchToMainThread();
        }

        if (atlasTexture != null && _pixels != null)
        {
            atlasTexture.SetPixels32(_pixels);
            atlasTexture.Apply();
        }
    }

    private void CopyPixelsToAtlasArray(Color32[] sourcePixels, int width, int height, Rectangle destination)
    {
        if (_pixels == null)
        {
            throw new InvalidOperationException(
                $"CPU pixel storage is unavailable for {_size}x{_size} atlas.");
        }

        if (sourcePixels.Length != checked(width * height))
        {
            throw new InvalidOperationException(
                $"Source pixel count {sourcePixels.Length} does not match " +
                $"the declared texture size {width}x{height}.");
        }

        if (width != destination.Width || height != destination.Height ||
            destination.X < 0 || destination.Y < 0 ||
            destination.X + width > _size || destination.Y + height > _size)
        {
            throw new InvalidOperationException(
                $"Texture {width}x{height} cannot be copied into atlas rectangle " +
                $"({destination.X}, {destination.Y}, " +
                $"{destination.Width}, {destination.Height}) in {_size}x{_size} atlas.");
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int sourceIndex = (y * width) + x;
                int destX = destination.X + x;
                int destY = destination.Y + y;
                int destIndex = (destY * _size) + destX;
                _pixels[destIndex] = sourcePixels[sourceIndex];
            }
        }

        // Extrude the source edge into every reserved gutter. Smooth atlas
        // sampling can approach a region from any direction; padding only on
        // the right and bottom still lets the left/top edge blend with the
        // neighbouring region or the transparent atlas clear color.
        for (int y = 0; y < height; y++)
        {
            Color32 leftEdge = sourcePixels[y * width];
            Color32 rightEdge = sourcePixels[(y * width) + (width - 1)];
            for (int padding = 1; padding <= _padding; padding++)
            {
                int row = (destination.Y + y) * _size;
                _pixels[row + destination.X - padding] = leftEdge;
                _pixels[row + destination.X + width + padding - 1] = rightEdge;
            }
        }

        for (int padding = 1; padding <= _padding; padding++)
        {
            int topRow = (destination.Y - padding) * _size;
            int bottomRow = (destination.Y + height + padding - 1) * _size;
            for (int x = -_padding; x < width + _padding; x++)
            {
                int sourceX = Math.Clamp(x, 0, width - 1);
                _pixels[topRow + destination.X + x] = sourcePixels[sourceX];
                _pixels[bottomRow + destination.X + x] =
                    sourcePixels[((height - 1) * width) + sourceX];
            }
        }
    }

    private void UploadGpuTexture(Texture2D atlasTexture, Texture2D source, Rectangle destination)
    {
        ValidateGpuCopySource(atlasTexture, source, destination);
        Graphics.CopyTexture(
            source, 0, 0, 0, 0, source.width, source.height,
            atlasTexture, 0, 0, destination.X, destination.Y);

        for (int padding = 1; padding <= _padding; padding++)
        {
            Graphics.CopyTexture(
                source, 0, 0, 0, 0, 1, source.height,
                atlasTexture, 0, 0,
                destination.X - padding,
                destination.Y);

            Graphics.CopyTexture(
                source, 0, 0, source.width - 1, 0, 1, source.height,
                atlasTexture, 0, 0,
                destination.X + source.width + padding - 1,
                destination.Y);

            Graphics.CopyTexture(
                source, 0, 0, 0, 0, source.width, 1,
                atlasTexture, 0, 0,
                destination.X,
                destination.Y - padding);

            Graphics.CopyTexture(
                source, 0, 0, 0, source.height - 1, source.width, 1,
                atlasTexture, 0, 0,
                destination.X,
                destination.Y + source.height + padding - 1);

            Graphics.CopyTexture(
                source, 0, 0, 0, 0, 1, 1,
                atlasTexture, 0, 0,
                destination.X - padding,
                destination.Y - padding);

            Graphics.CopyTexture(
                source, 0, 0, source.width - 1, 0, 1, 1,
                atlasTexture, 0, 0,
                destination.X + source.width + padding - 1,
                destination.Y - padding);

            Graphics.CopyTexture(
                source, 0, 0, 0, source.height - 1, 1, 1,
                atlasTexture, 0, 0,
                destination.X - padding,
                destination.Y + source.height + padding - 1);

            Graphics.CopyTexture(
                source, 0, 0, source.width - 1, source.height - 1, 1, 1,
                atlasTexture, 0, 0,
                destination.X + source.width + padding - 1,
                destination.Y + source.height + padding - 1);
        }
    }

    private static void ValidateGpuCopySource(
        Texture2D atlasTexture,
        Texture2D source,
        Rectangle destination)
    {
        if (source.width != destination.Width || source.height != destination.Height)
        {
            throw new InvalidOperationException(
                $"Terrain texture '{source.name}' is {source.width}x{source.height}, " +
                $"but its reserved atlas rectangle is " +
                $"{destination.Width}x{destination.Height}.");
        }

        if (source.graphicsFormat != atlasTexture.graphicsFormat)
        {
            throw new InvalidOperationException(
                $"Terrain texture '{source.name}' uses GPU format " +
                $"{source.graphicsFormat}, but atlas '{atlasTexture.name}' uses " +
                $"{atlasTexture.graphicsFormat}. Runtime image decoding must " +
                "canonicalize terrain textures before Graphics.CopyTexture.");
        }
    }
}
