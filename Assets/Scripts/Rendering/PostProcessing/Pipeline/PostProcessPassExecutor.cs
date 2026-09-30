#nullable enable

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using static Kern.Rendering.PostProcessing.PostProcessShaderConstants;

namespace Kern.Rendering.PostProcessing;

internal static class PostProcessPassExecutor
{
    private static Texture3D? _identityLut3D;

    // LocalKeyword ищет слово в шейдере по имени; кэшируем вместе со ссылкой
    // на сам шейдер, чтобы не искать его дважды за кадр.
    private static ComputeShader? _keywordShader;
    private static LocalKeyword _diagnosticsKeyword;

    public static void Render(PostProcessPassData data, UnsafeGraphContext context)
    {
        HDROutputUtils.ConfigureHDROutput(data.PostProcessCS, data.HDRGamut,
            data.HDROutput ? HDROutputUtils.Operation.ColorConversion : HDROutputUtils.Operation.None);
        var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
        int width = data.Width;
        int height = data.Height;

        SetDiagnosticsKeyword(data, cmd);
        int bloomDispatches = 0;

        if (data.BloomActive)
        {
            bloomDispatches = ExecuteBloom(data, cmd, width, height);
        }
        else if (!data.IsDisplayPass)
        {
            cmd.SetComputeFloatParam(data.PostProcessCS, BloomIntensityID, 0f);
            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, BloomTexID, Texture2D.blackTexture);
        }

        // Один раз на проход. Блум оставляет здесь размер своего последнего
        // уровня, поэтому композиту размер кадра ставится после него, а не до.
        cmd.SetComputeVectorParam(data.PostProcessCS, ScreenSizeID, new Vector4(width, height, 1f / width, 1f / height));

        if (data.IsDisplayPass)
        {
            BindDisplayParameters(data, cmd);
        }

