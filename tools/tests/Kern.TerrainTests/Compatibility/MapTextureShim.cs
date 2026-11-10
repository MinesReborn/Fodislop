#nullable enable

using System;
using UnityEngine;

namespace UnityEngine
{

public enum FilterMode
{
    Bilinear,
    Trilinear,
}

public enum TextureWrapMode
{
    Clamp,
}

public class Object
{
    public static void DestroyImmediate(Object? instance) => Destroy(instance);
    public static void Destroy(Object? instance)
    {
    }
}

public sealed class Texture2D : Object
{
    private readonly Color32[][] _mipPixels;
    public int width { get; }
    public int height { get; }

    public Texture2D(int width, int height, int mipCount)
    {
        this.width = width;
        this.height = height;
        _mipPixels = new Color32[mipCount][];
        for (int level = 0; level < mipCount; level++)
        {
            _mipPixels[level] = new Color32[checked(width * height)];
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
        }
    }

    public int mipmapCount => _mipPixels.Length;

    public void SetPixelData(Color32[] pixels, int mipLevel)
    {
        if (pixels.Length != _mipPixels[mipLevel].Length)
        {
            throw new ArgumentException("Mip pixel count does not match the texture level.", nameof(pixels));
        }

        Array.Copy(pixels, _mipPixels[mipLevel], pixels.Length);
    }

    public Color32[] GetPixels32(int mipLevel = 0) => (Color32[])_mipPixels[mipLevel].Clone();

    public void Apply(bool updateMipmaps, bool makeNoLongerReadable)
    {
    }
}

}

namespace Kern
{

public enum RuntimeTextureColorSpace
{
    Srgb,
    Linear,
}

public static class RuntimeTextureFactory
{
    public static Texture2D CreateRGBA32MipChain(
        int width,
        int height,
        string name,
        RuntimeTextureColorSpace colorSpace,
        FilterMode filterMode,
        TextureWrapMode wrapMode)
    {
        int mipCount = 1;
        int mipWidth = width;
        int mipHeight = height;
        while (mipWidth > 1 || mipHeight > 1)
        {
            mipWidth = Math.Max(1, mipWidth / 2);
            mipHeight = Math.Max(1, mipHeight / 2);
            mipCount++;
        }

        return new Texture2D(width, height, mipCount);
    }
}
}

namespace Kern.World
{
public class MapManager
{
    public virtual Color32 GetCellMinimapColor32(MinesServer.Data.CellType type) => default;
}
}
