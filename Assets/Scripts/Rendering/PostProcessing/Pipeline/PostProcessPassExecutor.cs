#nullable enable

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using static Kern.Rendering.PostProcessing.PostProcessShaderConstants;

namespace Kern.Rendering.PostProcessing;

internal static class PostProcessPassExecutor
{
    private static Texture2D? _identityLut1D;
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

        if (data.BloomActive)
        {
            ExecuteBloom(data, cmd, width, height);
        }
        else if (!data.IsDisplayPass)
        {
            cmd.SetComputeFloatParam(data.PostProcessCS, BloomIntensityID, 0f);
            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, BloomTexID, Texture2D.blackTexture);
        }

        // Один раз на проход. Блум оставляет здесь размер своего последнего
        // уровня, поэтому композиту размер кадра ставится после него, а не до.
        cmd.SetComputeVectorParam(data.PostProcessCS, ScreenSizeID, new Vector4(width, height, 1f / width, 1f / height));

        BindSharedParameters(data, cmd);
        if (data.IsDisplayPass)
        {
            BindDisplayParameters(data, cmd);
        }
        else
        {
            BindCreativeParameters(data, cmd);
        }

        if (data.KernelBakeGradeLut >= 0 && data.BakedGradeLut != null)
        {
            if (data.GradeLutCache == null || !data.GradeLutCache.Matches(data))
            {
                // Ключ сохраняется при исполнении прохода после записи
                // dispatch, а не при построении потенциально неисполненного графа.
                cmd.BeginSample("Kern.PostProcess.BakeGradeLut");
                int groups = Mathf.CeilToInt(PostProcessRenderPass.BakedGradeLutSize / 4f);
                cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelBakeGradeLut, BakedGradeLutID, data.BakedGradeLut);
                cmd.DispatchCompute(data.PostProcessCS, data.KernelBakeGradeLut, groups, groups, groups);
                cmd.EndSample("Kern.PostProcess.BakeGradeLut");
                data.GradeLutCache?.Store(data);
            }

            // Привязка нужна и в кадрах, использующих уже готовую таблицу.
            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, BakedGradeLutTexID, data.BakedGradeLut);
        }

        cmd.BeginSample("Kern.PostProcess.Composite");
        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, InputTexID, data.ColorTexture);
        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, OutputTexID, data.IntermediateTexture);
        cmd.DispatchCompute(data.PostProcessCS, data.KernelComposite, Mathf.CeilToInt(width / 8f), Mathf.CeilToInt(height / 8f), 1);
        cmd.EndSample("Kern.PostProcess.Composite");

        if (!data.SwapColor)
        {
            cmd.BeginSample("Kern.PostProcess.BlitBack");
            Blitter.BlitCameraTexture(cmd, data.IntermediateTexture, data.ColorTexture);
            cmd.EndSample("Kern.PostProcess.BlitBack");
        }

        if (data.TemporalActive)
        {
            cmd.BeginSample("Kern.PostProcess.HistoryCopy");
            cmd.CopyTexture(data.IntermediateTexture, data.HistoryTexture);
            cmd.EndSample("Kern.PostProcess.HistoryCopy");
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

    private static void ExecuteBloom(PostProcessPassData data, CommandBuffer cmd, int width, int height)
    {
        int levels = data.BloomLevels;
        cmd.SetComputeFloatParam(data.PostProcessCS, BloomThresholdID, data.BloomThreshold);
        cmd.SetComputeFloatParam(data.PostProcessCS, BloomSoftKneeID, data.BloomSoftKnee);
        cmd.SetComputeFloatParam(data.PostProcessCS, BloomRadiusID, data.BloomRadius);
        cmd.SetComputeFloatParam(data.PostProcessCS, BloomScatterID, data.BloomScatter);
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
            term *= Mathf.Max(data.BloomScatter, 0f);
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
        cmd.BeginSample("Kern.PostProcess.Bloom.Prefilter");
        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelPrefilter, InputTexID, data.ColorTexture);
        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelPrefilter, DestTexID, data.BloomPrefilterTexture);
        cmd.DispatchCompute(
            data.PostProcessCS,
            data.KernelPrefilter,
            Mathf.CeilToInt(prefilterWidth / 8f),
            Mathf.CeilToInt(prefilterHeight / 8f),
            1);
        cmd.EndSample("Kern.PostProcess.Bloom.Prefilter");

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
            sourceWidth = downWidth;
            sourceHeight = downHeight;
        }

        cmd.EndSample("Kern.PostProcess.Bloom.Downsample");

        TextureHandle currentUp = data.BloomDownTextures[levels - 1];
        int currentUpWidth = downWidth;
        int currentUpHeight = downHeight;
        cmd.BeginSample("Kern.PostProcess.Bloom.Upsample");
        for (int i = levels - 1; i >= 0; i--)
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

        cmd.EndSample("Kern.PostProcess.Bloom.Upsample");

        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, BloomTexID, currentUp);
    }

    // Читается обоими ядрами: CompositeFinal тоже спрашивает шторку сравнения.
    private static void BindSharedParameters(PostProcessPassData data, CommandBuffer cmd)
    {
        cmd.SetComputeFloatParam(data.PostProcessCS, CompareSplitID, data.CompareSplit);
        cmd.SetComputeIntParam(data.PostProcessCS, CompareModeID, data.CompareMode);
        cmd.SetComputeIntParam(data.PostProcessCS, CompareBeforeID, data.CompareBefore ? 1 : 0);
        cmd.SetComputeIntParam(data.PostProcessCS, CurveInterpolationID, data.CurveInterpolation);
    }

    // Входы запекаемой таблицы. Ядру дисплея ничего из этого не нужно: к нему
    // творческий грейд приходит уже готовой таблицей.
    private static void BindCreativeParameters(PostProcessPassData data, CommandBuffer cmd)
    {
        cmd.SetComputeFloatParam(data.PostProcessCS, ExposureID, data.CgActive ? data.Exposure : 0f);
        cmd.SetComputeVectorParam(data.PostProcessCS, ColorFilterID, data.CgActive ? data.ColorFilter : Color.white);
        cmd.SetComputeFloatParam(data.PostProcessCS, ContrastID, data.CgActive ? data.Contrast : 0f);
        cmd.SetComputeFloatParam(data.PostProcessCS, SaturationID, data.CgActive ? data.Saturation : 1f);
        cmd.SetComputeFloatParam(data.PostProcessCS, CdlSaturationID, data.CdlSaturation);
        cmd.SetComputeVectorParam(data.PostProcessCS, WhiteBalanceID, data.WhiteBalance);
        cmd.SetComputeVectorParam(data.PostProcessCS, CdlSlopeID, data.CdlSlope);
        cmd.SetComputeVectorParam(data.PostProcessCS, CdlOffsetID, data.CdlOffset);
        cmd.SetComputeVectorParam(data.PostProcessCS, CdlPowerID, data.CdlPower);
        cmd.SetComputeVectorParam(data.PostProcessCS, CdlMasterID, data.CdlMaster);
        cmd.SetComputeVectorParam(data.PostProcessCS, PrimaryLiftID, data.PrimaryLift);
        cmd.SetComputeVectorParam(data.PostProcessCS, PrimaryGammaID, data.PrimaryGamma);
        cmd.SetComputeVectorParam(data.PostProcessCS, PrimaryGainID, data.PrimaryGain);
        cmd.SetComputeVectorParam(data.PostProcessCS, PrimaryOffsetID, data.PrimaryOffset);
        cmd.SetComputeVectorParam(data.PostProcessCS, PrimaryMasterID, data.PrimaryMaster);
        cmd.SetComputeFloatParam(data.PostProcessCS, VibranceID, data.Vibrance);
        cmd.SetComputeFloatParam(data.PostProcessCS, HueID, data.Hue);
        cmd.SetComputeVectorParam(data.PostProcessCS, ContrastControlsID, data.ContrastControls);
        cmd.SetComputeVectorParam(data.PostProcessCS, ContrastControls2ID, data.ContrastControls2);
        cmd.SetComputeVectorArrayParam(
            data.PostProcessCS,
            HueVsHueCurveID,
            data.HueVsHueCurvePoints);
        cmd.SetComputeVectorArrayParam(
            data.PostProcessCS,
            HueVsSaturationCurveID,
            data.HueVsSaturationCurvePoints);
        cmd.SetComputeVectorArrayParam(
            data.PostProcessCS,
            HueVsLuminanceCurveID,
            data.HueVsLuminanceCurvePoints);
        cmd.SetComputeVectorArrayParam(
            data.PostProcessCS,
            LuminanceVsSaturationCurveID,
            data.LuminanceVsSaturationCurvePoints);
        cmd.SetComputeVectorArrayParam(
            data.PostProcessCS,
            SaturationVsSaturationCurveID,
            data.SaturationVsSaturationCurvePoints);
        cmd.SetComputeIntParam(
            data.PostProcessCS,
            HueVsHueCurvePointCountID,
            data.HueVsHueCurvePointCount);
        cmd.SetComputeIntParam(
            data.PostProcessCS,
            HueVsSaturationCurvePointCountID,
            data.HueVsSaturationCurvePointCount);
        cmd.SetComputeIntParam(
            data.PostProcessCS,
            HueVsLuminanceCurvePointCountID,
            data.HueVsLuminanceCurvePointCount);
        cmd.SetComputeIntParam(
            data.PostProcessCS,
            LuminanceVsSaturationCurvePointCountID,
            data.LuminanceVsSaturationCurvePointCount);
        cmd.SetComputeIntParam(
            data.PostProcessCS,
            SaturationVsSaturationCurvePointCountID,
            data.SaturationVsSaturationCurvePointCount);
        cmd.SetComputeVectorParam(data.PostProcessCS, Qualifier0ID, data.Qualifier0);
        cmd.SetComputeVectorParam(data.PostProcessCS, Qualifier1ID, data.Qualifier1);
        cmd.SetComputeVectorParam(data.PostProcessCS, Qualifier2ID, data.Qualifier2);
        cmd.SetComputeVectorParam(data.PostProcessCS, Qualifier3ID, data.Qualifier3);
        cmd.SetComputeVectorParam(data.PostProcessCS, Qualifier4ID, data.Qualifier4);
        cmd.SetComputeVectorParam(data.PostProcessCS, Qualifier5ID, data.Qualifier5);
        cmd.SetComputeVectorParam(data.PostProcessCS, Qualifier6ID, data.Qualifier6);
        cmd.SetComputeVectorArrayParam(
            data.PostProcessCS,
            QualifierHueSamplesID,
            data.QualifierHueSamples);
        cmd.SetComputeIntParam(
            data.PostProcessCS,
            QualifierHueSampleCountID,
            data.QualifierHueSampleCount);
    }

    // Точечные операции вывода: кривая дисплея, куб-LUT, виньетка, зерно,
    // калибровка и временной смаз.
    private static void BindDisplayParameters(PostProcessPassData data, CommandBuffer cmd)
    {
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
        cmd.SetComputeVectorParam(data.PostProcessCS, DisplayGrade0ID, data.DisplayGrade0);
        cmd.SetComputeVectorParam(data.PostProcessCS, DisplayGrade1ID, data.DisplayGrade1);
        cmd.SetComputeFloatParam(data.PostProcessCS, GamutCompressionID, data.GamutCompression);
        cmd.SetComputeVectorArrayParam(data.PostProcessCS, MasterCurveID, data.MasterCurvePoints);
        cmd.SetComputeVectorArrayParam(data.PostProcessCS, RedCurveID, data.RedCurvePoints);
        cmd.SetComputeVectorArrayParam(data.PostProcessCS, GreenCurveID, data.GreenCurvePoints);
        cmd.SetComputeVectorArrayParam(data.PostProcessCS, BlueCurveID, data.BlueCurvePoints);
        cmd.SetComputeIntParam(data.PostProcessCS, MasterCurvePointCountID, data.MasterCurvePointCount);
        cmd.SetComputeIntParam(data.PostProcessCS, RedCurvePointCountID, data.RedCurvePointCount);
        cmd.SetComputeIntParam(data.PostProcessCS, GreenCurvePointCountID, data.GreenCurvePointCount);
        cmd.SetComputeIntParam(data.PostProcessCS, BlueCurvePointCountID, data.BlueCurvePointCount);
        cmd.SetComputeVectorParam(
            data.PostProcessCS,
            LutParamsID,
            new Vector4(
                data.LutIntensity,
                data.LutType,
                data.LutColorSpace,
                data.Lut1D != null ? data.Lut1D.width : data.Lut3D != null ? data.Lut3D.width : 0f));
        cmd.SetComputeVectorParam(data.PostProcessCS, LutDomainMinID, data.LutDomainMin);
        cmd.SetComputeVectorParam(data.PostProcessCS, LutDomainMaxID, data.LutDomainMax);
        // Metal validates every resource declared by a compute kernel, even when
        // the Lut branch is disabled by intensity/type. Bind explicit identity
        // LUTs so neutral grading remains mathematically unchanged.
        cmd.SetComputeTextureParam(
            data.PostProcessCS,
            data.KernelComposite,
            Lut1DID,
            data.Lut1D ?? GetIdentityLut1D());
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
            cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauAnimationSpeedID, data.EigengrauAnimationSpeed);
        }

        cmd.SetComputeFloatParam(data.PostProcessCS, TimeID, data.TimeSeconds);
        cmd.SetComputeFloatParam(data.PostProcessCS, MotionBlurHistoryID, data.MotionBlurHistory);
        cmd.SetComputeMatrixParam(
            data.PostProcessCS,
            HistoryReprojectionID,
            data.HistoryReprojection);
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

    private static Texture2D GetIdentityLut1D()
    {
        if (_identityLut1D != null)
        {
            return _identityLut1D;
        }

        _identityLut1D = RuntimeTextureFactory.CreateRGBAFloatNoMip(
            2,
            1,
            "PostProcess_IdentityLut1D",
            RuntimeTextureColorSpace.Linear,
            FilterMode.Bilinear,
            TextureWrapMode.Clamp);
        _identityLut1D.SetPixels([Color.black, Color.white]);
        _identityLut1D.Apply(false, true);
        return _identityLut1D;
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
            Color.white,
            new Color(0f, 0f, 1f, 1f),
            new Color(1f, 0f, 1f, 1f),
            new Color(0f, 1f, 1f, 1f),
            Color.white,
        ]);
        _identityLut3D.Apply(false, true);
        return _identityLut3D;
    }
}
