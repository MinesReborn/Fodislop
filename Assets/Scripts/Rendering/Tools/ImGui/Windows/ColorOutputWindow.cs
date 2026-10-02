#nullable enable

using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Rendering;
using Kern.Rendering.PostProcessing;
using Kern.World.Lighting;
using UnityEngine;

namespace Kern.Tools.ImGui.Windows;

// Калибровка дисплея, тестовые узоры и диагностика пересвета.
// Калибровкой владеет DisplayManager, узорами — PostProcessRuntimeState.
public sealed class ColorOutputWindow : ToolWindow
{
    private readonly IClientConfigManager _clientConfig;
    private readonly DisplayManager _displayManager;
    private readonly LightingEngine? _lighting;

    private Vector2 _scroll;

    // Подписи пересобираются только при смене значения: окно рисуется каждый
    // кадр, а склейка строк в IMGUI — мусор в куче на каждый такой кадр.
    private float _paperWhiteLabelValue = float.NaN;
    private string _paperWhiteLabel = string.Empty;
    private float _peakLabelValue = float.NaN;
    private string _peakLabel = string.Empty;
    private HDROutputController.Phase _HDRPhaseValue = HDROutputController.Phase.Uninitialized;
    private bool _HDRActiveValue;
    private string _HDRLabel = string.Empty;

    public ColorOutputWindow(
        IClientConfigManager clientConfig,
        DisplayManager displayManager,
        LightingEngine? lighting)
        : base("Цвет и вывод", new Rect(288f, 16f, 300f, 430f))
    {
        _clientConfig = clientConfig;
        _displayManager = displayManager;
        _lighting = lighting;
    }

    public override bool WantsSampling => false;

    public override Vector2 MinimumSize => new(290f, 320f);

    protected override void OnPlaySessionReset()
    {
        _scroll = default;
        PostProcessRuntimeState.SetCalibrationPattern(CalibrationPattern.Off, 0f);
    }

    protected override void DrawContent()
    {
        using (ToolLayout.ScrollView(ref _scroll))
        {
            DrawPostProcessLayers();
            DrawExposureZebra();
            DrawCalibration();
            DrawPatterns();
            DrawHDRState();
        }
    }

    private static void DrawPostProcessLayers()
    {
        ToolChrome.SectionHeader("СЛОИ ПОСТОБРАБОТКИ");
        DrawPostProcessLayer(PostProcessDebugView.None, "Обычный кадр");
        DrawPostProcessLayer(PostProcessDebugView.Vignette, "Виньетка");
        DrawPostProcessLayer(PostProcessDebugView.FilmGrain, "FilmGrain");
        DrawPostProcessLayer(PostProcessDebugView.Bloom, "Bloom");
    }

    private static void DrawPostProcessLayer(PostProcessDebugView view, string label)
    {
        bool selected = PostProcessRuntimeState.DebugView == view;
        if (GUILayout.Toggle(selected, label, ToolTheme.SegmentedButton) && !selected)
        {
            PostProcessRuntimeState.DebugView = view;
        }
    }

    private void DrawExposureZebra()
    {
        if (_lighting == null)
        {
            return;
        }

        bool zebra = _lighting.ActiveDebugView == LightingEngine.DebugView.Exposure;
        using (ToolLayout.Horizontal())
        {
            ToolChrome.StatusPip(zebra ? ToolTheme.Warning : ToolPalette.Fade(ToolPalette.MutedText, 0.45f));
            bool next = GUILayout.Toggle(zebra, "Показать пересвет", SegmentedButtonStyle);
            if (next != zebra)
            {
                _lighting.SetDebugView(next
                    ? LightingEngine.DebugView.Exposure
                    : LightingEngine.DebugView.FinalLighting);
            }
        }

        GUILayout.Label(
            "Синее — тень, зелёное — рабочий диапазон, жёлтое — запас выше " +
            "белого (его показывает HDR), красное — сгорит даже после сжатия.",
            MutedLabelStyle);
    }

