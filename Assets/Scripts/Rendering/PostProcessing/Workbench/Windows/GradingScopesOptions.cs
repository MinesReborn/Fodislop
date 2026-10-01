#nullable enable

using Kern.Rendering.PostProcessing.Scopes;

namespace Kern.Rendering.PostProcessing.Workbench;

// Option lists for the scopes workbench segmented rows. Data only: no Unity
// calls, no state. Lives apart so GradingScopesWindow stays under the file
// size limit.
internal static class GradingScopesOptions
{
    internal readonly record struct Option(string Label, int Value);

    internal static readonly Option[] DebugViewOptions =
    [
        new("обычный", (int)PostProcessDebugView.None),
        new("ложный цвет", (int)PostProcessDebugView.FalseColor),
        new("отсечка", (int)PostProcessDebugView.Clipping),
        new("gamut warning", (int)PostProcessDebugView.GamutWarning),
        new("luma", (int)PostProcessDebugView.LumaOnly),
        new("sat", (int)PostProcessDebugView.SaturationOnly),
        new("RGB", (int)PostProcessDebugView.RgbParade),
        new("виньетка", (int)PostProcessDebugView.Vignette),
        new("FilmGrain", (int)PostProcessDebugView.FilmGrain),
        new("bloom", (int)PostProcessDebugView.Bloom),
    ];

    internal static readonly Option[] CompareOptions =
    [
        new("выкл", (int)CompareMode.Off),
        new("верт. wipe", (int)CompareMode.VerticalWipe),
        new("гориз. wipe", (int)CompareMode.HorizontalWipe),
        new("сплит", (int)CompareMode.SideBySide),
        new("A/B", (int)CompareMode.AbToggle),
    ];

    internal static readonly Option[] SourceModeOptions =
    [
        new("Выходной сигнал", (int)ScopesSourceMode.After),
        new("Сцена до тонмаппинга", (int)ScopesSourceMode.Before),
    ];

    internal static readonly Option[] WaveformOptions =
    [
        new("RGB parade", (int)ScopeWaveformMode.Parade),
        new("Overlay", (int)ScopeWaveformMode.Overlay),
        new("Luma", (int)ScopeWaveformMode.Luma),
    ];

    internal static readonly Option[] HistogramOptions =
    [
        new("RGB", 0),
        new("Luma", 1),
    ];
}
