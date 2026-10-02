#nullable enable

using UnityEngine;

namespace Kern.Rendering.PostProcessing;

internal static class PostProcessShaderConstants
{
    public const string PassName = "ComputePostProcessPass";

    public static readonly int InputTexId = Shader.PropertyToID("_InputTex");
    public static readonly int OutputTexId = Shader.PropertyToID("_OutputTex");
    public static readonly int ScreenSizeId = Shader.PropertyToID("_ScreenSize");


    public static readonly int VignetteIntensityId = Shader.PropertyToID("_VignetteIntensity");
    public static readonly int VignetteColorId = Shader.PropertyToID("_VignetteColor");
    public static readonly int VignetteSmoothnessId = Shader.PropertyToID("_VignetteSmoothness");
    public static readonly int VignetteCenterId = Shader.PropertyToID("_VignetteCenter");

    public static readonly int DisplayPaperWhiteNitsId = Shader.PropertyToID("_DisplayPaperWhiteNits");
    public static readonly int DisplayPeakRelativeId = Shader.PropertyToID("_DisplayPeakRelative");
    public static readonly int PostDebugViewId = Shader.PropertyToID("_PostDebugView");
    public static readonly int CompareSplitId = Shader.PropertyToID("_CompareSplit");
    public static readonly int CompareModeId = Shader.PropertyToID("_CompareMode");
    public static readonly int CompareBeforeId = Shader.PropertyToID("_CompareBefore");
    public static readonly int LUT3DId = Shader.PropertyToID("_GradeLut3D");
    public static readonly int LUTParamsId = Shader.PropertyToID("_GradeLutParams");
    public static readonly int LUTDomainMinId = Shader.PropertyToID("_GradeLutDomainMin");
    public static readonly int LUTDomainMaxId = Shader.PropertyToID("_GradeLutDomainMax");

    public static readonly int EigengrauIntensityId = Shader.PropertyToID("_EigengrauIntensity");
    public static readonly int EigengrauColorId = Shader.PropertyToID("_EigengrauColor");
    public static readonly int EigengrauDarknessThresholdId = Shader.PropertyToID("_EigengrauDarknessThreshold");
    public static readonly int EigengrauNoiseScaleId = Shader.PropertyToID("_EigengrauNoiseScale");
    public static readonly int EigengrauNoiseAmplitudeId = Shader.PropertyToID("_EigengrauNoiseAmplitude");
    public static readonly int FrameIndexId = Shader.PropertyToID("_FrameIndex");
    public static readonly int CalibrationPatternId = Shader.PropertyToID("_CalibrationPattern");
    public static readonly int CalibrationValueId = Shader.PropertyToID("_CalibrationValue");

    // Ключевое слово отладочных видов и шторки сравнения. Вне инструмента
    // колориста вариант не включается, и весь этот код в kernel не попадает.
    public const string DiagnosticsKeyword = "KERN_POST_DIAGNOSTICS";

}
