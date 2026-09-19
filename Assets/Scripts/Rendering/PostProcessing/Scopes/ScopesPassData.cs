#nullable enable

using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;

namespace Kern.Rendering.PostProcessing.Scopes;

internal sealed class ScopesPassData
{
    public ComputeShader ScopesCS = null!;
    public int KernelClear;
    public int KernelGather;
    public int KernelHistogram;
    public int KernelWaveform;
    public int KernelVectorscope;

    public ScopeResources Resources = null!;
    public TextureHandle SourceTexture;
    public int SourceWidth;
    public int SourceHeight;
    public float SignalScale;
    public int HistogramMode;
    public float VectorscopeScale;
    public bool ShowSkinToneLine;
    public int WaveformMode;
    public bool HDROutput;
    public ColorGamut HDRGamut;
}
