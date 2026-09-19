#nullable enable

using UnityEngine;

namespace Kern.Rendering.PostProcessing;
public static class ColorblindAdaptation
{
    public static bool TryApply(
        int colorblindMode,
        ref Color filter,
        ref float contrast,
        ref float saturation)
    {
        switch (colorblindMode)
        {
            case 0:
                return true;
            case 1:
                filter = new Color(
                    filter.r * 0.8f + filter.g * 0.2f,
                    filter.g * 0.7f + filter.b * 0.3f,
                    filter.b);
                return true;
            case 2:
                filter = new Color(
                    filter.r * 0.6f + filter.g * 0.4f,
                    filter.g * 0.9f,
                    filter.b * 1.1f);
                return true;
            case 3:
                filter = new Color(
                    filter.r * 0.95f,
                    filter.g * 0.85f + filter.b * 0.15f,
                    filter.b * 0.5f + filter.r * 0.5f);
                return true;
            case 4:
                contrast += 0.35f;
                saturation += 0.2f;
                return true;
            default:
                return false;
        }
    }
}
