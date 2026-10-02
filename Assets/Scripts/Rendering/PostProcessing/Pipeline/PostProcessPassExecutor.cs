#nullable enable

using UnityEngine;
using System.Diagnostics;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using static Kern.Rendering.PostProcessing.PostProcessShaderConstants;

namespace Kern.Rendering.PostProcessing;

internal static class PostProcessPassExecutor
{
    private static readonly ProfilerMarker s_displayMarker = new(ProfilerCategory.Render, "Kern.PostProcess.DisplayFinal", MarkerFlags.SampleGPU);
    private static Texture3D? s_identityLUT3D;

    // LocalKeyword ищет слово в шейдере по имени; кэшируем вместе со ссылкой
    // на сам шейдер, чтобы не искать его дважды за кадр.
    private static ComputeShader? s_keywordShader;
    private static LocalKeyword s_diagnosticsKeyword;

    public static void Render(PostProcessPassData data, UnsafeGraphContext context)
    {
        long startedAt = Stopwatch.GetTimestamp();
        var workload = new PostProcessWorkloadAccumulator();
        HDROutputUtils.ConfigureHDROutput(data.PostProcessCS, data.HDRGamut,
            data.HDROutput ? HDROutputUtils.Operation.ColorConversion : HDROutputUtils.Operation.None);
        var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
        int width = data.Width;
        int height = data.Height;

        SetDiagnosticsKeyword(data, cmd);
        // DisplayFinal uses the dimensions of its input world-grid texture.
        cmd.SetComputeVectorParam(data.PostProcessCS, ScreenSizeId, new Vector4(width, height, 1f / width, 1f / height));

        BindDisplayParameters(data, cmd, context);
        if (data.WorldGridRect.z > 0 && PostProcessRuntimeState.DiagnosticWorldImage != null &&
            PostProcessRuntimeState.DiagnosticOffscreenCamera != null &&
            data.CameraId == PostProcessRuntimeState.DiagnosticOffscreenCamera.GetEntityId())
        {
            RTHandle source = data.ColorTexture;
            PostProcessRuntimeState.DiagnosticWorldImage(cmd, source.rt, data.WorldGridRect);
        }
        cmd.BeginSample(s_displayMarker);
        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, InputTexId, data.ColorTexture);
        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, OutputTexId, data.IntermediateTexture);
        cmd.DispatchCompute(data.PostProcessCS, data.KernelComposite, (width + 7) / 8, (height + 7) / 8, 1);
        workload.RecordDispatch(width, height, 8, 8);
        cmd.EndSample(s_displayMarker);

        if (!data.SwapColor)
        {
            cmd.BeginSample("Kern.PostProcess.BlitBack");
            Blitter.BlitCameraTexture(cmd, data.IntermediateTexture, data.ColorTexture);
            cmd.EndSample("Kern.PostProcess.BlitBack");
        }

        data.Workload.Publish(workload.Complete(Time.frameCount, width, height,
            data.SwapColor ? 0 : 1, data.CreatedTextureCount, data.CreatedTexturePayloadBytes, startedAt));
    }

    private static void SetDiagnosticsKeyword(PostProcessPassData data, CommandBuffer cmd)
    {
        if (!ReferenceEquals(s_keywordShader, data.PostProcessCS))
        {
            s_keywordShader = data.PostProcessCS;
            s_diagnosticsKeyword = new LocalKeyword(data.PostProcessCS, DiagnosticsKeyword);
        }

        cmd.SetKeyword(data.PostProcessCS, s_diagnosticsKeyword, data.DiagnosticsActive);
    }

    // Точечные операции вывода: LUT, виньетка, зерно, калибровка, шторка
    // сравнения. Сценическому проходу из этого не нужно ничего.
    private static void BindDisplayParameters(PostProcessPassData data, CommandBuffer cmd,
        UnsafeGraphContext context)
    {
        Vector4 sourceUv = data.DisplaySourceUv;
        if (data.ScalesWorldGrid)
        {
            // Output pixel coordinates use the destination's origin. Convert
            // them to Y-up viewport coordinates, then to the source's origin.
            if (context.GetTextureUVOrigin(data.IntermediateTexture) == TextureUVOrigin.TopLeft)
            {
                sourceUv.w += sourceUv.y;
                sourceUv.y = -sourceUv.y;
            }

            if (context.GetTextureUVOrigin(data.ColorTexture) == TextureUVOrigin.TopLeft)
            {
                sourceUv.w = 1f - sourceUv.w;
                sourceUv.y = -sourceUv.y;
            }
        }

        cmd.SetComputeVectorParam(data.PostProcessCS, "_DisplaySourceUv", sourceUv);
        cmd.SetComputeVectorParam(data.PostProcessCS, "_DisplayWorldToViewportUv", data.DisplayWorldToViewportUv);
        cmd.SetComputeFloatParam(data.PostProcessCS, "_DisplayViewportAspect", data.DisplayViewportAspect);
        cmd.SetComputeIntParam(data.PostProcessCS, "_DisplayLinearFilter", data.DisplayLinearFilter ? 1 : 0);
        cmd.SetComputeFloatParam(data.PostProcessCS, CompareSplitId, data.CompareSplit);
        cmd.SetComputeIntParam(data.PostProcessCS, CompareModeId, data.CompareMode);
        cmd.SetComputeIntParam(data.PostProcessCS, CompareBeforeId, data.CompareBefore ? 1 : 0);
        cmd.SetComputeFloatParam(data.PostProcessCS, VignetteIntensityId, data.VignetteActive ? data.VignetteIntensity : 0f);
        cmd.SetComputeIntParam(data.PostProcessCS, "_DisplayApplyVignette", data.VignetteInFinalBlit ? 0 : 1);
        if (data.VignetteActive)
        {
            cmd.SetComputeVectorParam(data.PostProcessCS, VignetteColorId, data.VignetteColor);
            cmd.SetComputeFloatParam(data.PostProcessCS, VignetteSmoothnessId, data.VignetteSmoothness);
            cmd.SetComputeVectorParam(data.PostProcessCS, VignetteCenterId, data.VignetteCenter);
        }

        // Keep the shader finite even if a stale/partially initialized HDR
        // output profile reaches the pass before display reconciliation.
        cmd.SetComputeFloatParam(
            data.PostProcessCS,
            DisplayPaperWhiteNitsId,
            Mathf.Max(data.DisplayPaperWhiteNits, 1f));
        cmd.SetComputeFloatParam(
            data.PostProcessCS,
            DisplayPeakRelativeId,
            data.DisplayPeakRelative);
        cmd.SetComputeIntParam(data.PostProcessCS, PostDebugViewId, data.PostDebugView);
        cmd.SetComputeVectorParam(
            data.PostProcessCS,
            LUTParamsId,
            new Vector4(
                data.LUTIntensity,
                data.LUTColorSpace,
                data.LUT3D != null ? data.LUT3D.width : 0f,
                0f));
        cmd.SetComputeVectorParam(data.PostProcessCS, LUTDomainMinId, data.LUTDomainMin);
        cmd.SetComputeVectorParam(data.PostProcessCS, LUTDomainMaxId, data.LUTDomainMax);
        // Metal validates every resource declared by a compute kernel, even when
        // the LUT branch is disabled by intensity. Bind an explicit identity
        // LUT so the neutral frame remains mathematically unchanged.
        cmd.SetComputeTextureParam(
            data.PostProcessCS,
            data.KernelComposite,
            LUT3DId,
            data.LUT3D ?? GetIdentityLUT3D());
        cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauIntensityId, data.EigengrauActive ? data.EigengrauIntensity : 0f);
        if (data.EigengrauActive)
        {
            cmd.SetComputeVectorParam(data.PostProcessCS, EigengrauColorId, data.EigengrauColor);
            cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauDarknessThresholdId, data.EigengrauDarknessThreshold);
            cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauNoiseScaleId, data.EigengrauNoiseScale);
            cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauNoiseAmplitudeId, data.EigengrauNoiseAmplitude);
        }

        cmd.SetComputeFloatParam(data.PostProcessCS, FrameIndexId, data.FrameIndex);
        cmd.SetComputeFloatParam(data.PostProcessCS, CalibrationPatternId, data.CalibrationPattern);
        cmd.SetComputeFloatParam(data.PostProcessCS, CalibrationValueId, data.CalibrationValue);
    }

    private static Texture3D GetIdentityLUT3D()
    {
        if (s_identityLUT3D != null)
        {
            return s_identityLUT3D;
        }

        s_identityLUT3D = RuntimeTextureFactory.CreateRGBAFloat3DNoMip(
            2,
            "PostProcess_IdentityLUT3D",
            FilterMode.Bilinear,
            TextureWrapMode.Clamp);
        s_identityLUT3D.SetPixels(
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
        s_identityLUT3D.Apply(false, true);
        return s_identityLUT3D;
    }
}
