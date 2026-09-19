#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using static Kern.Rendering.PostProcessing.Scopes.ScopeShaderConstants;

namespace Kern.Rendering.PostProcessing.Scopes;

internal static class ScopesPassExecutor
{
    private const int GroupSize = 8;
    private const float TargetSamples = 65_536f;
    private const float UpdateIntervalSeconds = 0.1f;

    private static float _nextUpdateTime;

    public static void Render(ScopesPassData data, UnsafeGraphContext context)
    {
        float now = Time.realtimeSinceStartup;
        if (now < _nextUpdateTime)
        {
            return;
        }

        _nextUpdateTime = now + UpdateIntervalSeconds;
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
            ScopeSourceSizeID,
            new Vector4(
                data.SourceWidth,
                data.SourceHeight,
                1f / Mathf.Max(1, data.SourceWidth),
                1f / Mathf.Max(1, data.SourceHeight)));
        cmd.SetComputeVectorParam(
            data.ScopesCS,
            ScopeParamsID,
            new Vector4(
                histogramNormalization,
                step,
                densityNormalization,
                densityNormalization));
        cmd.SetComputeFloatParam(data.ScopesCS, ScopeSignalScaleID, data.SignalScale);
        cmd.SetComputeIntParam(data.ScopesCS, ScopeHistogramModeID, data.HistogramMode);
        cmd.SetComputeFloatParam(data.ScopesCS, ScopeVectorscopeScaleID, data.VectorscopeScale);
        cmd.SetComputeIntParam(
            data.ScopesCS,
            ScopeShowSkinToneLineID,
            data.ShowSkinToneLine ? 1 : 0);
        cmd.SetComputeIntParam(data.ScopesCS, ScopeWaveformModeID, data.WaveformMode);

        BindBuffers(cmd, data.ScopesCS, data.KernelClear, histogram, waveform, vectorscope, stats);
        Dispatch(cmd, data.ScopesCS, data.KernelClear, ScopeResources.Size, ScopeResources.Size);

        BindBuffers(cmd, data.ScopesCS, data.KernelGather, histogram, waveform, vectorscope, stats);
        cmd.SetComputeTextureParam(data.ScopesCS, data.KernelGather, ScopeSourceID, data.SourceTexture);
        Dispatch(cmd, data.ScopesCS, data.KernelGather, sampledWidth, sampledHeight);
        AsyncGPUReadback.Request(stats, StatsCallback(resources));

        Resolve(cmd, data, data.KernelHistogram, resources.HistogramTexture, histogram, waveform, vectorscope);
        Resolve(cmd, data, data.KernelWaveform, resources.WaveformTexture, histogram, waveform, vectorscope);
        Resolve(cmd, data, data.KernelVectorscope, resources.VectorscopeTexture, histogram, waveform, vectorscope);
    }

    private static ScopeResources? _statsCallbackOwner;
    private static System.Action<AsyncGPUReadbackRequest>? _statsCallback;

    // Группа методов превращается в новый делегат при каждом вызове, то есть
    // на каждый кадр с открытыми приборами.
    private static System.Action<AsyncGPUReadbackRequest> StatsCallback(ScopeResources resources)
    {
        if (_statsCallback == null || !ReferenceEquals(_statsCallbackOwner, resources))
        {
            _statsCallbackOwner = resources;
            _statsCallback = resources.ApplyStats;
        }

        return _statsCallback;
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
        cmd.SetComputeTextureParam(data.ScopesCS, kernel, ScopeOutputID, texture);
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
        cmd.SetComputeBufferParam(shader, kernel, HistogramBufferID, histogram);
        cmd.SetComputeBufferParam(shader, kernel, WaveformBufferID, waveform);
        cmd.SetComputeBufferParam(shader, kernel, VectorscopeBufferID, vectorscope);
        if (stats != null)
        {
            cmd.SetComputeBufferParam(shader, kernel, ScopeStatsBufferID, stats);
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
