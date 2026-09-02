#nullable enable

namespace Fodinae.Rendering.PostProcessing;

public static class PostProcessLimits
{
    public const float BloomIntensityMin = 0f;
    public const float BloomIntensityMax = 2f;
    public const float ChromaticAberrationIntensityMin = 0f;
    public const float ChromaticAberrationIntensityMax = 0.25f;
    public const float ExposureMin = -2f;
    public const float ExposureMax = 2f;
    public const float ContrastMin = -0.5f;
    public const float ContrastMax = 0.5f;
    public const float EigengrauIntensityMin = 0f;
    public const float EigengrauIntensityMax = 0.25f;
    public const float MotionBlurIntensityMin = 0f;
    public const float MotionBlurIntensityMax = 0.5f;
}
