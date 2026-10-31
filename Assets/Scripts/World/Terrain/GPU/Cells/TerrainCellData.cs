#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World;
using Kern.World.Textures;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

// Упаковка данных террейна для GPU. Раскладка — только в
// Assets/Shaders/Terrain/Cells/Format/TerrainCellFormat.hlsl (источник правды); здесь —
// её зеркало для записи и правила «тип → строка».

/// <summary>Клетка на GPU: её тип (Cell в TerrainCellFormat.hlsl).</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct TerrainCell(byte Bits);

/// <summary>Строка таблицы типов на GPU.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct TerrainTypeRow(
    uint AtlasXY,
    uint AtlasWH,
    uint Look,
    uint SpeedGlowTile);

/// <summary>Строка типа в развёрнутом виде: тип из cells.json и факты его текстуры.</summary>
///
/// Одно значение кормит строку таблицы и эталон вида в тестах: разойтись им
/// негде, потому что считаются одной функцией
/// (TerrainCellPacker.ResolveTypeFields).
internal readonly record struct TerrainTypeFields(
    CellType Type,
    BlockDefinition Block,
    // Текстура.
    int Slot,
    Vector4 AtlasRect,
    float TileSize,
    int FrameCount,
    float FrameHeightTiles,
    bool OpaqueOwn,
    bool OpaqueAny,
    // Сервер.
    bool HasTileGroup,
    int TileGroupId,
    byte DecalTextureIndex = 0)
{
    public bool IsBackground => TerrainCellData.IsBackground(Block);

    public bool HasAtlasRect => AtlasRect.z >= 0.0001f;
}

public static class TerrainCellData
{
    // Квадов на клетку: фон и передний план (меш идентификаторов).
    public const int LayersPerCell = 2;
    public const int BackgroundLayer = 0;
    public const int ForegroundLayer = 1;
    public const int TypeCount = 256;

    // Кайма кольца вокруг окна: соседи краевых клеток.
    public const int RingMargin = 1;

    // Единица смещения узла: 1/256 клетки. Узел считается в ней целыми
    // числами на CPU и GPU одинаково (TerrainVertexDistortionCalculator).
    public const int GeometryUnitsPerCell = 256;

    public const int DistortionVectorCount = 4;


    public static TerrainCell PackCell(CellType type) => new((byte)type);

    // Фон (drawLayer Background или Underlay) лежит сам на себе; под передним
    // планом — подложка
    // (TerrainTypeIsBackground, TerrainTypeUnder в шейдере).
    public static bool IsBackground(in BlockDefinition block) => block.DrawLayer != CellDrawLayer.Foreground;

    public static CellType UnderOf(CellType type) =>
        IsBackground(BlockRegistry.Get(type)) ? type : BlockRegistry.UnderlayType;

    public static CellType TypeOf(TerrainCell cell) => (CellType)cell.Bits;

    internal static TerrainTypeRow PackType(in TerrainTypeFields fields)
    {
        BlockDefinition block = fields.Block;
        if ((uint)fields.Slot > TerrainCellFormat.TypeSlotMask ||
            (uint)fields.TileGroupId > 254u ||
            (uint)fields.FrameCount > byte.MaxValue ||
            block.AnimationPalette > TerrainCellFormat.TypeAnimationPaletteMask)
        {
            throw new ArgumentOutOfRangeException(nameof(fields));
        }

        // Прямоугольник — целые пиксели атласа: размер атласа = 32 / размер тайла.
        float atlasSize = fields.TileSize > 0f ? TerrainCellFormat.CellTexels / fields.TileSize : 0f;
        int x = Mathf.RoundToInt(fields.AtlasRect.x * atlasSize);
        int y = Mathf.RoundToInt(fields.AtlasRect.y * atlasSize);
        int w = Mathf.RoundToInt(fields.AtlasRect.z * atlasSize);
        int h = Mathf.RoundToInt(fields.AtlasRect.w * atlasSize);
        if ((uint)x > TerrainCellFormat.TypePixelMask || (uint)y > TerrainCellFormat.TypePixelMask ||
            (uint)w > TerrainCellFormat.TypePixelMask || (uint)h > TerrainCellFormat.TypePixelMask)
        {
            throw new ArgumentOutOfRangeException(nameof(fields));
        }

        uint look =
            (uint)fields.Slot |
            (IsBackground(block) ? TerrainCellFormat.TypeBackground : 0u) |
            (fields.OpaqueOwn ? TerrainCellFormat.TypeOpaqueOwn : 0u) |
            (fields.OpaqueAny ? TerrainCellFormat.TypeOpaqueAny : 0u) |
            ((uint)block.TextureAnchor << TerrainCellFormat.TypeTextureAnchorShift) |
            ((uint)block.Outline << TerrainCellFormat.TypeOutlineShift) |
            ((uint)block.AnimationType << TerrainCellFormat.TypeAnimationTypeShift) |
            ((uint)block.AnimationPalette << TerrainCellFormat.TypeAnimationPaletteShift) |
            ((uint)fields.DecalTextureIndex << TerrainCellFormat.TypeDecalTextureIndexShift);
        uint tileGroup = (uint)(fields.HasTileGroup ? fields.TileGroupId + 1 : 0);
        return new TerrainTypeRow(
            (uint)x | ((uint)y << TerrainCellFormat.TypePixelHighShift) | ((uint)fields.FrameCount << TerrainCellFormat.TypeByteShift),
            (uint)w | ((uint)h << TerrainCellFormat.TypePixelHighShift) | ((uint)block.ReliefGroup << TerrainCellFormat.TypeByteShift),
            look,
            HalfBits(block.FrameAnimationSpeed) |
                ((block.Emission ? 255u : 0u) << TerrainCellFormat.TypeGlowShift) |
                (tileGroup << TerrainCellFormat.TypeByteShift));
    }

