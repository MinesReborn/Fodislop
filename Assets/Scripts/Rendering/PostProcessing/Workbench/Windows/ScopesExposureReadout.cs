#nullable enable

using Kern.Rendering;
using Kern.Rendering.PostProcessing.Scopes;
using UnityEngine;

namespace Kern.Rendering.PostProcessing.Workbench;

internal sealed class ScopesExposureReadout
{
    private float _median = float.NaN;
    private float _p95 = float.NaN;
    private ScopesSourceMode _sourceMode;
    private bool _HDR;
    private float _paperWhiteNits = float.NaN;
    private CalibrationPattern _calibrationMode;
    private float _calibrationValue = float.NaN;
    private string _label = string.Empty;

    public string GetLabel()
    {
        float median = ScopesRenderPass.MedianExposureStops;
        float p95 = ScopesRenderPass.P95ExposureStops;
        ScopesSourceMode sourceMode = ScopesRenderPass.SourceMode;
        bool HDR = HDROutput.Active;
        float paperWhiteNits = Mathf.Max(1f, PostProcessRuntimeState.DisplayPaperWhiteNits);
        CalibrationPattern calibrationMode = PostProcessRuntimeState.CalibrationMode;
        float calibrationValue = PostProcessRuntimeState.CalibrationValue;
        if (_label.Length == 0 ||
            !median.Equals(_median) ||
            !p95.Equals(_p95) ||
            sourceMode != _sourceMode ||
            HDR != _HDR ||
            !paperWhiteNits.Equals(_paperWhiteNits) ||
            calibrationMode != _calibrationMode ||
            !calibrationValue.Equals(_calibrationValue))
        {
            _median = median;
            _p95 = p95;
            _sourceMode = sourceMode;
            _HDR = HDR;
            _paperWhiteNits = paperWhiteNits;
            _calibrationMode = calibrationMode;
            _calibrationValue = calibrationValue;
            _label = BuildLabel(median, p95, sourceMode, HDR, paperWhiteNits, calibrationMode, calibrationValue);
        }

        return _label;
    }

    private static string BuildLabel(
        float median,
        float p95,
        ScopesSourceMode sourceMode,
        bool HDR,
        float paperWhiteNits,
        CalibrationPattern calibrationMode,
        float calibrationValue)
    {
        string sourceLabel = sourceMode == ScopesSourceMode.Before
            ? "до тонмаппинга · post exposure учтён"
            : "после тонмаппинга · линейный сигнал до PQ/scRGB";
        string nitUnit = sourceMode == ScopesSourceMode.Before
            ? "нит (эквивалент сцены)"
            : "нит (линейный выход)";
        string calibrationNote = calibrationMode == CalibrationPattern.PeakLadder
            ? $" Лестница намеренно включает ступени выше маркера {calibrationValue:0.#} nit."
            : string.Empty;
        if (float.IsNaN(median) || float.IsNaN(p95))
        {
            return "Экспозиция: ожидание выборки";
        }

        return HDR
            ? $"Яркость {sourceLabel}: медиана {FormatExposure(median, paperWhiteNits, nitUnit)} · " +
              $"P95 {FormatExposure(p95, paperWhiteNits, nitUnit)} " +
              $"(0 EV = paper white; сигнал рендера, не фотометр панели; диапазон ±12 EV).{calibrationNote}"
            : $"Яркость {sourceLabel}: медиана {median:+0.00;-0.00;0.00} EV · " +
              $"P95 {p95:+0.00;-0.00;0.00} EV (0 EV = reference white; диапазон ±12 EV)";
    }

    private static string FormatExposure(float stops, float paperWhiteNits, string unit)
    {
        float nitsEquivalent = paperWhiteNits * Mathf.Pow(2f, stops);
        return $"{stops:+0.00;-0.00;0.00} EV / {nitsEquivalent:0.#} {unit}";
    }
}
