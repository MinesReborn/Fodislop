#nullable enable

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Kern.Rendering.PostProcessing;

internal sealed class PostProcessPassData
{
    public EntityId CameraId;
    public PostProcessWorkload Workload = null!;
    public int CreatedTextureCount;
    public long CreatedTexturePayloadBytes;
    public Vector4 DisplaySourceUv = new(1, 1, 0, 0);
    public bool DisplayLinearFilter;
    public bool ScalesWorldGrid;
    public Vector4 WorldGridRect;
    public Vector4 DisplayWorldToViewportUv = new(1, 1, 0, 0);
    public float DisplayViewportAspect;
    public ComputeShader PostProcessCS = null!;
    public bool HDROutput;
    public ColorGamut HDRGamut;
    public int KernelPrefilter;
    public int KernelDownsample;
    public int KernelUpsample;
    public int KernelComposite;
    public int KernelUpsampleComposite;
    public int BloomStorageFormat;
    public bool UnfusedBloom;

    public TextureHandle ColorTexture;
    public TextureHandle IntermediateTexture;
    public TextureHandle BloomPrefilterTexture;
    public TextureHandle[] BloomDownTextures = null!;
    public TextureHandle[] BloomUpTextures = null!;
    public int Width;
    public int Height;

    // Проход дисплея и сценический проход грузят разные наборы параметров.
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
    public Vector4 ScreenToEmission;

    public bool VignetteActive;
    // World-grid frames: the vignette is applied per screen pixel by the
    // final blit; DisplayFinal still computes its mask for the debug view.
    public bool VignetteInFinalBlit;
    public float VignetteIntensity;
    public Vector4 VignetteColor;
    public float VignetteSmoothness;
    public Vector2 VignetteCenter;

    public float DisplayPaperWhiteNits;
    public float DisplayPeakRelative;
    public int PostDebugView;
    public float CompareSplit;
    public int CompareMode;
    public bool CompareBefore;

    public Texture3D? Lut3D;
    public float LutIntensity;
    public int LutColorSpace;
    public Vector3 LutDomainMin;
    public Vector3 LutDomainMax;

    public bool EigengrauActive;
    public float EigengrauIntensity;
    public Vector4 EigengrauColor;
    public float EigengrauDarknessThreshold;
    public float EigengrauNoiseScale;
    public float EigengrauNoiseAmplitude;

    // Промежуточная текстура становится цветом камеры вместо копирования обратно.
    public bool SwapColor;

    // Номер кадра, а не время: узор зерна эйгенграу обязан меняться ровно
    // раз в кадр, и привязка к секундам этого не даёт — при любой частоте
    // кадров выше заданной узор бы держался по нескольку кадров подряд.
    public float FrameIndex;

    // Калибровочный узор: 0 — нет, 1 — белая точка, 2 — лестница пика.
    public int CalibrationPattern;
    public float CalibrationValue;
}
