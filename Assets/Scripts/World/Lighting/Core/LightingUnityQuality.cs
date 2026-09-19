#nullable enable

using System;
using Kern.Rendering;
using Kern.World.Lighting.Quality;
using UnityEngine;

namespace Kern.World.Lighting;

// Разрешение настроек качества в конкретные числа — и только это.
//
// Сами присваивания Unity (QualitySettings.SetQualityLevel, antiAliasing,
// renderScale URP) живут в LightingEngine и обязаны жить там: владение
// глобальным состоянием качества принадлежит одному типу, иначе два места
// начинают спорить за один тумблер, и кто выставил текущее значение —
// неустановимо. Стережёт KERN-PATTERN.
//
// Здесь остаётся вся арифметика и поиск: она тестируема, ничего глобального не
// трогает и не требует ни Unity-сцены, ни живого конвейера.
internal static class LightingUnityQuality
{
    // Уровень качества Unity, который соответствует пресету, либо -1, если
    // подходящего уровня нет или менять нечего.
    public static int ResolveQualityLevelIndex(GraphicsPreset preset)
    {
        if (!GraphicsQualityProfile.IsStandard(preset))
        {
            return -1;
        }

        string targetName = preset.ToString();
        string[] qualityNames = QualitySettings.names;
        int qualityIndex = Array.IndexOf(qualityNames, targetName);
        if (qualityIndex < 0)
        {
            int presetIndex = (int)preset;
            if (presetIndex >= 0 && presetIndex < qualityNames.Length)
            {
                qualityIndex = presetIndex;
            }
            else
            {
                for (int i = 0; i < qualityNames.Length; i++)
                {
                    if (string.Equals(
                        qualityNames[i].Replace(" ", string.Empty),
                        targetName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        qualityIndex = i;
                        break;
                    }
                }
            }
        }

        if (qualityIndex < 0 || QualitySettings.GetQualityLevel() == qualityIndex)
        {
            return -1;
        }

        return qualityIndex;
    }

    public static string DescribeQualityLevel(int qualityIndex)
    {
        string[] qualityNames = QualitySettings.names;
        return qualityIndex >= 0 && qualityIndex < qualityNames.Length
            ? qualityNames[qualityIndex]
            : qualityIndex.ToString();
    }

    // Что именно надо выставить в Unity и URP по этим настройкам.
    public static RenderingPlan ResolveRenderingPlan(GraphicsQualitySettings settings)
    {
        int antiAliasing = Mathf.Clamp(settings.AntiAliasing, 0, 8);
        float requested = Mathf.Clamp(settings.RenderScale, 0.5f, 1f);
        float quantized = PixelGrid.QuantizeRenderScale(requested, 0.5f, 1f);
        return new RenderingPlan(
            antiAliasing,
            Mathf.Max(1, settings.AntiAliasing),
            requested,
            quantized);
    }

    internal readonly record struct RenderingPlan(
        int AntiAliasing,
        int MsaaSampleCount,
        float RequestedRenderScale,
        float RenderScale)
    {
        // Промежуточные значения дают дробный апскейл и муар на пиксель-арте,
        // поэтому запрошенный масштаб притягивается к сетке. Расхождение стоит
        // назвать вслух: иначе настройка молча означает не то, что показывает.
        public bool RenderScaleWasQuantized =>
            !Mathf.Approximately(RequestedRenderScale, RenderScale);
    }
}
