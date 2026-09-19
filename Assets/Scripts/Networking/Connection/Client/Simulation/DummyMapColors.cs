#nullable enable

namespace MinesServer.Networking.Connection.Client;

internal static class DummyMapColors
{
    public static int Get(int cellID)
    {
        if (cellID is < 0 or > 255)
        {
            return RGB(128, 128, 128);
        }

        return _Table[cellID];
    }

    private static readonly int[] _Table = BuildTable();

    private static int[] BuildTable()
    {
        var table = new int[256];

        // Основа: тот же градиент, что задавал старый клиент всем номерам подряд.
        for (int i = 0; i < 256; i++)
        {
            table[i] = FromUnit(i / 512f, i / 256f, 0.01f, 1f);
        }

        // Пустота и дорога — почти прозрачные в оригинале.
        table[0] = FromUnit(0f, 0f, 0f, 0.5f);
        table[1] = FromUnit(0f, 0f, 0f, 0.5f);

        table[32] = RGB(0, 0, 0);
        table[33] = RGB(15, 11, 3);
        table[34] = RGB(29, 25, 18);
        table[35] = RGB(68, 68, 68);
        table[36] = RGB(85, 68, 34);
        table[37] = RGB(68, 0, 0);
        table[38] = RGB(51, 68, 0);
        table[40] = RGB(255, 97, 107);
        table[41] = RGB(255, 107, 97);
        table[42] = RGB(255, 107, 107);
        table[43] = RGB(255, 187, 251);
        table[44] = RGB(191, 241, 251);
        table[45] = RGB(207, 203, 241);
        table[48] = RGB(255, 255, 255);
        table[49] = RGB(101, 150, 126);
        table[50] = RGB(101, 255, 255);
        table[51] = RGB(255, 51, 51);
        table[52] = RGB(255, 101, 255);
        table[53] = RGB(34, 101, 255);
        table[54] = RGB(238, 254, 255);
        table[55] = RGB(238, 254, 255);
        table[56] = RGB(225, 254, 255);
        table[57] = RGB(226, 254, 255);
        table[58] = RGB(227, 254, 255);
        table[59] = RGB(228, 254, 255);
        table[60] = RGB(204, 204, 204);
        table[61] = RGB(221, 221, 221);
        table[62] = RGB(255, 204, 204);
        table[63] = RGB(255, 221, 221);
        table[64] = RGB(170, 170, 170);
        table[65] = RGB(187, 187, 187);
        table[66] = RGB(184, 153, 51);
        table[67] = RGB(184, 136, 187);
        table[68] = RGB(119, 68, 68);
        table[69] = RGB(34, 68, 153);
        table[70] = RGB(243, 241, 152);
        table[71] = RGB(71, 215, 100);
        table[72] = RGB(101, 134, 247);
        table[73] = RGB(247, 82, 67);
        table[74] = RGB(132, 238, 247);
        table[75] = RGB(255, 135, 231);
        table[82] = RGB(17, 102, 102);
        table[83] = RGB(50, 135, 152);
        table[86] = RGB(184, 255, 17);
        table[90] = RGB(238, 238, 238);
        table[91] = RGB(255, 90, 0);
        table[92] = RGB(193, 187, 187);
        table[93] = RGB(187, 193, 187);
        table[94] = RGB(187, 187, 193);
        table[95] = RGB(184, 255, 34);
        table[96] = RGB(184, 255, 68);
        table[97] = RGB(112, 160, 183);
        table[98] = RGB(112, 187, 207);
        table[99] = RGB(219, 209, 125);
        table[100] = RGB(181, 168, 57);
        table[101] = RGB(76, 191, 0);
        table[102] = RGB(208, 206, 0);
        table[103] = RGB(133, 81, 166);
        table[104] = RGB(153, 153, 136);
        table[105] = RGB(198, 0, 0);
        table[106] = RGB(136, 136, 136);
        table[107] = RGB(8, 215, 100);
        table[108] = RGB(255, 0, 0);
        table[109] = RGB(0, 0, 255);
        table[110] = RGB(255, 0, 255);
        table[111] = RGB(238, 238, 255);
        table[112] = RGB(0, 255, 255);
        table[113] = RGB(211, 159, 166);
        table[114] = RGB(119, 119, 119);
        table[115] = RGB(56, 118, 65);
        table[116] = RGB(17, 17, 255);
        table[117] = RGB(170, 119, 119);
        table[118] = RGB(100, 98, 21);
        table[119] = RGB(170, 255, 255);
        table[120] = RGB(227, 191, 120);
        table[121] = RGB(163, 136, 72);
        table[122] = RGB(51, 153, 120);

        return table;
    }

    private static int RGB(int r, int g, int b) =>
        FromUnit(r / 256f, g / 256f, b / 256f, 1f);

    private static int FromUnit(float r, float g, float b, float a)
    {
        int red = ToByte(r);
        int green = ToByte(g);
        int blue = ToByte(b);
        int alpha = ToByte(a);
        return unchecked((int)(((uint)alpha << 24) | ((uint)red << 16) | ((uint)green << 8) | (uint)blue));
    }

    private static int ToByte(float value)
    {
        int scaled = (int)((value * 255f) + 0.5f);
        return scaled < 0 ? 0 : scaled > 255 ? 255 : scaled;
    }
}
