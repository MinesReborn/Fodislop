#nullable enable

using UnityEngine;

namespace Fodinae.Rendering.PostProcessing;

internal static class PostProcessShaderConstants
{
    public const string PassName = "ComputePostProcessPass";

    public static readonly int InputTexID = Shader.PropertyToID("_InputTex");
    public static readonly int SourceTexID = Shader.PropertyToID("_SourceTex");
    public static readonly int BaseTexID = Shader.PropertyToID("_BaseTex");
    public static readonly int BloomTexID = Shader.PropertyToID("_BloomTex");
    public static readonly int DestTexID = Shader.PropertyToID("_DestTex");
    public static readonly int OutputTexID = Shader.PropertyToID("_OutputTex");
    public static readonly int ScreenSizeID = Shader.PropertyToID("_ScreenSize");
    public static readonly int SourceTexelSizeID = Shader.PropertyToID("_SourceTexelSize");

    public static readonly int BloomThresholdID = Shader.PropertyToID("_BloomThreshold");
    public static readonly int BloomSoftKneeID = Shader.PropertyToID("_BloomSoftKnee");
    public static readonly int BloomRadiusID = Shader.PropertyToID("_BloomRadius");
    public static readonly int BloomScatterID = Shader.PropertyToID("_BloomScatter");
    public static readonly int BloomTintID = Shader.PropertyToID("_BloomTint");
    public static readonly int BloomIntensityID = Shader.PropertyToID("_BloomIntensity");

    public static readonly int VignetteIntensityID = Shader.PropertyToID("_VignetteIntensity");
    public static readonly int VignetteColorID = Shader.PropertyToID("_VignetteColor");
    public static readonly int VignetteSmoothnessID = Shader.PropertyToID("_VignetteSmoothness");
    public static readonly int VignetteCenterID = Shader.PropertyToID("_VignetteCenter");

    public static readonly int ChromaticAberrationIntensityID = Shader.PropertyToID("_ChromaticAberrationIntensity");

    public static readonly int ExposureID = Shader.PropertyToID("_Exposure");
    public static readonly int ColorFilterID = Shader.PropertyToID("_ColorFilter");
    public static readonly int ContrastID = Shader.PropertyToID("_Contrast");
    public static readonly int SaturationID = Shader.PropertyToID("_Saturation");
    public static readonly int GammaID = Shader.PropertyToID("_Gamma");
    public static readonly int HdrPaperWhiteScaleID = Shader.PropertyToID("_HdrPaperWhiteScale");
    public static readonly int ToneMappingEnabledID = Shader.PropertyToID("_ToneMappingEnabled");
    public static readonly int ToneMappingWhitePointID = Shader.PropertyToID("_ToneMappingWhitePoint");

    public static readonly int EigengrauIntensityID = Shader.PropertyToID("_EigengrauIntensity");
    public static readonly int EigengrauColorID = Shader.PropertyToID("_EigengrauColor");
    public static readonly int EigengrauDarknessThresholdID = Shader.PropertyToID("_EigengrauDarknessThreshold");
    public static readonly int EigengrauNoiseScaleID = Shader.PropertyToID("_EigengrauNoiseScale");
    public static readonly int EigengrauAnimationSpeedID = Shader.PropertyToID("_EigengrauAnimationSpeed");
    public static readonly int TimeID = Shader.PropertyToID("_Time");

    public static readonly int Advanced0ID = Shader.PropertyToID("_Advanced0");
    public static readonly int Advanced1ID = Shader.PropertyToID("_Advanced1");
    public static readonly int Advanced2ID = Shader.PropertyToID("_Advanced2");
    public static readonly int Advanced3ID = Shader.PropertyToID("_Advanced3");
    public static readonly int HistoryTexID = Shader.PropertyToID("_HistoryTex");
    public static readonly int TemporalID = Shader.PropertyToID("_Temporal");

    public static readonly string[] BloomDownNames =
    [
        "_PPBloomDown_0",
    ];

    public static readonly string[] BloomUpNames =
    [
        "_PPBloomUp_0",
    ];
}
