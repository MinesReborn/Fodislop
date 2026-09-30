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
    public static readonly int BloomStorageFormatID = Shader.PropertyToID("_BloomStorageFormat");
    public static readonly int EmissionTexID = Shader.PropertyToID("_EmissionTex");
    public static readonly int ScreenToEmissionID = Shader.PropertyToID("_ScreenToEmission");
    public static readonly int WorldEmissionTextureID = Shader.PropertyToID("_WorldEmissionTexture");
    public static readonly int WorldLightRectID = Shader.PropertyToID("_WorldLightRect");

    public static readonly int VignetteIntensityID = Shader.PropertyToID("_VignetteIntensity");
    public static readonly int VignetteColorID = Shader.PropertyToID("_VignetteColor");
    public static readonly int VignetteSmoothnessID = Shader.PropertyToID("_VignetteSmoothness");
    public static readonly int VignetteCenterID = Shader.PropertyToID("_VignetteCenter");

    public static readonly int DisplayPaperWhiteNitsID = Shader.PropertyToID("_DisplayPaperWhiteNits");
    public static readonly int DisplayPeakRelativeID = Shader.PropertyToID("_DisplayPeakRelative");
    public static readonly int PostDebugViewID = Shader.PropertyToID("_PostDebugView");
    public static readonly int CompareSplitID = Shader.PropertyToID("_CompareSplit");
    public static readonly int CompareModeID = Shader.PropertyToID("_CompareMode");
    public static readonly int CompareBeforeID = Shader.PropertyToID("_CompareBefore");
    public static readonly int Lut3DID = Shader.PropertyToID("_GradeLut3D");
    public static readonly int LutParamsID = Shader.PropertyToID("_GradeLutParams");
    public static readonly int LutDomainMinID = Shader.PropertyToID("_GradeLutDomainMin");
    public static readonly int LutDomainMaxID = Shader.PropertyToID("_GradeLutDomainMax");

    public static readonly int EigengrauIntensityID = Shader.PropertyToID("_EigengrauIntensity");
    public static readonly int EigengrauColorID = Shader.PropertyToID("_EigengrauColor");
    public static readonly int EigengrauDarknessThresholdID = Shader.PropertyToID("_EigengrauDarknessThreshold");
    public static readonly int EigengrauNoiseScaleID = Shader.PropertyToID("_EigengrauNoiseScale");
    public static readonly int EigengrauNoiseAmplitudeID = Shader.PropertyToID("_EigengrauNoiseAmplitude");
    public static readonly int FrameIndexID = Shader.PropertyToID("_FrameIndex");
    public static readonly int CalibrationPatternID = Shader.PropertyToID("_CalibrationPattern");
    public static readonly int CalibrationValueID = Shader.PropertyToID("_CalibrationValue");

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
