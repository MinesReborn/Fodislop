#nullable enable

using Fodinae.Core;

namespace Fodinae.Rendering.PostProcessing;

public static class PostProcessLimits
{
    public const float BloomIntensityMin = 0f;
    public const float BloomIntensityMax = 2f;
    public const float ChromaticAberrationIntensityMin = 0f;
    public const float ChromaticAberrationIntensityMax = 0.25f;
    public const float ExposureMin = PostProcessSettings.ExposureMin;
    public const float ExposureMax = PostProcessSettings.ExposureMax;
    public const float ContrastMin = PostProcessSettings.ContrastMin;
    public const float ContrastMax = PostProcessSettings.ContrastMax;
    public const float EigengrauIntensityMin = 0f;
    public const float EigengrauIntensityMax = 0.25f;
    public const float MotionBlurIntensityMin = 0f;
    public const float MotionBlurIntensityMax = 0.5f;
}
