#nullable enable

using System.Drawing;

namespace Fodinae.UI;

internal static class ChatColorPalette
{
    public static readonly Color DefaultColor = Color.FromArgb(255, 200, 180, 100);

    public static readonly Color[] PresetColors =
    [
        Color.White,
        Color.FromArgb(255, 60, 60),
        Color.FromArgb(60, 255, 60),
        Color.FromArgb(60, 130, 255),
        Color.FromArgb(255, 220, 60),
        Color.FromArgb(60, 255, 255),
        Color.FromArgb(255, 60, 255),
        Color.FromArgb(255, 160, 60),
    ];
}
