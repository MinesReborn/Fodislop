#nullable enable

using Kern.Core;
using UnityEngine;

namespace Kern.Rendering.PostProcessing;

// Ползунки калибровки 0..100 → параметры ToDisplayOutput (PostProcess.compute). 50 — нейтрально: ничего не меняет.
public static class DisplayAdjustment
{
    // Предельный сдвиг чёрного — доля белой точки. 0.013 линейных ≈ 30 из 255 в кодировке sRGB у самого чёрного:
    // столько же даёт макет (calibration.js); больше — чёрное серое, меньше — ползунок не достаёт до раздавленного.
    private const float MaxBlackLift = 0.013f;

    // Предельная степень средних тонов SDR: 2^±0.5 — от ×0.71 (светлее) до ×1.41 (темнее)
    private const float MaxGammaStops = 0.5f;

    public static float BlackLift(float blackLevel) =>
        (Mathf.Clamp(blackLevel, DisplaySettings.AdjustmentMin, DisplaySettings.AdjustmentMax) - DisplaySettings.DefaultBlackLevel) /
        DisplaySettings.DefaultBlackLevel * MaxBlackLift;

    public static float Gamma(float brightness) =>
        Mathf.Pow(2f, (DisplaySettings.DefaultBrightness -
            Mathf.Clamp(brightness, DisplaySettings.AdjustmentMin, DisplaySettings.AdjustmentMax)) /
            DisplaySettings.DefaultBrightness * MaxGammaStops);
}
