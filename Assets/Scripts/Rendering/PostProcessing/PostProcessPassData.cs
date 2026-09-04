#nullable enable

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Fodinae.Rendering.PostProcessing;

internal sealed class PostProcessPassData
{
    public ComputeShader PostProcessCS = null!;
    public int KernelPrefilter;
    public int KernelDownsample;
    public int KernelUpsample;
    public int KernelComposite;

    public TextureHandle ColorTexture;
    public TextureHandle IntermediateTexture;
    public TextureHandle BloomPrefilterTexture;
    public TextureHandle[] BloomDownTextures = null!;
    public TextureHandle[] BloomUpTextures = null!;
    public TextureHandle HistoryTexture;
    public RenderTextureDescriptor Descriptor;

    public bool BloomActive;
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

    public bool CaActive;
    public float CaIntensity;

    public bool CgActive;
    public float Exposure;
    public Vector4 ColorFilter;
    public float Contrast;
    public float Saturation;
    public float Gamma;
    public float HdrPaperWhiteScale;

    public bool EigengrauActive;
    public float EigengrauIntensity;
    public Vector4 EigengrauColor;
    public float EigengrauDarknessThreshold;
    public float EigengrauNoiseScale;
    public float EigengrauAnimationSpeed;

    public Vector4 Advanced0;
    public Vector4 Advanced1;
    public Vector4 Advanced2;
    public Vector4 Advanced3;
    public Vector4 Temporal;
    public bool HistoryValid;
    public bool TemporalActive;
    public float TimeSeconds;
}