    // half с отброшенным хвостом мантиссы — так же, как вершина пишет half
    // (TerrainVertex.H); шейдер разворачивает его f16tof32.
    internal static uint HalfBits(float value)
    {
        uint bits = unchecked((uint)BitConverter.SingleToInt32Bits(value));
        int exponent = (int)((bits >> 23) & 0xFFu) - 127 + 15;
        if (value < 0f || exponent >= 31)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Скорость анимации вне half.");
        }

        return exponent <= 0 ? 0u : ((uint)exponent << 10) | ((bits >> 13) & 0x3FFu);
    }

    internal static float HalfValue(uint halfBits)
    {
        uint exponent = (halfBits >> 10) & 0x1Fu;
        return exponent == 0u
            ? 0f
            : BitConverter.Int32BitsToSingle(unchecked((int)(((exponent - 15u + 127u) << 23) | ((halfBits & 0x3FFu) << 13))));
    }

    public const int TileDescriptorWords = 64;

    public static uint[] PackTileDescriptors()
    {
        var words = new uint[TileDescriptorWords];
        for (int mask = 0; mask < 256; mask++)
        {
            words[mask >> 2] |= (uint)TileBitmaskConverter.GetDescriptor((byte)mask) << ((mask & 3) * 8);
        }

        return words;
    }

    public static Vector4[] PackDistortion()
    {
        const int classicStep = TerrainVertexDistortionCalculator.DistortionStrengthUnits;
        return new Vector4[]
        {
            new Vector4(classicStep, TerrainConfigHolder.ClassicJitterRange,
                ((TerrainConfigHolder.ClassicJitterRange - 1) / 2) * classicStep, 0f),
            new Vector4(TerrainConfigHolder.ClassicXHashA, TerrainConfigHolder.ClassicXHashB,
                TerrainConfigHolder.ClassicXHashC, TerrainConfigHolder.ClassicXHashD),
            new Vector4(TerrainConfigHolder.ClassicYHashA, TerrainConfigHolder.ClassicYHashB,
                TerrainConfigHolder.ClassicYHashC, TerrainConfigHolder.ClassicYHashD),
            new Vector4(TerrainConfigHolder.ClassicXHashModulus, TerrainConfigHolder.ClassicYHashModulus,
                0f, 0f),
        };
    }

    // Доля свечения — дробная часть флагов света: шейдер складывает её с
    // целыми флагами (LightingFlags в TerrainCellFormat.hlsl).
    internal static float GlowFraction(float glow) =>
        glow * TerrainCellFormat.GlowFractionScale;
}

// Упаковка клетки и строки типа для буфера клеток (TerrainCellData). Вид
// типа — единственное место, где из конфига типа получается его строка; вид
// клетки шейдер выводит из клетки и соседей.
//
// Прежняя CPU-сборка вершин квада (FillQuad) живёт в тестах как эталон вида:
// Assets/Scripts/Tests/Editor/World/Terrain/Reference/Quads/TerrainQuadBuilder.cs.
internal static class TerrainCellPacker
{
    // Клетка для буфера — только её тип (0 у незагруженной); подложку,
    // узлы сетки и остальное шейдер выводит из типа и соседей. Кайма кольца
    // пишется так же.
    internal static TerrainCell PackCell(in TerrainCellSources sources, int x, int y) =>
        TerrainCellData.PackCell(sources.CellCache.GetCell(x + 1, y + 1).Type);

