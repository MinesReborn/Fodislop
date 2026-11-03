#nullable enable

using System;
using Kern.World.Terrain;
using MinesServer.Data;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

// Независимая расшифровка клетки, её восьми соседей и строк их типов по
// таблице битов из TerrainCellData.cs — так же, как её читает
// TerrainCellData.hlsl.
//
// Эталон — вершины того же квада, собранные TerrainQuadBuilder.FillQuad: до
// перехода на данные клетки шейдер получал ровно их. Совпадение проверяется
// по битам: half сравниваются как ushort, float — как uint. Исключение —
// углы геометрии: вершина несёт -0.0 там, где смещение получено как -0f, а
// узел в единицах 1/256 знака нуля не знает; для шейдера это одно число.
// Узлы оракул берёт из nodeAt (правило TerrainVertexDistortionCalculator):
// его HLSL-копию сверяет растровый шим на тех же мирах.
internal sealed class TerrainCellOracle
{
    private readonly Func<int, int, TerrainCell> _cellAt;
    private readonly Func<int, int, TerrainVertexOffset> _nodeAt;
    private readonly Func<uint, TerrainTypeRow> _typeOf;
    private readonly Func<int, int> _atlasSize;
    private readonly uint[] _tileDescriptors;
    private readonly uint _groundType;
    private readonly int _worldHeight;

    public TerrainCellOracle(
        Func<int, int, TerrainCell> cellAt,
        Func<int, int, TerrainVertexOffset> nodeAt,
        Func<uint, TerrainTypeRow> typeOf,
        Func<int, int> atlasSize,
        uint[] tileDescriptors,
        uint groundType,
        int worldHeight)
    {
        _cellAt = cellAt;
        _nodeAt = nodeAt;
        _typeOf = typeOf;
        _atlasSize = atlasSize;
        _tileDescriptors = tileDescriptors;
        _groundType = groundType;
        _worldHeight = worldHeight;
    }

    // L, BL, B, BR, R, TR, T, TL — порядок битов маски автотайла.
    private static readonly int[] s_dx = new[] {
-1, -1, 0, 1, 1, 1, 0, -1
 };
    private static readonly int[] s_dy = new[] {
0, -1, -1, -1, 0, 1, 1, 1
 };
    private const int Left = 0, BottomLeft = 1, Bottom = 2, BottomRight = 3, Right = 4, TopRight = 5, Top = 6, TopLeft = 7;

