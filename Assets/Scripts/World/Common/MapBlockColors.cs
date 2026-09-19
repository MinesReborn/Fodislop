#nullable enable

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World;

public static class MapBlockColors
{
    private static readonly Color[] _colorTable = new Color[256];
    private static readonly Color32[] _color32Table = new Color32[256];
    private static readonly Color[] _aliveColorTable = new Color[256];
    private static readonly Color32[] _aliveColor32Table = new Color32[256];
    private static readonly Color[] _transparentTable = new Color[256];
    private static readonly Color32[] _transparent32Table = new Color32[256];
    private static readonly Color[] _customTable = new Color[256];
    private static readonly Color32[] _custom32Table = new Color32[256];

    static MapBlockColors()
    {
        InitializeCustomTable();
        InitializeTables();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color GetColor(CellType cellType) => _colorTable[(byte)cellType];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color32 GetColor32(CellType cellType) => _color32Table[(byte)cellType];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color GetAliveColor(CellType cellType) => _aliveColorTable[(byte)cellType];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color32 GetAliveColor32(CellType cellType) => _aliveColor32Table[(byte)cellType];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color GetTransparentColor(CellType cellType) => _transparentTable[(byte)cellType];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color32 GetTransparentColor32(CellType cellType) => _transparent32Table[(byte)cellType];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color GetCustomColor(CellType cellType) => _customTable[(byte)cellType];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color RGBq(int r, int g, int b) =>
        new Color(r / 256f, g / 256f, b / 256f, 1f);

    private static void InitializeCustomTable()
    {
        for (int i = 0; i < 256; i++)
        {
            int num = i + 50;
            _customTable[i] = new Color(num / 312f, num / 312f, num / 312f, 1f);
            if (i <= 39)
            {
                _customTable[i] = new Color(0.04f, 0.045f, 0.045f, 1f);
            }
        }
    }

    private static void InitializeTables()
    {
        var sandIndices = new HashSet<int>
        {
            82, 91, 97, 98, 99, 100, 86, 66, 67, 95, 96, 60, 61, 62, 63, 64, 65, 68, 69,
        };

        for (int i = 0; i < 256; i++)
        {
            _colorTable[i] = new Color(i / 512f, i / 256f, 0.01f, 1f);
            _aliveColorTable[i] = new Color(i / 512f, i / 256f, 0.01f, 1f);
            _transparentTable[i] = new Color(0f, 0f, 0f, 1f);

            if (i > 39)
            {
                _transparentTable[i] = new Color(0.4f, 0.45f, 0.45f, 1f);
            }

            if (i < 120 && sandIndices.Contains(i))
            {
                _transparentTable[i] = new Color(0.3f, 0.35f, 0.35f, 1f);
            }
        }

        // Transparent table overrides
        _transparentTable[30] = new Color(1f, 1f, 0f, 1f);
        _transparentTable[35] = new Color(0.4f, 0.01f, 0.1f, 1f);
        _transparentTable[36] = new Color(0.4f, 0.1f, 0.01f, 1f);
        _transparentTable[39] = new Color(0.4f, 0.01f, 0.01f, 1f);
        _transparentTable[80] = new Color(0f, 1f, 1f, 1f);
        _transparentTable[81] = new Color(0f, 0.7f, 0.7f, 1f);
        _transparentTable[106] = new Color(1f, 1f, 1f, 1f);
        _transparentTable[114] = new Color(0.6f, 0.6f, 0.6f, 1f);
        _transparentTable[115] = new Color(0.6f, 0.6f, 0.6f, 1f);
        _transparentTable[117] = new Color(1f, 1f, 1f, 1f);
        _transparentTable[119] = new Color(0.8f, 1f, 1f, 1f);

        // Color table overrides
        _colorTable[0] = new Color(0f, 0f, 0f, 0.5f);
        _colorTable[1] = new Color(0f, 0f, 0f, 0.5f);
        _colorTable[32] = RGBq(0, 0, 0);
        _colorTable[33] = RGBq(15, 11, 3);
        _colorTable[34] = RGBq(29, 25, 18);
        _colorTable[35] = RGBq(68, 68, 68);
        _colorTable[36] = RGBq(85, 68, 34);
        _colorTable[37] = RGBq(68, 0, 0);
        _colorTable[38] = RGBq(51, 68, 0);
        _colorTable[40] = RGBq(255, 97, 107);
        _colorTable[41] = RGBq(255, 107, 97);
        _colorTable[42] = RGBq(255, 107, 107);
        _colorTable[43] = RGBq(255, 187, 251);
        _colorTable[44] = RGBq(191, 241, 251);
        _colorTable[45] = RGBq(207, 203, 241);
        _colorTable[48] = RGBq(255, 255, 255);
        _colorTable[49] = RGBq(101, 150, 126);
        _colorTable[50] = RGBq(101, 255, 255);
        _colorTable[51] = RGBq(255, 51, 51);
        _colorTable[52] = RGBq(255, 101, 255);
        _colorTable[53] = RGBq(34, 101, 255);
        _colorTable[54] = RGBq(238, 254, 255);
        _colorTable[55] = RGBq(238, 254, 255);
        _colorTable[56] = RGBq(225, 254, 255);
        _colorTable[57] = RGBq(226, 254, 255);
        _colorTable[58] = RGBq(227, 254, 255);
        _colorTable[59] = RGBq(228, 254, 255);
        _colorTable[60] = RGBq(204, 204, 204);
        _colorTable[61] = RGBq(221, 221, 221);
        _colorTable[62] = RGBq(255, 204, 204);
        _colorTable[63] = RGBq(255, 221, 221);
        _colorTable[64] = RGBq(170, 170, 170);
        _colorTable[65] = RGBq(187, 187, 187);
        _colorTable[66] = RGBq(184, 153, 51);
        _colorTable[67] = RGBq(184, 136, 187);
        _colorTable[68] = RGBq(119, 68, 68);
        _colorTable[69] = RGBq(34, 68, 153);
        _colorTable[70] = RGBq(243, 241, 152);
        _colorTable[71] = RGBq(71, 215, 100);
        _colorTable[72] = RGBq(101, 134, 247);
        _colorTable[73] = RGBq(247, 82, 67);
        _colorTable[74] = RGBq(132, 238, 247);
        _colorTable[75] = RGBq(255, 135, 231);
        _colorTable[82] = RGBq(17, 102, 102);
        _colorTable[83] = RGBq(50, 135, 152);
        _colorTable[86] = RGBq(184, 255, 17);
        _colorTable[90] = RGBq(238, 238, 238);
        _colorTable[91] = RGBq(255, 90, 0);
        _colorTable[92] = RGBq(193, 187, 187);
        _colorTable[93] = RGBq(187, 193, 187);
        _colorTable[94] = RGBq(187, 187, 193);
        _colorTable[95] = RGBq(184, 255, 34);
        _colorTable[96] = RGBq(184, 255, 68);
        _colorTable[97] = RGBq(112, 160, 183);
        _colorTable[98] = RGBq(112, 187, 207);
        _colorTable[99] = RGBq(219, 209, 125);
        _colorTable[100] = RGBq(181, 168, 57);
        _colorTable[101] = RGBq(76, 191, 0);
        _colorTable[102] = RGBq(208, 206, 0);
        _colorTable[103] = RGBq(133, 81, 166);
        _colorTable[104] = RGBq(153, 153, 136);
        _colorTable[105] = RGBq(198, 0, 0);
        _colorTable[106] = RGBq(136, 136, 136);
        _colorTable[107] = RGBq(8, 215, 100);
        _colorTable[108] = RGBq(255, 0, 0);
        _colorTable[109] = RGBq(0, 0, 255);
        _colorTable[110] = RGBq(255, 0, 255);
        _colorTable[111] = RGBq(238, 238, 255);
        _colorTable[112] = RGBq(0, 255, 255);
        _colorTable[113] = RGBq(211, 159, 166);
        _colorTable[114] = RGBq(119, 119, 119);
        _colorTable[115] = RGBq(56, 118, 65);
        _colorTable[116] = RGBq(17, 17, 255);
        _colorTable[117] = RGBq(170, 119, 119);
        _colorTable[118] = RGBq(100, 98, 21);
        _colorTable[119] = RGBq(170, 255, 255);
        _colorTable[120] = RGBq(227, 191, 120);
        _colorTable[121] = RGBq(163, 136, 72);
        _colorTable[122] = RGBq(51, 153, 120);

        // Alive color table overrides
        _aliveColorTable[50] = RGBq(101, 255, 255);
        _aliveColorTable[51] = RGBq(255, 51, 51);
        _aliveColorTable[52] = RGBq(255, 101, 255);
        _aliveColorTable[53] = RGBq(255, 138, 255);
        _aliveColorTable[54] = RGBq(238, 254, 255);
        _aliveColorTable[55] = RGBq(238, 254, 255);
        _aliveColorTable[116] = RGBq(161, 162, 255);
        _aliveColorTable[119] = RGBq(170, 255, 255);

        // Fill Color32 lookup tables
        for (int i = 0; i < 256; i++)
        {
            _color32Table[i] = (Color32)_colorTable[i];
            _aliveColor32Table[i] = (Color32)_aliveColorTable[i];
            _transparent32Table[i] = (Color32)_transparentTable[i];
            _custom32Table[i] = (Color32)_customTable[i];
        }
    }
}
