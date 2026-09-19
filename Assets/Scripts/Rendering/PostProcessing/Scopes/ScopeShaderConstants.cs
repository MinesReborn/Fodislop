#nullable enable

using UnityEngine;

namespace Kern.Rendering.PostProcessing.Scopes;

internal static class ScopeShaderConstants
{
    public const string PassName = "ComputeScopesPass";

    public static readonly int HistogramBufferID = Shader.PropertyToID("_HistogramBuffer");
    public static readonly int WaveformBufferID = Shader.PropertyToID("_WaveformBuffer");
    public static readonly int VectorscopeBufferID = Shader.PropertyToID("_VectorscopeBuffer");
    public static readonly int ScopeStatsBufferID = Shader.PropertyToID("_ScopeStatsBuffer");

    public static readonly int ScopeSourceID = Shader.PropertyToID("_ScopeSource");
    public static readonly int ScopeOutputID = Shader.PropertyToID("_ScopeOutput");
    public static readonly int ScopeSourceSizeID = Shader.PropertyToID("_ScopeSourceSize");
    public static readonly int ScopeParamsID = Shader.PropertyToID("_ScopeParams");
    public static readonly int ScopeSignalScaleID = Shader.PropertyToID("_ScopeSignalScale");
    public static readonly int ScopeHistogramModeID = Shader.PropertyToID("_ScopeHistogramMode");
    public static readonly int ScopeVectorscopeScaleID = Shader.PropertyToID("_ScopeVectorscopeScale");
    public static readonly int ScopeShowSkinToneLineID = Shader.PropertyToID("_ScopeShowSkinToneLine");
    public static readonly int ScopeWaveformModeID = Shader.PropertyToID("_ScopeWaveformMode");
}