    public void AssertMatchesQuad(
        ReadOnlySpan<TerrainVertex> quad,
        TerrainQuadResult result,
        int gridX,
        int unityY,
        int layer,
        string where)
    {
        bool foreground = layer == 1;
        uint cell = _cellAt(gridX, unityY).Bits;
        uint fgType = cell & 0xFFu;
        var nb = new uint[8];
        var nbType = new uint[8];
        for (int i = 0; i < 8; i++)
        {
            nb[i] = _cellAt(gridX + s_dx[i], unityY + s_dy[i]).Bits;
            nbType[i] = nb[i] & 0xFFu;
        }

        uint fgLook = _typeOf(fgType).Look;
        bool fgEmpty = Bit(fgLook, 3);
        bool fgCause = Outline(fgLook) == 1;

        uint typeIndex = 0;
        if (fgType != 0)
        {
            if (foreground)
            {
                typeIndex = fgEmpty ? 0u : fgType;
            }
            else
            {
                typeIndex = Under(fgType);
            }
        }

        float[] cornersX = new[] {
0f, 1f, 1f, 0f
 };
        float[] cornersY = new[] {
0f, 0f, 1f, 1f
 };
        if (fgCause && fgType != 0)
        {
            (int Dx, int Dy)[] owners = new[] {
(0, 0), (1, 0), (1, 1), (0, 1)
 };
            for (int corner = 0; corner < 4; corner++)
            {
                TerrainVertexOffset node = _nodeAt(gridX + owners[corner].Dx, unityY + owners[corner].Dy);
                cornersX[corner] += node.XSteps / TerrainVertexOffset.GridSize;
                cornersY[corner] += node.YSteps / TerrainVertexOffset.GridSize;
            }
        }

        bool fgAnchored =
            cornersX[0] != 0f || cornersX[1] != 1f || cornersX[2] != 1f || cornersX[3] != 0f ||
            cornersY[0] != 0f || cornersY[1] != 0f || cornersY[2] != 1f || cornersY[3] != 1f;

        if (typeIndex != 0 && !foreground)
        {
            bool occluded = !fgEmpty &&
                HasRect(_typeOf(fgType)) && Outline(fgLook) != 2 &&
                Bit(fgLook, 4);
            if (occluded && fgAnchored)
            {
                for (int i = 0; i < 8; i++)
                {
                    occluded &= OpaqueMass(nbType[i]);
                }
            }

            typeIndex = occluded ? 0u : typeIndex;
        }

        TerrainTypeRow type = _typeOf(typeIndex);
        int atlasIndex = typeIndex != 0 ? (int)(type.Look & 7u) : -1;
        float size = typeIndex != 0 ? _atlasSize(atlasIndex) : 1f;
        Assert.That(atlasIndex, Is.EqualTo(result.AtlasIndex), $"{where}: слот атласа");
        if (typeIndex == 0)
        {
            return;
        }

        uint look = type.Look;

        int serverY = _worldHeight - 1 - unityY;

        var layerTypes = new uint[8];
        for (int i = 0; i < 8; i++)
        {
            layerTypes[i] = foreground ? nbType[i] : Under(nbType[i]);
        }

        uint descriptor = 0;
        bool tiling = TileCode(type) != 0;
        if (tiling)
        {
            uint mask = 0;
            for (int i = 0; i < 8; i++)
            {
                TerrainTypeRow other = _typeOf(layerTypes[i]);
                if (TileCode(other) != 0 && TileCode(other) == TileCode(type))
                {
                    mask |= 1u << i;
                }
            }

            descriptor = (_tileDescriptors[mask >> 2] >> (int)((mask & 3u) * 8u)) & 0xFFu;
        }

        if (foreground && Outline(look) == 4)
        {
            uint side = (Corner(nbType[Left]) ? 1u : 0u) | (Corner(nbType[Right]) ? 2u : 0u) |
                (Corner(nbType[Top]) ? 4u : 0u) | (Corner(nbType[Bottom]) ? 8u : 0u);
            if (side != 0)
            {
                descriptor = WallVariant(descriptor, side);
                tiling = true;
            }
        }

        uint foregroundSides = (IsForeground(nbType[Top]) ? 1u : 0u) | (IsForeground(nbType[Left]) ? 2u : 0u) |
            (IsForeground(nbType[Bottom]) ? 4u : 0u) | (IsForeground(nbType[Right]) ? 8u : 0u);
        uint glow = (type.SpeedGlowTile >> 16) & 0xFFu;
        uint lightingFlags = foregroundSides |
            (glow != 0 ? 16u : 0u) |
            (foreground && !Bit(look, 3) ? 32u : 0u);
        uint contour = 0;
        if (foreground)
        {
            contour = Outline(look) == 2 ? 1u : 0u;
            if (Rim(type) != 0)
            {
                bool t = Same(Rim(type), nbType[Top]), l = Same(Rim(type), nbType[Left]);
                bool b = Same(Rim(type), nbType[Bottom]), r = Same(Rim(type), nbType[Right]);
                uint rim = (t ? 1u : 0u) | (l ? 2u : 0u) | (b ? 4u : 0u) | (r ? 8u : 0u);
                uint corners = (b && l && !Same(Rim(type), nbType[BottomLeft]) ? 1u : 0u) |
                    (b && r && !Same(Rim(type), nbType[BottomRight]) ? 2u : 0u) |
                    (t && r && !Same(Rim(type), nbType[TopRight]) ? 4u : 0u) |
                    (t && l && !Same(Rim(type), nbType[TopLeft]) ? 8u : 0u);
                contour += ((rim + 1) * 32) + (corners << 10);
            }
        }

        // Свечение — байт 0..255, доля после масштаба — четверть.
        float packedFlags = lightingFlags + glow * (1f / 255f) * 0.25f;
        // Размер тайла и высота кадра не хранятся: 32 текселя на размер атласа
        // слота и высота прямоугольника на размер тайла.
        float tile = 32f / _atlasSize(atlasIndex);
        // Прямоугольник — пиксели по 12 бит, в UV делятся на размер атласа.
        var rect = new Vector4(
            (type.AtlasXY & 0xFFFu) / size, ((type.AtlasXY >> 12) & 0xFFFu) / size,
            (type.AtlasWH & 0xFFFu) / size, ((type.AtlasWH >> 12) & 0xFFFu) / size);
        float frameHeight = rect.w > 0f ? rect.w / tile : 1f;
        uint decalTextureIndex = (look >> 23) & 0xFFu;
        float expectedDecal = Decal(decalTextureIndex, gridX, serverY);

        bool anchored = foreground && fgAnchored;
        if (!foreground)
        {
            cornersX = new[] { 0f, 1f, 1f, 0f };
            cornersY = new[] { 0f, 0f, 1f, 1f };
        }

        uint uvBits = CornerUvBits(descriptor, tiling);
        uint animation = (look >> TerrainCellFormat.TypeAnimationTypeShift) & 0xFu;
        float speed = Mathf.HalfToFloat((ushort)(type.SpeedGlowTile & 0xFFFFu));
        float phase = Phase(
            (look >> TerrainCellFormat.TypeAnimationPaletteShift) & TerrainCellFormat.TypeAnimationPaletteMask,
            HasRect(type),
            animation == (uint)CellAnimationType.Blinking,
            animation == (uint)CellAnimationType.Faceted,
            gridX,
            serverY);
        for (int corner = 0; corner < 4; corner++)
        {
            TerrainVertex v = quad[corner];
            string at = $"{where}, угол {corner}";

            Assert.That(v.UV0x, Is.EqualTo(TerrainVertex.H((uvBits >> (corner * 2)) & 1u)), $"{at}: u");
            Assert.That(v.UV0y, Is.EqualTo(TerrainVertex.H((uvBits >> ((corner * 2) + 1)) & 1u)), $"{at}: v");

            AssertFloat(v.UV1.x, rect.x, $"{at}: атлас x");
            AssertFloat(v.UV1.y, rect.y, $"{at}: атлас y");
            AssertFloat(v.UV1.z, rect.z, $"{at}: атлас z");
            AssertFloat(v.UV1.w, rect.w, $"{at}: атлас w");

            Assert.That(v.UV2x, Is.EqualTo(TerrainVertex.H(tile)), $"{at}: тайл x");
            Assert.That(v.UV2y, Is.EqualTo(TerrainVertex.H(tile)), $"{at}: тайл y");
            Assert.That(v.UV2z, Is.EqualTo(TerrainVertex.H(type.AtlasXY >> 24)), $"{at}: кадры");
            Assert.That(v.UV2w, Is.EqualTo(TerrainVertex.H(frameHeight)), $"{at}: высота кадра");

            AssertFloat(v.UV3.x, gridX, $"{at}: мировой x");
            AssertFloat(v.UV3.y, serverY, $"{at}: серверный y");
            AssertFloat(v.UV3.z, (descriptor & 31u) | (Bit(look, 6) ? 32u : 0u), $"{at}: колонка");
            AssertFloat(v.UV3.w, tiling ? 1f : 0f, $"{at}: автотайл");

            Assert.That(v.UV4x, Is.EqualTo(TerrainVertex.H(animation)), $"{at}: тип анимации");
            Assert.That(v.UV4y, Is.EqualTo(TerrainVertex.H(speed)), $"{at}: скорость");
            Assert.That(v.UV4z, Is.EqualTo(TerrainVertex.H(phase)), $"{at}: фаза");
            Assert.That(v.UV4w, Is.EqualTo(TerrainVertex.H(0f)), $"{at}: резерв UV4w");

            Assert.That(v.UV5x, Is.EqualTo(TerrainVertex.H(anchored ? 1f : 0f)), $"{at}: якорь");
            Assert.That(Mathf.HalfToFloat(v.UV5y), Is.EqualTo(cornersX[corner]), $"{at}: угол x");
            Assert.That(Mathf.HalfToFloat(v.UV5z), Is.EqualTo(cornersY[corner]), $"{at}: угол y");
            Assert.That(v.UV5w, Is.EqualTo(TerrainVertex.H(0f)), $"{at}: резерв");

            AssertFloat(v.UV6.x, expectedDecal != 0f ? decalTextureIndex : 0f, $"{at}: атлас декали");
            AssertFloat(v.UV6.y, packedFlags, $"{at}: флаги света");
            AssertFloat(v.UV6.z, contour, $"{at}: контур");
            AssertFloat(v.UV6.w, expectedDecal, $"{at}: декаль");
        }
    }