        if (!data.BloomActive || data.UnfusedBloom)
        {
            cmd.BeginSample("Kern.PostProcess.Composite");
            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, InputTexID, data.ColorTexture);
            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, OutputTexID, data.IntermediateTexture);
            cmd.DispatchCompute(data.PostProcessCS, data.KernelComposite, Mathf.CeilToInt(width / 8f), Mathf.CeilToInt(height / 8f), 1);
            if (data.BloomActive)
            {
                bloomDispatches++;
            }
            cmd.EndSample("Kern.PostProcess.Composite");
        }

        if (!data.IsDisplayPass)
        {
            PostProcessRuntimeState.RecordBloomDispatches(bloomDispatches);
        }

        if (!data.SwapColor)
        {
            cmd.BeginSample("Kern.PostProcess.BlitBack");
            Blitter.BlitCameraTexture(cmd, data.IntermediateTexture, data.ColorTexture);
            cmd.EndSample("Kern.PostProcess.BlitBack");
        }
    }

    private static void SetDiagnosticsKeyword(PostProcessPassData data, CommandBuffer cmd)
    {
        if (!ReferenceEquals(_keywordShader, data.PostProcessCS))
        {
            _keywordShader = data.PostProcessCS;
            _diagnosticsKeyword = new LocalKeyword(data.PostProcessCS, DiagnosticsKeyword);
        }

        cmd.SetKeyword(data.PostProcessCS, _diagnosticsKeyword, data.DiagnosticsActive);
    }

    private static int ExecuteBloom(PostProcessPassData data, CommandBuffer cmd, int width, int height)
    {
        int dispatches = 0;
        int levels = data.BloomLevels;
        float scatter = data.BloomScatter;

        cmd.SetComputeFloatParam(data.PostProcessCS, BloomThresholdID, data.BloomThreshold);
        cmd.SetComputeFloatParam(data.PostProcessCS, BloomSoftKneeID, data.BloomSoftKnee);
        cmd.SetComputeFloatParam(data.PostProcessCS, BloomRadiusID, data.BloomRadius);
        cmd.SetComputeFloatParam(data.PostProcessCS, BloomScatterID, scatter);
        cmd.SetComputeVectorParam(data.PostProcessCS, BloomTintID, data.BloomTint);

        // Яркость подъёма нормируется по глубине пирамиды.
        //
        // Подъём складывает `highRes + lowRes * scatter` на каждом уровне,
        // поэтому суммарный вес равен 1 + s + s^2 + ... + s^levels. Без
        // нормировки добавление уровня само по себе делало блум ярче, и
        // «интенсивность» означала разное при разной глубине. Теперь она
        // означает долю добавленного света и не зависит от числа уровней.
        float energy = 0f;
        float term = 1f;
        for (int i = 0; i <= levels; i++)
        {
            energy += term;
            term *= Mathf.Max(scatter, 0f);
        }

        cmd.SetComputeFloatParam(
            data.PostProcessCS,
            BloomIntensityID,
            data.BloomIntensity / Mathf.Max(energy, 1e-4f));

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
        cmd.SetComputeVectorParam(data.PostProcessCS, ScreenToEmissionID, data.ScreenToEmission);
        Texture emissionTex = Shader.GetGlobalTexture(WorldEmissionTextureID) ?? Texture2D.blackTexture;
        cmd.BeginSample("Kern.PostProcess.Bloom.Prefilter");
        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelPrefilter, InputTexID, data.ColorTexture);
        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelPrefilter, EmissionTexID, emissionTex);
        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelPrefilter, DestTexID, data.BloomPrefilterTexture);
        cmd.DispatchCompute(
            data.PostProcessCS,
            data.KernelPrefilter,
            Mathf.CeilToInt(prefilterWidth / 8f),
            Mathf.CeilToInt(prefilterHeight / 8f),
            1);
        cmd.EndSample("Kern.PostProcess.Bloom.Prefilter");
        dispatches++;

        int downWidth = prefilterWidth;
        int downHeight = prefilterHeight;
        int sourceWidth = prefilterWidth;
        int sourceHeight = prefilterHeight;
        TextureHandle currentSource = data.BloomPrefilterTexture;
        cmd.BeginSample("Kern.PostProcess.Bloom.Downsample");
        for (int i = 0; i < levels; i++)
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
            dispatches++;
            sourceWidth = downWidth;
            sourceHeight = downHeight;
        }

        cmd.EndSample("Kern.PostProcess.Bloom.Downsample");

        TextureHandle currentUp = data.BloomDownTextures[levels - 1];
        int currentUpWidth = downWidth;
        int currentUpHeight = downHeight;
        cmd.BeginSample("Kern.PostProcess.Bloom.Upsample");
        for (int i = levels - 1; i >= (data.UnfusedBloom ? 0 : 1); i--)
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
            dispatches++;
            currentUpWidth = upWidth;
            currentUpHeight = upHeight;
        }

        cmd.EndSample("Kern.PostProcess.Bloom.Upsample");

        if (data.UnfusedBloom)
        {
            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, BloomTexID, currentUp);
            return dispatches;
        }

        int kernel = data.KernelUpsampleComposite;
        cmd.BeginSample("Kern.PostProcess.Bloom.UpsampleComposite");
        cmd.SetComputeVectorParam(data.PostProcessCS, ScreenSizeID, new Vector4(width, height, 1f / width, 1f / height));
        cmd.SetComputeVectorParam(data.PostProcessCS, SourceTexelSizeID,
            new Vector4(1f / currentUpWidth, 1f / currentUpHeight, currentUpWidth, currentUpHeight));
        cmd.SetComputeIntParam(data.PostProcessCS, BloomStorageFormatID, data.BloomStorageFormat);
        cmd.SetComputeTextureParam(data.PostProcessCS, kernel, SourceTexID, currentUp);
        cmd.SetComputeTextureParam(data.PostProcessCS, kernel, BaseTexID, data.BloomPrefilterTexture);
        cmd.SetComputeTextureParam(data.PostProcessCS, kernel, InputTexID, data.ColorTexture);
        cmd.SetComputeTextureParam(data.PostProcessCS, kernel, OutputTexID, data.IntermediateTexture);
        cmd.DispatchCompute(data.PostProcessCS, kernel, (width + 15) / 16, (height + 15) / 16, 1);
        cmd.EndSample("Kern.PostProcess.Bloom.UpsampleComposite");
        return dispatches + 1;
    }

    // Точечные операции вывода: LUT, виньетка, зерно, калибровка, шторка
    // сравнения. Сценическому проходу из этого не нужно ничего.
    private static void BindDisplayParameters(PostProcessPassData data, CommandBuffer cmd)
    {
        cmd.SetComputeFloatParam(data.PostProcessCS, CompareSplitID, data.CompareSplit);
        cmd.SetComputeIntParam(data.PostProcessCS, CompareModeID, data.CompareMode);
        cmd.SetComputeIntParam(data.PostProcessCS, CompareBeforeID, data.CompareBefore ? 1 : 0);
        cmd.SetComputeFloatParam(data.PostProcessCS, VignetteIntensityID, data.VignetteActive ? data.VignetteIntensity : 0f);
        if (data.VignetteActive)
        {
            cmd.SetComputeVectorParam(data.PostProcessCS, VignetteColorID, data.VignetteColor);
            cmd.SetComputeFloatParam(data.PostProcessCS, VignetteSmoothnessID, data.VignetteSmoothness);
            cmd.SetComputeVectorParam(data.PostProcessCS, VignetteCenterID, data.VignetteCenter);
        }

        // Keep the shader finite even if a stale/partially initialized HDR
        // output profile reaches the pass before display reconciliation.
        cmd.SetComputeFloatParam(
            data.PostProcessCS,
            DisplayPaperWhiteNitsID,
            Mathf.Max(data.DisplayPaperWhiteNits, 1f));
        cmd.SetComputeFloatParam(
            data.PostProcessCS,
            DisplayPeakRelativeID,
            data.DisplayPeakRelative);
        cmd.SetComputeIntParam(data.PostProcessCS, PostDebugViewID, data.PostDebugView);
        cmd.SetComputeVectorParam(
            data.PostProcessCS,
            LutParamsID,
            new Vector4(
                data.LutIntensity,
                data.LutColorSpace,
                data.Lut3D != null ? data.Lut3D.width : 0f,
                0f));
        cmd.SetComputeVectorParam(data.PostProcessCS, LutDomainMinID, data.LutDomainMin);
        cmd.SetComputeVectorParam(data.PostProcessCS, LutDomainMaxID, data.LutDomainMax);
        // Metal validates every resource declared by a compute kernel, even when
        // the Lut branch is disabled by intensity. Bind an explicit identity
        // LUT so the neutral frame remains mathematically unchanged.
        cmd.SetComputeTextureParam(
            data.PostProcessCS,
            data.KernelComposite,
            Lut3DID,
            data.Lut3D ?? GetIdentityLut3D());
        cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauIntensityID, data.EigengrauActive ? data.EigengrauIntensity : 0f);
        if (data.EigengrauActive)
        {
            cmd.SetComputeVectorParam(data.PostProcessCS, EigengrauColorID, data.EigengrauColor);
            cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauDarknessThresholdID, data.EigengrauDarknessThreshold);
            cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauNoiseScaleID, data.EigengrauNoiseScale);
            cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauNoiseAmplitudeID, data.EigengrauNoiseAmplitude);
        }

        cmd.SetComputeFloatParam(data.PostProcessCS, FrameIndexID, data.FrameIndex);
        cmd.SetComputeFloatParam(data.PostProcessCS, CalibrationPatternID, data.CalibrationPattern);
        cmd.SetComputeFloatParam(data.PostProcessCS, CalibrationValueID, data.CalibrationValue);
    }

    private static Texture3D GetIdentityLut3D()
    {
        if (_identityLut3D != null)
        {
            return _identityLut3D;
        }

        _identityLut3D = RuntimeTextureFactory.CreateRGBAFloat3DNoMip(
            2,
            "PostProcess_IdentityLut3D",
            FilterMode.Bilinear,
            TextureWrapMode.Clamp);
        _identityLut3D.SetPixels(
        [
            Color.black,
            new Color(1f, 0f, 0f, 1f),
            new Color(0f, 1f, 0f, 1f),
            new Color(1f, 1f, 0f, 1f),
            new Color(0f, 0f, 1f, 1f),
            new Color(1f, 0f, 1f, 1f),
            new Color(0f, 1f, 1f, 1f),
            Color.white,
        ]);
        _identityLut3D.Apply(false, true);
        return _identityLut3D;
    }
}
