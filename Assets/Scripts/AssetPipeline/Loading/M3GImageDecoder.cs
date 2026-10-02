#nullable enable

using UnityEngine;

namespace Kern;

internal static class M3GImageDecoder
{
    public static Texture2D Decode(byte[] source)
    {
        M3DecodedImage image = new M3Decompressor().Decompress(source);
        Texture2D texture = RuntimeTextureFactory.CreateRGBA32NoMip(
            image.Width,
            image.Height,
            "M3G",
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp);
        texture.LoadRawTextureData(image.Rgba);
        texture.Apply(
            updateMipmaps: false,
            makeNoLongerReadable: RuntimeTextureFactory.SupportsTexture2DGPUCopy);
        return texture;
    }
}