    // Раскладка строки — своя расшифровка, независимая от шейдера:
    // look: слот 0-2, пол 3, opaqueOwn 4, opaqueAny 5, по миру 6,
    // контур 7-9, анимация 10-11, эффект поверхности 12-13, палитра 14-16,
    // индекс текстуры декали 23-30; speedGlowTile:
    // скорость half 0-15, свечение 16-23, тайлгруппа + 1 24-31; масса каймы —
    // старший байт atlasWH.
    private static bool Bit(uint word, int bit) => ((word >> bit) & 1u) != 0;

    // Фон (бит 3) лежит сам на себе, передний план — на подложке.
    private uint Under(uint type) => Bit(_typeOf(type).Look, 3) ? type : _groundType;

    // Контур: 0 жёсткий, 1 волнистый, 2 капля, 3 податливый, 4 стена, 5 угол, 6 дверь.
    private static uint Outline(uint look) => (look >> 7) & 7u;

    private static uint TileCode(TerrainTypeRow row) => row.SpeedGlowTile >> 24;

    private static uint Rim(TerrainTypeRow row) => row.AtlasWH >> 24;

    private static bool HasRect(TerrainTypeRow row) => (row.AtlasWH & 0xFFFu) != 0;

    private bool OpaqueMass(uint type) =>
        type != 0 &&
        IsForeground(type) &&
        Outline(_typeOf(type).Look) == 1 &&
        Bit(_typeOf(type).Look, 5);

