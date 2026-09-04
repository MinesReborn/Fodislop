#nullable enable

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using static Fodinae.Rendering.PostProcessing.PostProcessShaderConstants;

namespace Fodinae.Rendering.PostProcessing;

internal static class PostProcessPassExecutor
{
    public static void Render(PostProcessPassData data, UnsafeGraphContext context)
    {
        var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
        int width = data.Descriptor.width;
        int height = data.Descriptor.height;

        cmd.SetComputeVectorParam(data.PostProcessCS, ScreenSizeID, new Vector4(width, height, 1f / width, 1f / height));

        if (data.BloomActive)
        {
            ExecuteBloom(data, cmd, width, height);
        }
        else
        {
            cmd.SetComputeFloatParam(data.PostProcessCS, BloomIntensityID, 0f);
            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, BloomTexID, Texture2D.blackTexture);
        }

        cmd.SetComputeVectorParam(data.PostProcessCS, ScreenSizeID, new Vector4(width, height, 1f / width, 1f / height));

        BindPostProcessParameters(data, cmd);

        cmd.BeginSample("Fodinae.PostProcess.Composite");
        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, InputTexID, data.ColorTexture);
        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, OutputTexID, data.IntermediateTexture);
        cmd.DispatchCompute(data.PostProcessCS, data.KernelComposite, Mathf.CeilToInt(width / 8f), Mathf.CeilToInt(height / 8f), 1);
        cmd.EndSample("Fodinae.PostProcess.Composite");

        cmd.BeginSample("Fodinae.PostProcess.BlitBack");
        Blitter.BlitCameraTexture(cmd, data.IntermediateTexture, data.ColorTexture);
        if (data.TemporalActive)
        {
            cmd.CopyTexture(data.IntermediateTexture, data.HistoryTexture);
        }

        cmd.EndSample("Fodinae.PostProcess.BlitBack");
    }

    private static void ExecuteBloom(PostProcessPassData data, CommandBuffer cmd, int width, int height)
    {
        cmd.SetComputeFloatParam(data.PostProcessCS, BloomThresholdID, data.BloomThreshold);
        cmd.SetComputeFloatParam(data.PostProcessCS, BloomSoftKneeID, data.BloomSoftKnee);
        cmd.SetComputeFloatParam(data.PostProcessCS, BloomRadiusID, data.BloomRadius);
        cmd.SetComputeFloatParam(data.PostProcessCS, BloomScatterID, data.BloomScatter);
        cmd.SetComputeVectorParam(data.PostProcessCS, BloomTintID, data.BloomTint);
        cmd.SetComputeFloatParam(data.PostProcessCS, BloomIntensityID, data.BloomIntensity);

        int prefilterWidth = Mathf.Max(1, width / 2);
        int prefilterHeight = Mathf.Max(1, height / 2);
        cmd.SetComputeVectorParam(
            data.PostProcessCS,
            ScreenSizeID,
            new Vector4(
                prefilterWidth,
                prefilterHeight,
                1f / prefilterWidth,
                1f / prefilterHeight));
        cmd.SetComputeVectorParam(
            data.PostProcessCS,
            SourceTexelSizeID,
            new Vector4(1f / width, 1f / height, width, height));
        cmd.BeginSample("Fodinae.PostProcess.Bloom.Prefilter");
        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelPrefilter, InputTexID, data.ColorTexture);
        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelPrefilter, DestTexID, data.BloomPrefilterTexture);
        cmd.DispatchCompute(
            data.PostProcessCS,
            data.KernelPrefilter,
            Mathf.CeilToInt(prefilterWidth / 8f),
            Mathf.CeilToInt(prefilterHeight / 8f),
            1);
        cmd.EndSample("Fodinae.PostProcess.Bloom.Prefilter");

        int downWidth = prefilterWidth;
        int downHeight = prefilterHeight;
        int sourceWidth = prefilterWidth;
        int sourceHeight = prefilterHeight;
        TextureHandle currentSource = data.BloomPrefilterTexture;
        cmd.BeginSample("Fodinae.PostProcess.Bloom.Downsample");
        for (int i = 0; i < data.BloomDownTextures.Length; i++)
        {
            downWidth = Mathf.Max(1, downWidth / 2);
            downHeight = Mathf.Max(1, downHeight / 2);
            cmd.SetComputeVectorParam(
                data.PostProcessCS,
                ScreenSizeID,
                new Vector4(downWidth, downHeight, 1f / downWidth, 1f / downHeight));
            cmd.SetComputeVectorParam(
                data.PostProcessCS,
                SourceTexelSizeID,
                new Vector4(1f / sourceWidth, 1f / sourceHeight, sourceWidth, sourceHeight));
            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelDownsample, SourceTexID, currentSource);
            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelDownsample, DestTexID, data.BloomDownTextures[i]);
            cmd.DispatchCompute(
                data.PostProcessCS,
                data.KernelDownsample,
                Mathf.CeilToInt(downWidth / 8f),
                Mathf.CeilToInt(downHeight / 8f),
                1);
            currentSource = data.BloomDownTextures[i];
            sourceWidth = downWidth;
            sourceHeight = downHeight;
        }

        cmd.EndSample("Fodinae.PostProcess.Bloom.Downsample");

        TextureHandle currentUp = data.BloomDownTextures[^1];
        int currentUpWidth = downWidth;
        int currentUpHeight = downHeight;
        cmd.BeginSample("Fodinae.PostProcess.Bloom.Upsample");
        for (int i = data.BloomUpTextures.Length - 1; i >= 0; i--)
        {
            int upWidth = Mathf.Max(1, width >> (i + 1));
            int upHeight = Mathf.Max(1, height >> (i + 1));
            TextureHandle baseTexture = i == 0
                ? data.BloomPrefilterTexture
                : data.BloomDownTextures[i - 1];
            cmd.SetComputeVectorParam(
                data.PostProcessCS,
                ScreenSizeID,
                new Vector4(upWidth, upHeight, 1f / upWidth, 1f / upHeight));
            cmd.SetComputeVectorParam(
                data.PostProcessCS,
                SourceTexelSizeID,
                new Vector4(1f / currentUpWidth, 1f / currentUpHeight, currentUpWidth, currentUpHeight));
            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelUpsample, SourceTexID, currentUp);
            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelUpsample, BaseTexID, baseTexture);
            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelUpsample, DestTexID, data.BloomUpTextures[i]);
            cmd.DispatchCompute(
                data.PostProcessCS,
                data.KernelUpsample,
                Mathf.CeilToInt(upWidth / 8f),
                Mathf.CeilToInt(upHeight / 8f),
                1);
            currentUp = data.BloomUpTextures[i];
            currentUpWidth = upWidth;
            currentUpHeight = upHeight;
        }

        cmd.EndSample("Fodinae.PostProcess.Bloom.Upsample");

        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, BloomTexID, currentUp);
    }

    private static void BindPostProcessParameters(PostProcessPassData data, CommandBuffer cmd)
    {
        cmd.SetComputeFloatParam(data.PostProcessCS, VignetteIntensityID, data.VignetteActive ? data.VignetteIntensity : 0f);
        if (data.VignetteActive)
        {
            cmd.SetComputeVectorParam(data.PostProcessCS, VignetteColorID, data.VignetteColor);
            cmd.SetComputeFloatParam(data.PostProcessCS, VignetteSmoothnessID, data.VignetteSmoothness);
            cmd.SetComputeVectorParam(data.PostProcessCS, VignetteCenterID, data.VignetteCenter);
        }

        cmd.SetComputeFloatParam(data.PostProcessCS, ChromaticAberrationIntensityID, data.CaActive ? data.CaIntensity : 0f);

        cmd.SetComputeFloatParam(data.PostProcessCS, ExposureID, data.CgActive ? data.Exposure : 0f);
        cmd.SetComputeVectorParam(data.PostProcessCS, ColorFilterID, data.CgActive ? data.ColorFilter : Color.white);
        cmd.SetComputeFloatParam(data.PostProcessCS, ContrastID, data.CgActive ? data.Contrast : 0f);
        cmd.SetComputeFloatParam(data.PostProcessCS, SaturationID, data.CgActive ? data.Saturation : 1f);
        cmd.SetComputeFloatParam(data.PostProcessCS, GammaID, data.Gamma);
        cmd.SetComputeFloatParam(data.PostProcessCS, HdrPaperWhiteScaleID, data.HdrPaperWhiteScale);
        cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauIntensityID, data.EigengrauActive ? data.EigengrauIntensity : 0f);
        if (data.EigengrauActive)
        {
            cmd.SetComputeVectorParam(data.PostProcessCS, EigengrauColorID, data.EigengrauColor);
            cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauDarknessThresholdID, data.EigengrauDarknessThreshold);
            cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauNoiseScaleID, data.EigengrauNoiseScale);
            cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauAnimationSpeedID, data.EigengrauAnimationSpeed);
            cmd.SetComputeFloatParam(data.PostProcessCS, TimeID, data.TimeSeconds);
        }

        cmd.SetComputeVectorParam(data.PostProcessCS, Advanced0ID, data.Advanced0);
        cmd.SetComputeVectorParam(data.PostProcessCS, Advanced1ID, data.Advanced1);
        cmd.SetComputeVectorParam(data.PostProcessCS, Advanced2ID, data.Advanced2);
        cmd.SetComputeVectorParam(data.PostProcessCS, Advanced3ID, data.Advanced3);
        cmd.SetComputeFloatParam(data.PostProcessCS, TimeID, data.TimeSeconds);
        cmd.SetComputeVectorParam(data.PostProcessCS, TemporalID, data.Temporal);
        if (data.TemporalActive && data.HistoryValid)
        {
            cmd.SetComputeTextureParam(
                data.PostProcessCS,
                data.KernelComposite,
                HistoryTexID,
                data.HistoryTexture);
        }
        else
        {
            cmd.SetComputeTextureParam(
                data.PostProcessCS,
                data.KernelComposite,
                HistoryTexID,
                Texture2D.blackTexture);
        }
    }
}
