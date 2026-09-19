#nullable enable

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Kern.Rendering.PostProcessing;

internal sealed class PostProcessPassData
{
    public ComputeShader PostProcessCS = null!;
    public bool HDROutput;
    public ColorGamut HDRGamut;
    public int KernelPrefilter;
    public int KernelDownsample;
    public int KernelUpsample;
    public int KernelComposite;
    public int KernelBakeGradeLut = -1;
    public RenderTexture? BakedGradeLut;
    public BakedGradeLutCache? GradeLutCache;
    // Поколение грейда из PostProcessRuntimeState. Оно уже меняется ровно
    // тогда, когда меняется содержимое грейда, — и служит ключом запекания
    // вместо посборного сравнения сотни векторов на каждом кадре.
    public uint GradeGeneration;

    public TextureHandle ColorTexture;
    public TextureHandle IntermediateTexture;
    public TextureHandle BloomPrefilterTexture;
    public TextureHandle[] BloomDownTextures = null!;
    public TextureHandle[] BloomUpTextures = null!;
    public TextureHandle HistoryTexture;
    public int Width;
    public int Height;

    // Проход дисплея и творческий проход грузят разные наборы параметров.
    // Раньше оба грузили все ~70, включая девять массивов кривых, которые
    // творческому проходу не нужны вовсе (они уже запечены в таблицу), а
    // дисплейному не нужны CDL, колёса и квалификатор.
    public bool IsDisplayPass;
    public bool DiagnosticsActive;

    public bool BloomActive;
    // Фактическое число уровней пирамиды в этом кадре: на малом окне нижние
    // уровни вырождаются в один пиксель и считать их незачем.
    public int BloomLevels;
    public float BloomThreshold;
    public float BloomSoftKnee;
    public float BloomRadius;
    public float BloomScatter;
    public Vector4 BloomTint;
    public float BloomIntensity;

    public bool VignetteActive;
    public float VignetteIntensity;
    public Vector4 VignetteColor;
    public float VignetteSmoothness;
    public Vector2 VignetteCenter;

    public bool CgActive;
    public float Exposure;
    public Vector4 ColorFilter;
    public float Contrast;
    public float Saturation;
    public float CdlSaturation;
    public float DisplayPaperWhiteNits;
    public float DisplayPeakRelative;
    public int PostDebugView;
    public float CompareSplit;
    public int CompareMode;
    public bool CompareBefore;
    public Vector2 WhiteBalance;

    public Vector4 CdlSlope;
    public Vector4 CdlOffset;
    public Vector4 CdlPower;
    public Vector3 CdlMaster;
    public Vector4 PrimaryLift;
    public Vector4 PrimaryGamma;
    public Vector4 PrimaryGain;
    public Vector4 PrimaryOffset;
    public Vector4 PrimaryMaster;
    public float Vibrance;
    public float Hue;
    public Vector4 ContrastControls;
    public Vector3 ContrastControls2;
    public Vector4 DisplayGrade0;
    public Vector4 DisplayGrade1;
    public float GamutCompression;
    public Vector4[] MasterCurvePoints = null!;
    public Vector4[] RedCurvePoints = null!;
    public Vector4[] GreenCurvePoints = null!;
    public Vector4[] BlueCurvePoints = null!;
    public Vector4[] HueVsHueCurvePoints = null!;
    public Vector4[] HueVsSaturationCurvePoints = null!;
    public Vector4[] HueVsLuminanceCurvePoints = null!;
    public Vector4[] LuminanceVsSaturationCurvePoints = null!;
    public Vector4[] SaturationVsSaturationCurvePoints = null!;
    public int MasterCurvePointCount;
    public int RedCurvePointCount;
    public int GreenCurvePointCount;
    public int BlueCurvePointCount;
    public int HueVsHueCurvePointCount;
    public int HueVsSaturationCurvePointCount;
    public int HueVsLuminanceCurvePointCount;
    public int LuminanceVsSaturationCurvePointCount;
    public int SaturationVsSaturationCurvePointCount;
    public int CurveInterpolation;
    public Vector4 Qualifier0;
    public Vector4 Qualifier1;
    public Vector4 Qualifier2;
    public Vector4 Qualifier3;
    public Vector4 Qualifier4;
    public Vector4 Qualifier5;
    public Vector4 Qualifier6;
    public Vector4[] QualifierHueSamples = null!;
    public int QualifierHueSampleCount;
    public Texture2D? Lut1D;
    public Texture3D? Lut3D;
    public int LutType;
    public float LutIntensity;
    public int LutColorSpace;
    public Vector3 LutDomainMin;
    public Vector3 LutDomainMax;

    public bool EigengrauActive;
    public float EigengrauIntensity;
    public Vector4 EigengrauColor;
    public float EigengrauDarknessThreshold;
    public float EigengrauNoiseScale;
    public float EigengrauAnimationSpeed;

    public float MotionBlurHistory;
    public bool HistoryValid;
    public bool TemporalActive;

    // clip прошлого кадра из clip текущего: VP_prev * inverse(VP_cur).
    public Matrix4x4 HistoryReprojection;

    // Промежуточная текстура становится цветом камеры вместо копирования обратно.
    public bool SwapColor;
    public float TimeSeconds;
}
