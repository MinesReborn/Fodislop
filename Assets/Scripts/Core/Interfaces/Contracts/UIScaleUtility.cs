#nullable enable

using UnityEngine;

namespace Kern.Core;

public static class UIScaleUtility
{
    public const float HighDpiThreshold = 180f;
    public const float RetinaDefaultScale = 1.35f;
    public const float StandardDefaultScale = 1.00f;

    public const float UIScaleMin = 0.50f;
    public const float UIScaleMax = 2.50f;

    public static bool IsRetinaOrHighDpi
    {
        get
        {
            float dpi = Screen.dpi;
            if (dpi >= HighDpiThreshold)
            {
                return true;
            }

            // На macOS в редакторе или standalone встроенные экраны MacBook
            // имеют Retina-матрицу (обычно 220..260 dpi, но Unity иногда
            // сообщает 160+ в зависимости от масштабирования дисплея в ОС).
            if ((Application.platform == RuntimePlatform.OSXPlayer ||
                 Application.platform == RuntimePlatform.OSXEditor) &&
                dpi >= 160f)
            {
                return true;
            }

            return false;
        }
    }

    public static float RecommendedDefaultScale =>
        IsRetinaOrHighDpi ? RetinaDefaultScale : StandardDefaultScale;

    public static float Clamp(float scale) =>
        Mathf.Clamp(scale, UIScaleMin, UIScaleMax);

    public static float ResolveEffectiveScale(float configuredScale)
    {
        if (float.IsNaN(configuredScale) || float.IsInfinity(configuredScale) || configuredScale <= 0f)
        {
            return RecommendedDefaultScale;
        }

        return Clamp(configuredScale);
    }
}
