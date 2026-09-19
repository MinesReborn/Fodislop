#nullable enable

using Kern.Core;

namespace Kern.Rendering.PostProcessing;

public static class PostProcessLimits
{
    public const float BloomIntensityMin = 0f;
    public const float BloomIntensityMax = 2f;
    public const float ExposureMin = PostProcessSettings.ExposureMin;
    public const float ExposureMax = PostProcessSettings.ExposureMax;
    public const float ContrastMin = PostProcessSettings.ContrastMin;
    public const float ContrastMax = PostProcessSettings.ContrastMax;
    public const float EigengrauIntensityMin = 0f;
    // Потолок был 0.25 при авторском значении 0.3 в PostProcessLook: параметр
    // молча зажимался, и авторское число означало не то, что написано.
    public const float EigengrauIntensityMax = 1f;
    public const float MotionBlurIntensityMin = 0f;
    public const float MotionBlurIntensityMax = 0.5f;
}
