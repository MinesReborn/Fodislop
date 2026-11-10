#nullable enable

using Kern.World;
using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;

using Kern.AssetPipeline.Loading;

namespace Kern.Tests.World;

// Reads the authored PNG through Unity's production texture decoder.
[TestFixture]
public sealed class TerrainDecalAtlasBoundsTests
{
    [TestCase("terrain-decals.png", 2f)]
    [TestCase("terrain-decals-rock.png", 0f)]
    public void AuthoredDecalsFitCellAtMaximumTranslation(string filename, float translationPixels)
    {
        byte[] bytes = System.IO.File.ReadAllBytes(System.IO.Path.Combine(
            Application.dataPath, "Textures", "Server", "World", "Terrain", "Decals", filename));
        Texture2D? atlas = RuntimeTextureFactory.DecodeEncodedImageToRGBA32NoMip(
            bytes, "DecalPaddingProof", RuntimeTextureColorSpace.Srgb,
            FilterMode.Point, TextureWrapMode.Clamp, makeNoLongerReadable: false);
        Assert.That(atlas, Is.Not.Null);
        try
        {
            Color32[] pixels = atlas!.GetPixels32();
            // Independent geometry oracle: reflection/quarter-turns preserve
            // radius. The smallest roundable silhouette is the isolated disk.
            const float RadiusPixels = 16.32f;
            for (int variant = 0; variant < 16; variant++)
            {
                for (int y = 0; y < 32; y++)
                {
                    for (int x = 0; x < 32; x++)
                    {
                        if (pixels[y * 512 + variant * 32 + x].a == 0)
                        {
                            continue;
                        }

                        float extentX = Mathf.Abs(x + 0.5f - 16f) + translationPixels;
                        float extentY = Mathf.Abs(y + 0.5f - 16f) + translationPixels;
                        Assert.That(Mathf.Max(extentX, extentY), Is.LessThan(16f),
                            $"{filename}, variant {variant}: translation clips the cell boundary");
                        Assert.That(extentX * extentX + extentY * extentY,
                            Is.LessThan(RadiusPixels * RadiusPixels),
                            $"{filename}, variant {variant}: translation clips the rounded silhouette");
                    }
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(atlas);
        }
    }

}
