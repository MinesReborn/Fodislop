#nullable enable

using System;
using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

[TestFixture]
public class TerrainCellDataPackerTests
{
    private static TerrainVertex[] Quad(Vector2[] cornerUvs)
    {
        var quad = new TerrainVertex[4];
        for (int i = 0; i < 4; i++)
        {
            quad[i].Color = new Color32(12, 34, 56, 78);
            quad[i].UV0 = cornerUvs[i];
            quad[i].UV1 = new Vector4(0.125f, 0.5f, 0.0625f, 0.25f);
            quad[i].UV2 = new Vector4(0.015625f, 0.015625f, 4f, 2f);
            quad[i].UV3 = new Vector4(9999f, 39999f, 17f, 1f);
            quad[i].UV4 = new Vector4(2f, 1.5f, 6.283f, 0f);
            quad[i].UV5 = new Vector4(1f, 0.1f * i, 0.2f * i, 0f);
            quad[i].UV6 = new Vector4(16777215f, 127.25f, 7f, 0f);
        }

        return quad;
    }

    private static readonly Vector2[] _Identity =
    [
        new(0, 0), new(1, 0), new(1, 1), new(0, 1),
    ];

    [Test]
    public void SharedAttributesAreCopiedBitForBit()
    {
        TerrainVertex[] quad = Quad(_Identity);
        TerrainCellTexels texels = TerrainCellDataPacker.PackQuad(quad, 3);
        TerrainVertex v = quad[0];

        Assert.That(texels.Color, Is.EqualTo(v.Color));
        Assert.That(texels.AtlasRect, Is.EqualTo(new TerrainHalfTexel(v.UV1x, v.UV1y, v.UV1z, v.UV1w)));
        Assert.That(texels.TileSize, Is.EqualTo(new TerrainHalfTexel(v.UV2x, v.UV2y, v.UV2z, v.UV2w)));
        Assert.That(texels.Animation, Is.EqualTo(new TerrainHalfTexel(v.UV4x, v.UV4y, v.UV4z, v.UV4w)));
        Assert.That(texels.World, Is.EqualTo(v.UV3));
        Assert.That(texels.Glow, Is.EqualTo(v.UV6));
        Assert.That(TerrainCellDataPacker.UnpackAtlasIndex(texels.Meta), Is.EqualTo(3));
    }

    [Test]
    public void LargeWorldCoordinatesAndPackedColorsSurvive()
    {
        TerrainCellTexels texels = TerrainCellDataPacker.PackQuad(Quad(_Identity), 0);

        Assert.That(texels.World.x, Is.EqualTo(9999f));
        Assert.That(texels.World.y, Is.EqualTo(39999f));
        Assert.That(texels.Glow.x, Is.EqualTo(16777215f));
    }

    [Test]
    public void UndrawnQuadPacksAsZeroAtlas()
    {
        TerrainCellTexels texels = TerrainCellDataPacker.PackQuad(Quad(_Identity), -1);

        Assert.That(texels.Meta.r, Is.EqualTo(0));
        Assert.That(TerrainCellDataPacker.UnpackAtlasIndex(texels.Meta), Is.EqualTo(-1));
    }

    // Все восемь преобразований варианта из TerrainQuadBuilder: отражение по
    // x (0x40), по y (0x20), поворот (0x80) и их сочетания.
    [Test]
    public void EveryCornerTransformRoundTrips([NUnit.Framework.Range(0, 7)] int transform)
    {
        Vector2[] uv = (Vector2[])_Identity.Clone();
        if ((transform & 1) != 0)
        {
            (uv[0].x, uv[1].x) = (uv[1].x, uv[0].x);
            (uv[3].x, uv[2].x) = (uv[2].x, uv[3].x);
        }

        if ((transform & 2) != 0)
        {
            (uv[0].y, uv[3].y) = (uv[3].y, uv[0].y);
            (uv[1].y, uv[2].y) = (uv[2].y, uv[1].y);
        }

        if ((transform & 4) != 0)
        {
            Vector2 t = uv[0];
            uv[0] = uv[1];
            uv[1] = uv[2];
            uv[2] = uv[3];
            uv[3] = t;
        }

        byte bits = TerrainCellDataPacker.PackCornerUvs(Quad(uv));
        for (int corner = 0; corner < 4; corner++)
        {
            Assert.That(TerrainCellDataPacker.UnpackCornerUv(bits, corner), Is.EqualTo(uv[corner]), $"corner {corner}");
        }
    }

    [Test]
    public void TexelIndexPutsLayersOnSeparateRows()
    {
        const int width = 5;

        Assert.That(TerrainCellDataPacker.TexelIndex(0, 0, TerrainCellDataPacker.BackgroundLayer, width), Is.EqualTo(0));
        Assert.That(TerrainCellDataPacker.TexelIndex(0, 0, TerrainCellDataPacker.ForegroundLayer, width), Is.EqualTo(width));
        Assert.That(TerrainCellDataPacker.TexelIndex(4, 3, TerrainCellDataPacker.ForegroundLayer, width), Is.EqualTo((7 * width) + 4));
    }

    [Test]
    public void ShortQuadIsRejected()
    {
        Assert.Throws<ArgumentException>(() => TerrainCellDataPacker.PackQuad(new TerrainVertex[3], 0));
    }
}
