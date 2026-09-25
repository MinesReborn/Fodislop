#nullable enable

using Kern.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Kern.World.Lighting;

/// <summary>
/// Владение глобальным качеством Unity: уровень качества, сглаживание и
/// масштаб рендера URP.
/// </summary>
///
/// Это единственное место, где эти три значения присваиваются: у глобального
/// тумблера обязан быть ровно один владелец, иначе «кто выставил текущее
/// значение» становится неустановимым. Стережёт KERN-PATTERN. Поиск уровня и
/// квантование масштаба остались в <see cref="LightingUnityQuality"/> — они
/// чистая арифметика и тестируются без сцены.
internal static class LightingUnityQualityApplier
{
    public static void ApplyQualityLevel()
    {
        int qualityIndex = LightingUnityQuality.ResolveQualityLevelIndex();
        if (qualityIndex < 0)
        {
            return;
        }

        QualitySettings.SetQualityLevel(qualityIndex, applyExpensiveChanges: true);
        Debug.Log(
            "[LightingUnityQuality] Applied Unity QualityLevel: " +
            $"{LightingUnityQuality.DescribeQualityLevel(qualityIndex)} ({qualityIndex})");
    }

    public static void ApplyRenderingSettings(GraphicsQualitySettings settings)
    {
        LightingUnityQuality.RenderingPlan plan =
            LightingUnityQuality.ResolveRenderingPlan(settings);
        QualitySettings.antiAliasing = plan.AntiAliasing;
        if (plan.RenderScaleWasQuantized)
        {
            Debug.Log(
                $"[LightingUnityQuality] Масштаб рендера {plan.RequestedRenderScale:F2} приведён " +
                $"к {plan.RenderScale:F2}: промежуточные значения дают дробный апскейл " +
                "и муар на пиксель-арте.");
        }

        float appliedScale = plan.RequestedRenderScale;
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
        {
            urp.renderScale = plan.RenderScale;
            urp.msaaSampleCount = plan.MsaaSampleCount;
            appliedScale = urp.renderScale;
        }

        Debug.Log(
            $"[LightingUnityQuality] ApplyUnityRenderingSettings: AA={settings.AntiAliasing}, " +
            $"RenderScale={appliedScale} (запрошено {settings.RenderScale})");
    }
}
