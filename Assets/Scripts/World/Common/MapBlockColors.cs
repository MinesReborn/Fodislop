#nullable enable

using System;
using System.Runtime.CompilerServices;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World;

/// <summary>
/// Палитра цветов клеток для мини-карты и большой карты.
///
/// Это единственный источник цвета клетки в игре: сервер цвета не присылает,
/// в <c>CellConfigurationPacket.Color</c> он кладёт заглушку
/// <c>0xFFFFFFFF</c> для всех 256 типов (<c>ProtocolWorld.BuildCellConfigurations</c>),
/// а модели цвета на сервере нет вовсе. Поэтому палитра, а не пакет, решает,
/// как клетка выглядит на карте.
///
/// Значения хранятся точно в <see cref="Color32"/>. Прежнее хранение в
/// <see cref="Color"/> с последующим кастом обратно теряло по единице в
/// каждом ненулевом канале: <c>Color(r / 256f)</c> → <c>byte(c * 255f)</c> даёт
/// <c>255 → 254</c> и <c>112 → 111</c>, то есть палитра систематически темнела.
///
/// Неназванные числовые значения (2–28, 46, 47, 56–59, 84, 85, 89, 123–255)
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

    private static readonly Color32[] _palette = new Color32[256];
    private static readonly bool[] _authored = new bool[256];

    static MapBlockColors()
    {
        Array.Fill(_palette, UnknownColor);
        BuildPalette();
    }

    /// <summary>Палитра описывает этот тип клетки и является для него источником правды.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAuthored(CellType cellType) => _authored[(byte)cellType];

    /// <summary>Цвет типа клетки в виде точных байтов.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color32 GetColor32(CellType cellType) => _palette[(byte)cellType];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color GetColor(CellType cellType) => _palette[(byte)cellType];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetPackedColor(CellType cellType)
    {
        Color32 bytes = _palette[(byte)cellType];
        return unchecked((int)(((uint)bytes.a << 24) |
            ((uint)bytes.r << 16) |
            ((uint)bytes.g << 8) |
            bytes.b));
    }

    private static void BuildPalette()
    {
        // Пустота и прегенер — почти прозрачные чёрные, как в оригинальном клиенте.
        Set(0, 0, 0, 0, 128);
        Set(1, 0, 0, 0, 128);

        // Фон и пол.
        Set(31, 40, 12, 6, 255);   // VolcanoBackground
        Set(32, 0, 0, 0, 255);     // Empty
        Set(33, 15, 11, 3, 255);   // BackgroundWithLightTraces
        Set(34, 29, 25, 18, 255);  // BackgroundWithHeavyTraces

        // Дороги и проходы.
        Set(29, 56, 52, 48, 255);  // BuildingRoad
        Set(30, 68, 17, 17, 255);  // Gate
        Set(35, 68, 68, 68, 255);  // Road
        Set(36, 85, 68, 34, 255);  // GoldenRoad
        Set(39, 60, 72, 84, 255);  // PolymerRoad
        Set(83, 50, 135, 152, 255); // TeleportBlock

        // Постройки.
        Set(37, 68, 0, 0, 255);      // BuildingDoor
        Set(38, 51, 68, 0, 255);      // BuildingCorner
        Set(106, 136, 136, 136, 255); // BuildingWall
        Set(104, 153, 153, 136, 255); // FedBlock
        Set(101, 76, 191, 0, 255);    // GreenBlock
        Set(102, 208, 206, 0, 255);   // YellowBlock
        Set(105, 198, 0, 0, 255);     // RedBlock
        Set(90, 238, 238, 238, 255);  // Box
        Set(88, 170, 165, 150, 255);  // Skull

        // Военные блоки и поддержки.
        Set(48, 255, 255, 255, 255);  // QuadBlock
        Set(49, 101, 150, 126, 255);  // Support
        Set(80, 24, 72, 72, 255);     // MilitaryBlockFrame
        Set(81, 17, 85, 85, 255);     // MilitaryBlock
        Set(82, 17, 102, 102, 255);   // MilitaryBlockSand

        // Валуны.
        Set(40, 255, 97, 107, 255);   // BlackBoulder1
        Set(41, 255, 107, 97, 255);   // BlackBoulder2
        Set(42, 255, 107, 107, 255);  // BlackBoulder3
        Set(43, 255, 187, 251, 255);  // MetalBoulder1
        Set(44, 191, 241, 251, 255);  // MetalBoulder2
        Set(45, 207, 203, 241, 255);  // MetalBoulder3
        Set(92, 193, 187, 187, 255);  // Boulder1
        Set(93, 187, 193, 187, 255);  // Boulder2
        Set(94, 187, 187, 193, 255);  // Boulder3

        // Живые кристаллы.
        Set(50, 101, 255, 255, 255); // AliveCyan
        Set(51, 255, 51, 51, 255);   // AliveRed
        Set(52, 255, 101, 255, 255); // AliveViol
        Set(53, 34, 101, 255, 255);  // AliveNigger
        Set(54, 238, 254, 255, 255); // AliveWhite
        Set(55, 238, 254, 255, 255); // AliveRainbow
        Set(116, 17, 17, 255, 255);  // AliveBlue

        // Пески.
        Set(60, 204, 204, 204, 255); // WhiteSand
        Set(61, 221, 221, 221, 255); // DarkWhiteSand
        Set(62, 255, 204, 204, 255); // RustySand
        Set(63, 255, 221, 221, 255); // DarkRustySand
        Set(64, 170, 170, 170, 255); // BlackSand
        Set(65, 187, 187, 187, 255); // DarkBlackSand
        Set(97, 112, 160, 183, 255); // BlueSand
        Set(98, 112, 187, 207, 255); // DarkBlueSand
        Set(99, 219, 209, 125, 255); // YellowSand
        Set(100, 181, 168, 57, 255); // DarkYellowSand

        // Кислоты и опасности.
        Set(66, 184, 153, 51, 255); // GrayAcid
        Set(67, 184, 136, 187, 255); // PurpleAcid
        Set(86, 71, 215, 100, 255); // PassiveAcid
        Set(95, 184, 255, 34, 255); // LivingActiveAcid
        Set(96, 184, 255, 68, 255); // CorrosiveActiveAcid
        Set(91, 255, 90, 0, 255);   // Lava
        Set(70, 243, 241, 152, 255); // DeepMagmaBoulder

        // Жилы породы.
        Set(103, 133, 81, 166, 255); // Rock
        Set(113, 211, 159, 166, 255); // HeavyRock
        Set(114, 119, 119, 119, 255); // NiggerRock
        Set(115, 56, 118, 65, 255); // LivingBlackRock
        Set(117, 170, 119, 119, 255); // RedRock
        Set(120, 227, 191, 120, 255); // GoldenRock
        Set(121, 163, 136, 72, 255); // DeepRock
        Set(122, 51, 153, 120, 255);  // GRock
        Set(76, 46, 42, 54, 255);    // DeepObsidianRock
        Set(79, 180, 90, 140, 255);  // DeepStripedRock

        // Кристаллы и голубы.
        Set(71, 101, 134, 247, 255); // XGreen
        Set(72, 247, 82, 67, 255);   // XBlue
        Set(73, 101, 255, 255, 255); // XRed
        Set(74, 132, 238, 247, 255); // XCyan
        Set(75, 255, 135, 231, 255); // XViolet
        Set(68, 119, 68, 68, 255);   // Pearl
        Set(69, 34, 68, 153, 255);   // DeepLazuriteSand
        Set(77, 40, 160, 160, 255); // DeepTurquoiseRock
        Set(78, 200, 120, 220, 255); // DeepRainbowRock

        // Собранные кристаллы.
        Set(107, 8, 215, 100, 255);  // Green
        Set(108, 255, 0, 0, 255);    // Red
        Set(109, 0, 0, 255, 255);    // Blue
        Set(110, 255, 0, 255, 255);  // Violet
        Set(111, 238, 238, 255, 255); // White
        Set(112, 0, 255, 255, 255);  // Cyan
        Set(87, 255, 180, 255, 255); // SuperRainbow
        Set(118, 100, 98, 21, 255);  // AcidRock
        Set(119, 170, 255, 255, 255); // HypnoRock
    }

    private static void Set(int cellId, byte r, byte g, byte b, byte a)
    {
        _palette[cellId] = new Color32(r, g, b, a);
        _authored[cellId] = true;
    }
}
