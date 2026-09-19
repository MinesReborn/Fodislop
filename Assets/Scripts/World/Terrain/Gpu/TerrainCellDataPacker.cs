#nullable enable

using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Kern.World.Terrain;

// Тексель RGBAHalf: четыре сырых half, как они лежат в вершине.
[StructLayout(LayoutKind.Sequential)]
public struct TerrainHalfTexel
{
    public ushort R;
    public ushort G;
    public ushort B;
    public ushort A;

    public TerrainHalfTexel(ushort r, ushort g, ushort b, ushort a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }
}

// Всё, что квад террейна хранит одинаково во всех четырёх вершинах.
public readonly record struct TerrainCellTexels(
    Color32 Color,
    Color32 Meta,
    TerrainHalfTexel AtlasRect,
    TerrainHalfTexel TileSize,
    TerrainHalfTexel Animation,
    Vector4 World,
    Vector4 Glow);

// Упаковка квада террейна в тексели данных клетки.
//
// Шесть из девяти атрибутов вершины (цвет, прямоугольник атласа, размер
// тайла, мировая клетка, анимация, свечение) одинаковы у всех четырёх вершин
// квада: TerrainQuadBuilder пишет их одним циклом. Им место в текстуре, по
// одному текселю на квад, а не в вершинах вчетверо. По-вершинно различаются
// только позиция со смещением искажения (оно общее для соседних квадов и
// лежит отдельной сеткой), угол UV (поворот и отражение варианта, 8 бит) и
// якорь, который выводится из тех же смещений.
//
// Поля half копируются сырыми байтами: шейдер читает ровно то, что читал
// из вершины, без второго округления.
public static class TerrainCellDataPacker
{
    public const int LayersPerCell = 2;
    public const int BackgroundLayer = 0;
    public const int ForegroundLayer = 1;

    public static int TexelIndex(int x, int y, int layer, int width) =>
        (((y * LayersPerCell) + layer) * width) + x;

    public static TerrainCellTexels PackQuad(ReadOnlySpan<TerrainVertex> quad, int atlasIndex)
    {
        if (quad.Length < 4)
        {
            throw new ArgumentException("A terrain quad has four vertices.", nameof(quad));
        }

        ref readonly TerrainVertex v = ref quad[0];
        byte drawn = atlasIndex < 0 ? (byte)0 : (byte)Math.Min(atlasIndex + 1, byte.MaxValue);
        return new TerrainCellTexels(
            v.Color,
            new Color32(drawn, PackCornerUvs(quad), 0, 0),
            new TerrainHalfTexel(v.UV1x, v.UV1y, v.UV1z, v.UV1w),
            new TerrainHalfTexel(v.UV2x, v.UV2y, v.UV2z, v.UV2w),
            new TerrainHalfTexel(v.UV4x, v.UV4y, v.UV4z, v.UV4w),
            v.UV3,
            v.UV6);
    }

    // Биты угла i: (2i) — u, (2i+1) — v. Углы UV квада всегда 0 или 1.
    public static byte PackCornerUvs(ReadOnlySpan<TerrainVertex> quad)
    {
        int bits = 0;
        for (int corner = 0; corner < 4; corner++)
        {
            bits |= HalfBit(quad[corner].UV0x) << (corner * 2);
            bits |= HalfBit(quad[corner].UV0y) << ((corner * 2) + 1);
        }

        return (byte)bits;
    }

    public static Vector2 UnpackCornerUv(byte bits, int corner) =>
        new((bits >> (corner * 2)) & 1, (bits >> ((corner * 2) + 1)) & 1);

    public static int UnpackAtlasIndex(Color32 meta) => meta.r - 1;

    private static int HalfBit(ushort half) => Mathf.HalfToFloat(half) > 0.5f ? 1 : 0;
}