    private bool Corner(uint type) => IsForeground(type) && Outline(_typeOf(type).Look) == 5;

    private bool IsForeground(uint type) => !Bit(_typeOf(type).Look, 3);

    private bool Same(uint ownRim, uint otherType)
    {
        if (!IsForeground(otherType))
        {
            return false;
        }

        uint otherGroup = Rim(_typeOf(otherType));
        if (otherGroup == 0)
        {
            return false;
        }

        return ownRim == otherGroup;
    }

    private static uint WallVariant(uint descriptor, uint side)
    {
        bool hasLeft = (side & 1) != 0, hasRight = (side & 2) != 0, hasTop = (side & 4) != 0, hasBottom = (side & 8) != 0;
        uint count = (hasLeft ? 1u : 0u) + (hasRight ? 1u : 0u) + (hasTop ? 1u : 0u) + (hasBottom ? 1u : 0u);
        uint transforms = descriptor & 0xE0u;
        if (count == 1 && (hasRight || hasBottom))
        {
            transforms ^= 0x40u;
        }

        if (count >= 2 && !hasLeft && !hasRight)
        {
            transforms ^= 0x80u;
        }

        return transforms | ((8u + Math.Min(count, 2u)) & 0x1Fu);
    }

    private static float Phase(uint palette, bool hasRect, bool blinking, bool faceted, int gridX, int serverY)
    {
        if (!hasRect)
        {
            return palette;
        }

        uint seed = unchecked(((uint)gridX * 374761397u) + ((uint)serverY * 668265263u));
        seed = unchecked((seed ^ (seed >> 13)) * 1274126177u);
        seed ^= seed >> 16;
        if (blinking)
        {
            return (seed % 6283) / 1000f;
        }

        return faceted ? (seed & 0xFFFF) / 65536f : palette;
    }

    private static uint CornerUvBits(uint descriptor, bool tiling)
    {
        uint transforms = tiling ? descriptor : 0u;
        uint flipU = (transforms >> 6) & 1u;
        uint flipV = (transforms >> 5) & 1u;
        uint turn = (transforms >> 7) & 1u;
        uint bits = 0;
        for (uint corner = 0; corner < 4; corner++)
        {
            uint source = (corner + turn) & 3u;
            uint u = (source is 1 or 2 ? 1u : 0u) ^ flipU;
            uint v = (source >= 2 ? 1u : 0u) ^ flipV;
            bits |= (u | (v << 1)) << (int)(corner * 2);
        }

        return bits;
    }

    private static float Decal(uint decalTextureIndex, int worldX, int serverY)
    {
        if (decalTextureIndex == 0)
        {
            return 0f;
        }

        uint hash = unchecked((uint)worldX * 374761393u);
        hash = unchecked(hash + ((uint)serverY * 668265263u));
        hash ^= unchecked(decalTextureIndex * 2246822519u);
        hash = unchecked((hash ^ (hash >> 13)) * 1274126177u);
        hash ^= hash >> 16;
        if (hash % 100u >= 20u)
        {
            return 0f;
        }

        uint packed = 1u + (hash % 16u) +
            (((hash >> 8) & 3u) << 4) +
            (((hash >> 10) & 1u) << 6) +
            (((hash >> 12) & 3u) << 7) +
            (((hash >> 14) & 3u) << 9);
        return packed;
    }

    private static void AssertFloat(float actual, float expected, string what) =>
        Assert.That(
            BitConverter.SingleToInt32Bits(actual),
            Is.EqualTo(BitConverter.SingleToInt32Bits(expected)),
            $"{what}: {actual} ≠ {expected}");
}
