#nullable enable

using System;
using System.Runtime.CompilerServices;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World;

/// <summary>
/// Палитра цветов клеток для мини-карты и большой карты.
///
/// Это временный источник цвета клетки: сервер пока не присылает реальные
/// цвета, в <c>CellConfigurationPacket.MapColor</c> он кладёт заглушку
/// <c>0xFFFFFFFF</c> для всех 256 типов (<c>ProtocolWorld.BuildCellConfigurations</c>),
/// а модели цвета на сервере нет. После миграции источником станет MapColor,
/// палитра останется fallback для неизвестных типов.
///
/// Значения хранятся точно в <see cref="Color32"/>. Прежнее хранение в
/// <see cref="Color"/> с последующим кастом обратно теряло по единице в
/// каждом ненулевом канале: <c>Color(r / 256f)</c> → <c>byte(c * 255f)</c> даёт
/// <c>255 → 254</c> и <c>112 → 111</c>, то есть палитра систематически темнела.
///
/// Неназванные числовые значения (3–28, 46, 47, 56–59, 84, 85, 89, 123–255)
/// получают <see cref="UnknownColor"/> — громкий маркер «нет данных», а не
/// формула: формула молча рисовала произвольный цвет там, где типа клетки
/// попросту нет.
/// </summary>
public static class MapBlockColors
{
    /// <summary>Маркер типа клетки, для которого в палитре нет цвета.</summary>
    public static readonly Color32 UnknownColor = new(255, 0, 255, 255);

    /// <summary>Заглушка для типа клетки, которому сервер цвет не прислал и палитра неизвестна.</summary>
    public static readonly Color32 MissingServerColor = new(77, 77, 77, 255);

    private static readonly Color32[] s_palette = new Color32[256];
    private static readonly bool[] s_authored = new bool[256];

    static MapBlockColors()
    {
        Array.Fill(s_palette, UnknownColor);
        BuildPalette();
    }

    /// <summary>Палитра описывает этот тип клетки и является для него источником правды.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAuthored(CellType cellType) => s_authored[(byte)cellType];

    /// <summary>Цвет типа клетки в виде точных байтов.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color32 GetColor32(CellType cellType) => s_palette[(byte)cellType];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color GetColor(CellType cellType) => s_palette[(byte)cellType];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetPackedColor(CellType cellType)
    {
        Color32 bytes = s_palette[(byte)cellType];
        return unchecked((int)(((uint)bytes.a << 24) |
            ((uint)bytes.r << 16) |
            ((uint)bytes.g << 8) |
            bytes.b));
    }

    private static void BuildPalette()
    {
        foreach ((CellType type, BlockDefinition def) in BlockRegistry.Blocks)
        {
            Color32 color = def.MapColor;
            Set((byte)type, color.r, color.g, color.b, color.a);
        }
    }

    private static void Set(int cellId, byte r, byte g, byte b, byte a)
    {
        s_palette[cellId] = new Color32(r, g, b, a);
        s_authored[cellId] = true;
    }
}
