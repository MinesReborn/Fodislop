#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Kern.Rendering.PostProcessing.Scopes;

internal static class ScopesPassExecutor
{
    private const int GroupSize = 8;
    private const float TargetSamples = 65_536f;
    private const float UpdateIntervalSeconds = 0.1f;

    private static float s_nextUpdateTime;

    public static void Render(ScopesPassData data, UnsafeGraphContext context)
    {
        float now = Time.realtimeSinceStartup;
        if (now < s_nextUpdateTime)
        {
            return;
        }

        s_nextUpdateTime = now + UpdateIntervalSeconds;
        CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
        ScopeResources resources = data.Resources;
        HDROutputUtils.ConfigureHDROutput(
            data.ScopesCS,
            data.HDRGamut,
            data.HDROutput ? HDROutputUtils.Operation.ColorConversion : HDROutputUtils.Operation.None);

        ComputeBuffer histogram = Require(resources.HistogramBuffer, nameof(resources.HistogramBuffer));
        ComputeBuffer waveform = Require(resources.WaveformBuffer, nameof(resources.WaveformBuffer));
        ComputeBuffer vectorscope = Require(resources.VectorscopeBuffer, nameof(resources.VectorscopeBuffer));
        ComputeBuffer stats = Require(resources.StatsBuffer, nameof(resources.StatsBuffer));
        ComputeBuffer exposureHistogram = Require(
            resources.ExposureHistogramBuffer,
            nameof(resources.ExposureHistogramBuffer));

        // Прореживание: разбирать каждый пиксель кадра в 4K не нужно и вредно —
        // прибор от этого не точнее, а кадр дороже. Сетки 256x256 выборок
        // достаточно для 256 корзин и не превращает отладочный вид в GPU-нагрузку.
        long pixels = (long)data.SourceWidth * data.SourceHeight;
        int step = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(pixels / TargetSamples)));
        int sampledWidth = Mathf.Max(1, (data.SourceWidth + step - 1) / step);
        int sampledHeight = Mathf.Max(1, (data.SourceHeight + step - 1) / step);

        // Нормировка отклика: обратна числу выборок, иначе прибор менял бы
        // яркость при смене разрешения окна.
        float sampleCount = Mathf.Max(1, sampledWidth * sampledHeight);
        float histogramNormalization = 64f * ScopeResources.Size / sampleCount;
        float densityNormalization =
            64f * ScopeResources.Size * ScopeResources.Size / sampleCount;

        cmd.SetComputeVectorParam(
            data.ScopesCS,
            ScopeShaderProperties.ScopeSourceSizeId,
            new Vector4(
                data.SourceWidth,
                data.SourceHeight,
                1f / Mathf.Max(1, data.SourceWidth),
                1f / Mathf.Max(1, data.SourceHeight)));
        cmd.SetComputeVectorParam(
            data.ScopesCS,
            ScopeShaderProperties.ScopeParamsId,
            new Vector4(
                histogramNormalization,
                step,
                densityNormalization,
                densityNormalization));
        cmd.SetComputeFloatParam(data.ScopesCS, ScopeShaderProperties.ScopeSignalScaleId, data.SignalScale);
        cmd.SetComputeFloatParam(data.ScopesCS, ScopeShaderProperties.ScopeExposureScaleId, data.ExposureScale);
        cmd.SetComputeIntParam(data.ScopesCS, ScopeShaderProperties.ScopeHistogramModeId, data.HistogramMode);
        cmd.SetComputeFloatParam(data.ScopesCS, ScopeShaderProperties.ScopeVectorscopeScaleId, data.VectorscopeScale);
        cmd.SetComputeIntParam(
            data.ScopesCS,
            ScopeShaderProperties.ScopeShowSkinToneLineId,
            data.ShowSkinToneLine ? 1 : 0);
        cmd.SetComputeIntParam(data.ScopesCS, ScopeShaderProperties.ScopeWaveformModeId, data.WaveformMode);

        BindBuffers(cmd, data.ScopesCS, data.KernelClear, histogram, waveform, vectorscope, stats);
        cmd.SetComputeBufferParam(
            data.ScopesCS,
            data.KernelClear,
            ScopeShaderProperties.ExposureHistogramBufferId,
            exposureHistogram);
        Dispatch(cmd, data.ScopesCS, data.KernelClear, ScopeResources.Size, ScopeResources.Size);

        BindBuffers(cmd, data.ScopesCS, data.KernelGather, histogram, waveform, vectorscope, stats);
        cmd.SetComputeTextureParam(data.ScopesCS, data.KernelGather, ScopeShaderProperties.ScopeSourceId, data.SourceTexture);
        cmd.SetComputeBufferParam(
            data.ScopesCS,
            data.KernelGather,
            ScopeShaderProperties.ExposureHistogramBufferId,
            exposureHistogram);
        Dispatch(cmd, data.ScopesCS, data.KernelGather, sampledWidth, sampledHeight);
        AsyncGPUReadback.Request(stats, StatsCallback(resources));
        AsyncGPUReadback.Request(exposureHistogram, ExposureHistogramCallback(resources));

        Resolve(cmd, data, data.KernelHistogram, resources.HistogramTexture, histogram, waveform, vectorscope);
        Resolve(cmd, data, data.KernelWaveform, resources.WaveformTexture, histogram, waveform, vectorscope);
        Resolve(cmd, data, data.KernelVectorscope, resources.VectorscopeTexture, histogram, waveform, vectorscope);
    }

    private static ScopeResources? s_statsCallbackOwner;
    private static System.Action<AsyncGPUReadbackRequest>? s_statsCallback;
    private static ScopeResources? s_exposureHistogramCallbackOwner;
    private static System.Action<AsyncGPUReadbackRequest>? s_exposureHistogramCallback;

    // Группа методов превращается в новый делегат при каждом вызове, то есть
    // на каждый кадр с открытыми приборами.
    private static System.Action<AsyncGPUReadbackRequest> StatsCallback(ScopeResources resources)
    {
        if (s_statsCallback == null || !ReferenceEquals(s_statsCallbackOwner, resources))
        {
            s_statsCallbackOwner = resources;
            s_statsCallback = resources.ApplyStats;
        }

        return s_statsCallback;
    }

    private static System.Action<AsyncGPUReadbackRequest> ExposureHistogramCallback(ScopeResources resources)
    {
        if (s_exposureHistogramCallback == null || !ReferenceEquals(s_exposureHistogramCallbackOwner, resources))
        {
            s_exposureHistogramCallbackOwner = resources;
            s_exposureHistogramCallback = resources.ApplyExposureHistogram;
        }

        return s_exposureHistogramCallback;
    }

    private static void Resolve(
        CommandBuffer cmd,
        ScopesPassData data,
        int kernel,
        RenderTexture? target,
        ComputeBuffer histogram,
        ComputeBuffer waveform,
        ComputeBuffer vectorscope)
    {
        RenderTexture texture = Require(target, "scope target");
        BindBuffers(cmd, data.ScopesCS, kernel, histogram, waveform, vectorscope);
        cmd.SetComputeTextureParam(data.ScopesCS, kernel, ScopeShaderProperties.ScopeOutputId, texture);
        Dispatch(cmd, data.ScopesCS, kernel, texture.width, texture.height);
    }

    private static void BindBuffers(
        CommandBuffer cmd,
        ComputeShader shader,
        int kernel,
        ComputeBuffer histogram,
        ComputeBuffer waveform,
        ComputeBuffer vectorscope,
        ComputeBuffer? stats = null)
    {
        cmd.SetComputeBufferParam(shader, kernel, ScopeShaderProperties.HistogramBufferId, histogram);
        cmd.SetComputeBufferParam(shader, kernel, ScopeShaderProperties.WaveformBufferId, waveform);
        cmd.SetComputeBufferParam(shader, kernel, ScopeShaderProperties.VectorscopeBufferId, vectorscope);
        if (stats != null)
        {
            cmd.SetComputeBufferParam(shader, kernel, ScopeShaderProperties.ScopeStatsBufferId, stats);
        }
    }

    private static void Dispatch(CommandBuffer cmd, ComputeShader shader, int kernel, int width, int height)
    {
        cmd.DispatchCompute(
            shader,
            kernel,
            Mathf.CeilToInt(width / (float)GroupSize),
            Mathf.CeilToInt(height / (float)GroupSize),
            1);
    }

    private static T Require<T>(T? value, string name)
        where T : class =>
        value ?? throw new InvalidOperationException($"Scopes pass requires '{name}'.");
}
