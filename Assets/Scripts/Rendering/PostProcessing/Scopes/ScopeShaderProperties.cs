#nullable enable

using UnityEngine;

namespace Kern.Rendering.PostProcessing.Scopes;

internal static class ScopeShaderProperties
{
    internal static readonly int HistogramBufferId = Shader.PropertyToID("_HistogramBuffer");
    internal static readonly int WaveformBufferId = Shader.PropertyToID("_WaveformBuffer");
    internal static readonly int VectorscopeBufferId = Shader.PropertyToID("_VectorscopeBuffer");
    internal static readonly int ScopeStatsBufferId = Shader.PropertyToID("_ScopeStatsBuffer");
    internal static readonly int ExposureHistogramBufferId = Shader.PropertyToID("_ExposureHistogramBuffer");

    internal static readonly int ScopeSourceId = Shader.PropertyToID("_ScopeSource");
    internal static readonly int ScopeOutputId = Shader.PropertyToID("_ScopeOutput");
    // _ScopeSourceSize: xy = source dimensions in pixels; zw = reciprocal dimensions.
    internal static readonly int ScopeSourceSizeId = Shader.PropertyToID("_ScopeSourceSize");
    // _ScopeParams: x = histogram normalization; y = sampling step in pixels;
    // z = waveform normalization; w = vectorscope normalization.
    internal static readonly int ScopeParamsId = Shader.PropertyToID("_ScopeParams");
    internal static readonly int ScopeSignalScaleId = Shader.PropertyToID("_ScopeSignalScale");
    internal static readonly int ScopeExposureScaleId = Shader.PropertyToID("_ScopeExposureScale");
    internal static readonly int ScopeHistogramModeId = Shader.PropertyToID("_ScopeHistogramMode");
    internal static readonly int ScopeVectorscopeScaleId = Shader.PropertyToID("_ScopeVectorscopeScale");
    internal static readonly int ScopeShowSkinToneLineId = Shader.PropertyToID("_ScopeShowSkinToneLine");
    internal static readonly int ScopeWaveformModeId = Shader.PropertyToID("_ScopeWaveformMode");
}