    /// <summary>Строка таблицы типов: тип из cells.json и факты его текстуры.</summary>
    ///
    /// Единственное место, где из метаданных типа получается его строка. Тип
    /// без разрешённых метаданных (default) — строка без текстуры и
    /// тайлгруппы: всё из cells.json в ней уже есть.
    internal static TerrainTypeFields ResolveTypeFields(
        CellType cellType,
        in CellMetadata props,
        IReadOnlyList<IAtlasDescriptor> atlases)
    {
        Vector4 atlasRect = props.AtlasRect;
        int frameCount = props.AnimationFrameCount;
        int slot = (uint)props.AtlasIndex < (uint)atlases.Count ? props.AtlasIndex : 0;
        bool hasTexture = atlasRect.z > 0f && atlasRect.w > 0f && props.AtlasIndex >= 0;
        if (!hasTexture)
        {
            atlasRect = Vector4.zero;
            frameCount = 1;
        }

        // Размер тайла и высота кадра не хранятся — выводятся так же, как в
        // шейдере (TerrainTypeTileSize, TerrainTypeFrameHeight).
        float tileSize = atlases.Count > 0 ? TerrainCellFormat.CellTexels / atlases[slot].Size : 0f;

        // Прямоугольник — в целых пикселях атласа, как его хранит строка;
        // UV из пикселей точны (размер атласа — степень двойки).
        if (atlasRect.z > 0f && atlases.Count > 0)
        {
            float size = atlases[slot].Size;
            atlasRect = new Vector4(
                Mathf.Round(atlasRect.x * size) / size,
                Mathf.Round(atlasRect.y * size) / size,
                Mathf.Round(atlasRect.z * size) / size,
                Mathf.Round(atlasRect.w * size) / size);
        }

        // Непрозрачность — свойство пары «тип, атлас»: шейдер атласов не
        // знает, поэтому оба ответа считаются здесь.
        bool opaqueAny = false;
        for (int index = 0; index < atlases.Count; index++)
        {
            opaqueAny |= atlases[index].IsFullyOpaque(cellType);
        }

        return new TerrainTypeFields(
            Type: cellType,
            Block: BlockRegistry.Get(cellType),
            DecalTextureIndex: props.DecalTextureIndex,
            Slot: slot,
            AtlasRect: atlasRect,
            TileSize: tileSize,
            FrameCount: frameCount,
            FrameHeightTiles: atlasRect.w > 0f ? atlasRect.w / tileSize : 1f,
            OpaqueOwn: slot < atlases.Count && atlases[slot].IsFullyOpaque(cellType),
            OpaqueAny: opaqueAny,
            HasTileGroup: props.HasTileGroup,
            TileGroupId: props.TileGroupId);
    }
}

public static class TerrainDecalCatalog
{
    public const int VariantCount = (int)TerrainCellFormat.DecalVariants;
    private static readonly Dictionary<string, byte> s_textureIndices = new(StringComparer.Ordinal);
    private static readonly Vector4[] s_atlasInfo = new Vector4[byte.MaxValue + 1];
    private static readonly Vector4[] s_effects = new Vector4[byte.MaxValue + 1];
    private static string[] s_textureNames = Array.Empty<string>();

    public static void Configure(IEnumerable<string?> textureNames)
    {
        s_textureIndices.Clear();
        Array.Clear(s_atlasInfo, 0, s_atlasInfo.Length);
        Array.Clear(s_effects, 0, s_effects.Length);
        var names = new List<string>();
        foreach (string? textureName in textureNames)
        {
            if (string.IsNullOrWhiteSpace(textureName) || s_textureIndices.ContainsKey(textureName))
            {
                continue;
            }

            if (names.Count == byte.MaxValue)
            {
                throw new InvalidDataException("World uses more decal textures than the GPU cell format supports.");
            }

            byte index = (byte)(names.Count + 1);
            names.Add(textureName);
            s_textureIndices.Add(textureName, index);
            s_atlasInfo[index] = TerrainDecalFactory.CreateShaderInfo();
            s_effects[index] = new Vector4(
                TerrainDecalFactory.Strength,
                TerrainDecalFactory.PlacementOffset / 255f,
                0f,
                0f);
        }

        s_textureNames = names.ToArray();
    }

    public static IReadOnlyList<string> GetTextureNames() => s_textureNames;

    public static Vector4[] GetAtlasInfo() => s_atlasInfo;

    public static Vector4[] GetEffects() => s_effects;

    public static byte TextureIndex(string? textureName)
    {
        if (string.IsNullOrWhiteSpace(textureName))
        {
            return 0;
        }

        return s_textureIndices.TryGetValue(textureName, out byte index)
            ? index
            : throw new InvalidDataException($"Decal texture '{textureName}' is not registered for the world.");
    }

    public static int Place(byte decalTextureIndex, int worldX, int serverY)
    {
        if (decalTextureIndex == 0)
        {
            return 0;
        }

        uint hash = Hash(worldX, serverY, decalTextureIndex);
        if ((hash % 100u) >= TerrainDecalFactory.PlacementPercent)
        {
            return 0;
        }

        int variant = (int)(hash % (uint)VariantCount);
        int rotation = (int)((hash >> 8) & 3u);
        int mirror = (int)((hash >> 10) & 1u);
        int offsetX = (int)((hash >> 12) & 3u);
        int offsetY = (int)((hash >> 14) & 3u);
        // DecalCode в TerrainCellFormat.hlsl.
        int packed = 1 + variant +
            (rotation << TerrainCellFormat.DecalRotationShift) +
            (mirror << TerrainCellFormat.DecalMirrorShift) +
            (offsetX << TerrainCellFormat.DecalOffsetXShift) +
            (offsetY << TerrainCellFormat.DecalOffsetYShift);
        return packed;
    }

    private static uint Hash(int worldX, int serverY, uint seed)
    {
        uint hash = unchecked((uint)worldX) * 374761393u;
        hash += unchecked((uint)serverY) * 668265263u;
        hash ^= seed * 2246822519u;
        hash = (hash ^ (hash >> 13)) * 1274126177u;
        return hash ^ (hash >> 16);
    }
}
