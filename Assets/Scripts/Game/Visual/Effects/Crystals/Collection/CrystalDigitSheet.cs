#nullable enable

using UnityEngine;

namespace Kern.Game.Visual.Effects.Crystals;

// Лист цифр счётчика добычи (Resources/Effects/Crystals/Digits/CrystalDigits.png, 192×12):
// шестнадцать клеток 12×12 — шесть значков кристаллов g r v b w c, затем цифры 1…9 и 0.
// Цифра выводится серой заготовкой, окрашенной цветом кристалла; ряд стоит справа от
// летящего кристалла: первая цифра в 0.75 клетки от его центра, следующие через 0.5.
// Клетка мира — 16 px текстуры (RenderingConstants.PIXELS_PER_UNIT), поэтому 12 px цифры
// занимают 0.75 клетки.
internal static class CrystalDigitSheet
{
    public const int CellPx = 12;
    public const int FirstDigitCell = 6;
    public const float FirstDigitOffset = 0.75f;
    public const float DigitStep = 0.5f;
    public const int SortingOrder = 6;

    // Клетка листа для цифры 0…9: «1» — шестая, «0» — пятнадцатая.
    public static int CellOf(int digit) => digit == 0 ? FirstDigitCell + 9 : FirstDigitCell + digit - 1;

    // Окраска цифр по букве кристалла.
    public static Color TintOf(string letter) => letter switch
    {
        "r" => new Color(1f, 0.5f, 0.5f),
        "v" => new Color(1f, 0.1f, 1f),
        "b" => new Color(0.5f, 0.5f, 1f),
        "w" => new Color(1f, 1f, 1f),
        "c" => new Color(0.1f, 1f, 1f),
        _ => new Color(0.5f, 1f, 0.5f),
    };
}