    private void DrawCalibration()
    {
        ToolChrome.SectionHeader("КАЛИБРОВКА ДИСПЛЕЯ");
        GUILayout.Label(
            "Белая точка — яркость листа бумаги, к ней приравнена единица сцены. " +
            "Пик — самое яркое, что дисплей умеет показать. Шаг 50 нит: тоньше " +
            "глазом всё равно не различить.",
            MutedLabelStyle);

        DisplaySettings display = _clientConfig.Config.Display;

        float paperWhite = display.PaperWhiteNits;
        if (_paperWhiteLabelValue != paperWhite)
        {
            _paperWhiteLabelValue = paperWhite;
            _paperWhiteLabel = $"Белая точка   {paperWhite:0} нит";
        }

        GUILayout.Label(_paperWhiteLabel, MetricLabelStyle);
        float nextPaperWhite = GUILayout.HorizontalSlider(
            paperWhite, DisplaySettings.PaperWhiteMin, DisplaySettings.PaperWhiteMax);
        if (!Mathf.Approximately(nextPaperWhite, paperWhite))
        {
            _displayManager.SetPaperWhiteNits(nextPaperWhite);
        }

        float peak = display.PeakBrightnessNits;
        if (_peakLabelValue != peak)
        {
            _peakLabelValue = peak;
            _peakLabel = $"Пик   {peak:0} нит";
        }

        GUILayout.Label(_peakLabel, MetricLabelStyle);
        float nextPeak = GUILayout.HorizontalSlider(
            peak, DisplaySettings.PeakBrightnessMin, DisplaySettings.PeakBrightnessMax);
        if (!Mathf.Approximately(nextPeak, peak))
        {
            _displayManager.SetPeakBrightnessNits(nextPeak);
        }
    }

    private void DrawPatterns()
    {
        ToolChrome.SectionHeader("УЗОРЫ КАЛИБРОВКИ");
        GUILayout.Label(
            "Узор рисует проход вывода, поверх него ничего не ложится. " +
            "Белое поле — проверка белой точки, лестница — проверка пика: " +
            "верхние ступени должны перестать различаться там, где кончается " +
            "дисплей.",
            MutedLabelStyle);

        CalibrationPattern active = PostProcessRuntimeState.CalibrationMode;
        DisplaySettings display = _clientConfig.Config.Display;

        DrawPatternRow(active, CalibrationPattern.Off, "Без узора", 0f);
        DrawPatternRow(
            active, CalibrationPattern.PaperWhite, "Белое поле", display.PaperWhiteNits);
        DrawPatternRow(
            active, CalibrationPattern.PeakLadder, "Лестница пика", display.PeakBrightnessNits);
    }

    private static void DrawPatternRow(
        CalibrationPattern active, CalibrationPattern pattern, string label, float valueNits)
    {
        using (ToolLayout.Horizontal())
        {
            bool on = active == pattern;
            ToolChrome.StatusPip(on
                ? ToolTheme.Warning
                : ToolPalette.Fade(ToolPalette.MutedText, 0.45f));
            if (GUILayout.Toggle(on, label, ToolTheme.SegmentedButton) && !on)
            {
                PostProcessRuntimeState.SetCalibrationPattern(pattern, valueNits);
            }
        }
    }

    private void DrawHDRState()
    {
        ToolChrome.SectionHeader("СОСТОЯНИЕ ВЫВОДА");

        HDROutputController.Phase phase = HDROutput.Status;
        bool active = HDROutput.Active;
        if (_HDRPhaseValue != phase || _HDRActiveValue != active)
        {
            _HDRPhaseValue = phase;
            _HDRActiveValue = active;
            _HDRLabel = active
                ? $"HDR · {phase}"
                : $"SDR · {phase}";
        }

        using (ToolLayout.Horizontal())
        {
            ToolChrome.StatusPip(phase == HDROutputController.Phase.Failed
                ? ToolTheme.Error
                : active ? ToolTheme.Success : ToolPalette.Fade(ToolPalette.MutedText, 0.6f));
            GUILayout.Label(_HDRLabel, MetricLabelStyle);
        }

        // Переключение режима вывода остаётся в настройках игрока: там есть
        // подтверждение с откатом по таймеру, без которого недостижимый режим
        // запирает человека в нечитаемом экране. Дублировать его тут без этой
        // защиты нельзя.
        GUILayout.Label(
            "Переключается в настройках игры — там переключение подтверждается " +
            "и откатывается само, если экран стал нечитаемым.",
            MutedLabelStyle);
    }
}
