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
    Vector4 Glow,
    TerrainHalfTexel GeometryX,
    TerrainHalfTexel GeometryY);

// Упаковка квада террейна в тексели данных клетки.
//
// Общие данные клетки (цвет, прямоугольник атласа, размер тайла, мировая
// клетка, анимация, свечение и четыре угла геометрии) хранятся одним текселем
// на слой клетки. По-вершинно различается только угол UV (поворот и отражение
// варианта, 8 бит). Так геометрия и материалы обновляются одной dirty-операцией.
//
// Поля half копируются сырыми байтами: шейдер читает ровно то, что читал
// из вершины, без второго округления. GeometryX/GeometryY — канонические
// локальные координаты четырёх углов квада; они идут вместе с cell-data в
// одном dirty/snapshot contract. Для Organic четыре изгиба рёбер кодируются
// числом 1..625: младший байт в Meta.b, старшие биты в Meta.a от 128.
// У Classic Meta.a остаётся 255, у ровной клетки 0.
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
        int organicEdges = Mathf.RoundToInt(Mathf.HalfToFloat(v.UV5w));
        (byte organicLowByte, byte organicHighByte) =
            TerrainCellGeometry.PackOrganicEdgeMetadata(organicEdges);
        byte anchored = organicEdges > 0
            ? organicHighByte
            : v.UV5x != 0 ? byte.MaxValue : (byte)0;
        return new TerrainCellTexels(
            v.Color,
            new Color32(drawn, PackCornerUvs(quad), organicLowByte, anchored),
            new TerrainHalfTexel(v.UV1x, v.UV1y, v.UV1z, v.UV1w),
            new TerrainHalfTexel(v.UV2x, v.UV2y, v.UV2z, v.UV2w),
            new TerrainHalfTexel(v.UV4x, v.UV4y, v.UV4z, v.UV4w),
            v.UV3,
            v.UV6,
            new TerrainHalfTexel(quad[0].UV5y, quad[1].UV5y, quad[2].UV5y, quad[3].UV5y),
            new TerrainHalfTexel(quad[0].UV5z, quad[1].UV5z, quad[2].UV5z, quad[3].UV5z));
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

    // Positive IEEE half values are ordered by their bits. 0x3800 is 0.5,
    // 0x7C00 is +infinity; exclude NaNs just like a floating-point comparison.
    private static int HalfBit(ushort half) => half > 0x3800 && half <= 0x7C00 ? 1 : 0;
}
