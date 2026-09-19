#nullable enable

using UnityEngine;

namespace Kern.Rendering.PostProcessing;

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

    public static readonly int ExposureID = Shader.PropertyToID("_Exposure");
    public static readonly int ColorFilterID = Shader.PropertyToID("_ColorFilter");
    public static readonly int ContrastID = Shader.PropertyToID("_Contrast");
    public static readonly int SaturationID = Shader.PropertyToID("_Saturation");
    public static readonly int CdlSaturationID = Shader.PropertyToID("_CdlSaturation");
    public static readonly int DisplayPaperWhiteNitsID = Shader.PropertyToID("_DisplayPaperWhiteNits");
    public static readonly int DisplayPeakRelativeID = Shader.PropertyToID("_DisplayPeakRelative");
    public static readonly int PostDebugViewID = Shader.PropertyToID("_PostDebugView");
    public static readonly int CompareSplitID = Shader.PropertyToID("_CompareSplit");
    public static readonly int CompareModeID = Shader.PropertyToID("_CompareMode");
    public static readonly int CompareBeforeID = Shader.PropertyToID("_CompareBefore");
    public static readonly int WhiteBalanceID = Shader.PropertyToID("_WhiteBalance");
    public static readonly int CdlSlopeID = Shader.PropertyToID("_CdlSlope");
    public static readonly int CdlOffsetID = Shader.PropertyToID("_CdlOffset");
    public static readonly int CdlPowerID = Shader.PropertyToID("_CdlPower");
    public static readonly int CdlMasterID = Shader.PropertyToID("_CdlMaster");
    public static readonly int PrimaryLiftID = Shader.PropertyToID("_PrimaryLift");
    public static readonly int PrimaryGammaID = Shader.PropertyToID("_PrimaryGamma");
    public static readonly int PrimaryGainID = Shader.PropertyToID("_PrimaryGain");
    public static readonly int PrimaryOffsetID = Shader.PropertyToID("_PrimaryOffset");
    public static readonly int PrimaryMasterID = Shader.PropertyToID("_PrimaryMaster");
    public static readonly int VibranceID = Shader.PropertyToID("_Vibrance");
    public static readonly int HueID = Shader.PropertyToID("_Hue");
    public static readonly int ContrastControlsID = Shader.PropertyToID("_ContrastControls");
    public static readonly int ContrastControls2ID = Shader.PropertyToID("_ContrastControls2");
    public static readonly int DisplayGrade0ID = Shader.PropertyToID("_DisplayGrade0");
    public static readonly int DisplayGrade1ID = Shader.PropertyToID("_DisplayGrade1");
    public static readonly int GamutCompressionID = Shader.PropertyToID("_GamutCompression");
    public static readonly int BakedGradeLutID = Shader.PropertyToID("_BakedGradeLut");
    public static readonly int BakedGradeLutTexID = Shader.PropertyToID("_BakedGradeLutTex");
    public static readonly int MasterCurveID = Shader.PropertyToID("_MasterCurve");
    public static readonly int RedCurveID = Shader.PropertyToID("_RedCurve");
    public static readonly int GreenCurveID = Shader.PropertyToID("_GreenCurve");
    public static readonly int BlueCurveID = Shader.PropertyToID("_BlueCurve");
    public static readonly int HueVsHueCurveID = Shader.PropertyToID("_HueVsHueCurve");
    public static readonly int HueVsSaturationCurveID = Shader.PropertyToID("_HueVsSaturationCurve");
    public static readonly int HueVsLuminanceCurveID = Shader.PropertyToID("_HueVsLuminanceCurve");
    public static readonly int LuminanceVsSaturationCurveID = Shader.PropertyToID("_LuminanceVsSaturationCurve");
    public static readonly int SaturationVsSaturationCurveID = Shader.PropertyToID("_SaturationVsSaturationCurve");
    public static readonly int MasterCurvePointCountID = Shader.PropertyToID("_MasterCurvePointCount");
    public static readonly int RedCurvePointCountID = Shader.PropertyToID("_RedCurvePointCount");
    public static readonly int GreenCurvePointCountID = Shader.PropertyToID("_GreenCurvePointCount");
    public static readonly int BlueCurvePointCountID = Shader.PropertyToID("_BlueCurvePointCount");
    public static readonly int HueVsHueCurvePointCountID = Shader.PropertyToID("_HueVsHueCurvePointCount");
    public static readonly int HueVsSaturationCurvePointCountID = Shader.PropertyToID("_HueVsSaturationCurvePointCount");
    public static readonly int HueVsLuminanceCurvePointCountID = Shader.PropertyToID("_HueVsLuminanceCurvePointCount");
    public static readonly int LuminanceVsSaturationCurvePointCountID = Shader.PropertyToID("_LuminanceVsSaturationCurvePointCount");
    public static readonly int SaturationVsSaturationCurvePointCountID = Shader.PropertyToID("_SaturationVsSaturationCurvePointCount");
    public static readonly int CurveInterpolationID = Shader.PropertyToID("_CurveInterpolation");
    public static readonly int Qualifier0ID = Shader.PropertyToID("_Qualifier0");
    public static readonly int Qualifier1ID = Shader.PropertyToID("_Qualifier1");
    public static readonly int Qualifier2ID = Shader.PropertyToID("_Qualifier2");
    public static readonly int Qualifier3ID = Shader.PropertyToID("_Qualifier3");
    public static readonly int Qualifier4ID = Shader.PropertyToID("_Qualifier4");
    public static readonly int Qualifier5ID = Shader.PropertyToID("_Qualifier5");
    public static readonly int Qualifier6ID = Shader.PropertyToID("_Qualifier6");
    public static readonly int QualifierHueSamplesID = Shader.PropertyToID("_QualifierHueSamples");
    public static readonly int QualifierHueSampleCountID = Shader.PropertyToID("_QualifierHueSampleCount");
    public static readonly int Lut1DID = Shader.PropertyToID("_GradeLut1D");
    public static readonly int Lut3DID = Shader.PropertyToID("_GradeLut3D");
    public static readonly int LutParamsID = Shader.PropertyToID("_GradeLutParams");
    public static readonly int LutDomainMinID = Shader.PropertyToID("_GradeLutDomainMin");
    public static readonly int LutDomainMaxID = Shader.PropertyToID("_GradeLutDomainMax");

    public static readonly int EigengrauIntensityID = Shader.PropertyToID("_EigengrauIntensity");
    public static readonly int EigengrauColorID = Shader.PropertyToID("_EigengrauColor");
    public static readonly int EigengrauDarknessThresholdID = Shader.PropertyToID("_EigengrauDarknessThreshold");
    public static readonly int EigengrauNoiseScaleID = Shader.PropertyToID("_EigengrauNoiseScale");
    public static readonly int EigengrauAnimationSpeedID = Shader.PropertyToID("_EigengrauAnimationSpeed");
    public static readonly int TimeID = Shader.PropertyToID("_Time");

    public static readonly int HistoryTexID = Shader.PropertyToID("_HistoryTex");
    public static readonly int MotionBlurHistoryID = Shader.PropertyToID("_MotionBlurHistory");
    public static readonly int HistoryReprojectionID = Shader.PropertyToID("_HistoryReprojection");

    // Ключевое слово отладочных видов и шторки сравнения. Вне инструмента
    // колориста вариант не включается, и весь этот код в kernel не попадает.
    public const string DiagnosticsKeyword = "KERN_POST_DIAGNOSTICS";

    // Глубина пирамиды блума. Уровней было по одному: цепочка «половина ->
    // четверть -> обратно» давала охват порядка радиуса на четверти
    // разрешения, то есть около десятка пикселей полного кадра. Такой блум
    // физически не мог дать ореола — его нечем было раздуть, сколько ни
    // крути радиус.
    //
    // Длины обоих массивов обязаны совпадать: проход строит цепочку так, что
    // источник для уровня подъёма i лежит на уровне i+1, и последний спуск
    // должен попасть ровно под первый подъём.
    public static readonly string[] BloomDownNames =
    [
        "_PPBloomDown_0",
        "_PPBloomDown_1",
        "_PPBloomDown_2",
        "_PPBloomDown_3",
    ];

    public static readonly string[] BloomUpNames =
    [
        "_PPBloomUp_0",
        "_PPBloomUp_1",
        "_PPBloomUp_2",
        "_PPBloomUp_3",
    ];
}
