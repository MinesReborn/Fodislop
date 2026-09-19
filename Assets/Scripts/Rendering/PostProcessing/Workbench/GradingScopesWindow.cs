#nullable enable

using System.Collections.Generic;
using Kern.Rendering.PostProcessing.Scopes;
using Kern.Tools.Imgui;
using UnityEngine;

namespace Kern.Rendering.PostProcessing.Workbench;

internal sealed class GradingScopesWindow : ToolWindow
{
    private const string ScopesOnLabel = "●  Считать приборы";
    private const string ScopesOffLabel = "○  Считать приборы";

    // Поля окна (13 + 13) и вертикальная полоса прокрутки.
    private const float ChromeWidth = 26f + 18f;
    private const float TwoColumnMinWidth = 700f;
    private const float ColumnGap = 8f;

    private static readonly GUIContent _MeasureContent = new();
    private static readonly GUILayoutOption[] ExpandWidth = [GUILayout.ExpandWidth(true)];

    private readonly Dictionary<string, float> _buttonWidths = [];

    private bool _scopesEnabled;
    private bool? _scopesEnabledRequested;
    private PostProcessDebugView? _debugViewRequested;
    private Vector2 _scroll;

    // Ширина берётся на Layout и держится до следующего Layout: иначе ресайз
    // между Layout и Repaint перенёс бы кнопки в другие ряды, и число
    // контролов двух событий разошлось бы.
    private float _contentWidth = 380f;

    private float _zoomLabelValue = float.NaN;
    private string _zoomLabel = string.Empty;
    private int _splitLabelPercent = -1;
    private string _splitLabel = string.Empty;
    private double _clippedBlack = double.NaN;
    private double _clippedHighlight = double.NaN;
    private string _clippedLabel = string.Empty;

    public GradingScopesWindow()
        : base("Приборы изображения", new Rect(738f, 16f, 446f, 720f))
    {
    }

    public bool ScopesRequested => Visible && _scopesEnabled;

    public override bool WantsSampling => false;

    public override Vector2 MinimumSize => new(380f, 360f);

    protected override void OnPlaySessionReset()
    {
        _scopesEnabled = false;
        _scopesEnabledRequested = null;
        _debugViewRequested = null;
        PostProcessRuntimeState.DebugView = PostProcessDebugView.None;
        PostProcessRuntimeState.CompareSplit = 0f;
        PostProcessRuntimeState.CompareMode = CompareMode.Off;
        PostProcessRuntimeState.CompareBefore = false;
        ScopesRenderPass.SourceMode = ScopesSourceMode.After;
        ScopesRenderPass.WaveformMode = ScopeWaveformMode.Overlay;
        ScopesRenderPass.HistogramMode = 0;
        _scroll = default;

        // Стили пересобираются вместе со скином, и замеры кнопок вместе с ними.
        _buttonWidths.Clear();
    }

    protected override void OnVisibilityChanged(bool visible)
    {
        if (!visible)
        {
            PostProcessRuntimeState.DebugView = PostProcessDebugView.None;
            PostProcessRuntimeState.CompareSplit = 0f;
            PostProcessRuntimeState.CompareMode = CompareMode.Off;
            PostProcessRuntimeState.CompareBefore = false;
            ScopesRenderPass.SourceMode = ScopesSourceMode.After;
            ScopesRenderPass.WaveformMode = ScopeWaveformMode.Overlay;
            ScopesRenderPass.HistogramMode = 0;
            _debugViewRequested = null;
        }
    }

    protected override void DrawContent()
    {
        if (Event.current.type == EventType.Layout)
        {
            ApplyPendingChanges();
            _contentWidth = Mathf.Max(120f, Rect.width - ChromeWidth);
        }

        using var scroll = ToolLayout.ScrollView(ref _scroll);

        DrawFrameSection();
        ToolTheme.Separator();
        DrawCompareSection();
        ToolTheme.Separator();
        DrawScopesSection();
    }

    private void DrawFrameSection()
    {
        int view = SegmentedRow("ВИД КАДРА", GradingScopesOptions.DebugViewOptions, (int)PostProcessRuntimeState.DebugView);
        if (view != (int)PostProcessRuntimeState.DebugView)
        {
            _debugViewRequested = (PostProcessDebugView)view;
        }

        string explanation = PostProcessRuntimeState.DebugView switch
        {
            PostProcessDebugView.FalseColor =>
                "зелёное — ключевой тон, жёлтое и оранжевое — света, " +
                "красное — пересвет, синее — провал",
            PostProcessDebugView.Clipping =>
                "красное — упёрлось в потолок, синее — село в пол",
            PostProcessDebugView.HighlightClipping =>
                "красное — clipped highlights, исходное изображение сохранено",
            PostProcessDebugView.ShadowClipping =>
                "синее — clipped shadows, исходное изображение сохранено",
            PostProcessDebugView.GamutWarning =>
                "синий — ниже display gamut, магентовый — выше, белый — оба предупреждения",
            PostProcessDebugView.LumaOnly =>
                "монохромная яркость финального graded output",
            PostProcessDebugView.SaturationOnly =>
                "чёрный — нейтральный, белый — максимальная насыщенность",
            PostProcessDebugView.QualifierMatte =>
                "белое — выбранная qualifier-маска, чёрное — исключённые пиксели",
            PostProcessDebugView.RgbParade => "трети R|G|B монохромом",
            _ => "кадр показывается без отладочной разметки",
        };
        GUILayout.Label(explanation, WrappedLabelStyle);
    }

    private void DrawCompareSection()
    {
        CompareMode mode = PostProcessRuntimeState.CompareMode;
        int picked = SegmentedRow("СРАВНЕНИЕ ДО / ПОСЛЕ", GradingScopesOptions.CompareOptions, (int)mode);
        if (picked != (int)mode)
        {
            ApplyCompareMode((CompareMode)picked);
            mode = PostProcessRuntimeState.CompareMode;
        }

        if (mode == CompareMode.AbToggle)
        {
            PostProcessRuntimeState.CompareBefore = GUILayout.Toggle(
                PostProcessRuntimeState.CompareBefore,
                "Показывать BEFORE",
                SegmentedButtonStyle);
        }

        if (mode is CompareMode.VerticalWipe or CompareMode.HorizontalWipe)
        {
            using (ToolLayout.Horizontal())
            {
                float split = GUILayout.HorizontalSlider(PostProcessRuntimeState.CompareSplit, 0f, 1f);
                PostProcessRuntimeState.CompareSplit = split;
                GUILayout.Label(SplitLabel(split), ToolTheme.FieldLabel, ToolLayout.Width(48f));
            }
        }

        GUILayout.Label(
            mode switch
            {
                CompareMode.VerticalWipe => "Слева — BEFORE, справа — AFTER.",
                CompareMode.HorizontalWipe => "Снизу — BEFORE, сверху — AFTER.",
                CompareMode.SideBySide => "Левая половина — BEFORE, правая — AFTER.",
                CompareMode.AbToggle => PostProcessRuntimeState.CompareBefore
                    ? "A/B: показывается BEFORE."
                    : "A/B: показывается AFTER.",
                _ => "Сравнение выключено. Удерживайте \\ для временного обхода грейда.",
            },
            WrappedLabelStyle);
    }

    private void DrawScopesSection()
    {
        GUILayout.Label("ПРИБОРЫ", SectionLabelStyle);
        bool scopesEnabled = GUILayout.Toggle(
            _scopesEnabled,
            _scopesEnabled ? ScopesOnLabel : ScopesOffLabel,
            SegmentedButtonStyle);
        if (scopesEnabled != _scopesEnabled)
        {
            _scopesEnabledRequested = scopesEnabled;
        }

        if (!_scopesEnabled)
        {
            // Пустые рамки на полэкрана ничего не сообщают: пока приборы
            // выключены, окно остаётся компактным.
            GUILayout.Label("Приборы выключены — проход не запускается.", MutedLabelStyle);
            return;
        }

        // Режимы пишутся только при смене: сеттеры прохода не обязаны быть
        // дешёвыми, а отрисовка вызывается несколько раз за кадр.
        int source = SegmentedRow("ИСТОЧНИК", GradingScopesOptions.SourceModeOptions, (int)ScopesRenderPass.SourceMode);
        if (source != (int)ScopesRenderPass.SourceMode)
        {
            ScopesRenderPass.SourceMode = (ScopesSourceMode)source;
        }

        int waveform = SegmentedRow("WAVEFORM", GradingScopesOptions.WaveformOptions, (int)ScopesRenderPass.WaveformMode);
        if (waveform != (int)ScopesRenderPass.WaveformMode)
        {
            ScopesRenderPass.WaveformMode = (ScopeWaveformMode)waveform;
        }

        int histogram = SegmentedRow("HISTOGRAM", GradingScopesOptions.HistogramOptions, ScopesRenderPass.HistogramMode);
        if (histogram != ScopesRenderPass.HistogramMode)
        {
            ScopesRenderPass.HistogramMode = histogram;
        }

        bool available = ScopesRenderPass.Available;
        if (!available)
        {
            string? failure = ScopesRenderPass.FailureMessage;
            GUILayout.Label(
                failure == null
                    ? "Приборы недоступны: renderer feature ещё не создал ScopesRenderPass " +
                      "или не нашёл Scopes.compute."
                    : "Приборы остановлены: " + failure +
                      ". Выключите и снова включите «Считать приборы» для повтора.",
                ToolTheme.WarningLabel);
            return;
        }

        GUILayout.Label(ClippedLabel(), MutedLabelStyle);
        GUILayout.Space(4f);

        string waveformTitle = ScopesRenderPass.WaveformMode switch
        {
            ScopeWaveformMode.Parade => "Waveform RGB parade",
            ScopeWaveformMode.Luma => "Waveform luma",
            _ => "Waveform RGB overlay",
        };

        if (_contentWidth >= TwoColumnMinWidth)
        {
            float column = (_contentWidth - ColumnGap) * 0.5f;
            using (ToolLayout.Horizontal())
            {
                DrawScope("Гистограмма", ScopesRenderPass.LiveHistogram, column, column * 0.6f);
                GUILayout.Space(ColumnGap);
                DrawScope(
                    "Вектороскоп",
                    ScopesRenderPass.LiveVectorscope,
                    column,
                    column * 0.6f,
                    ScaleMode.ScaleToFit);
            }

            GUILayout.Space(6f);
            DrawScope(waveformTitle, ScopesRenderPass.LiveWaveform, _contentWidth, Mathf.Min(320f, _contentWidth * 0.35f));
        }
        else
        {
            DrawScope("Гистограмма", ScopesRenderPass.LiveHistogram, _contentWidth, 128f);
            GUILayout.Space(6f);
            DrawScope(waveformTitle, ScopesRenderPass.LiveWaveform, _contentWidth, 200f);
            GUILayout.Space(6f);
            DrawScope(
                "Вектороскоп",
                ScopesRenderPass.LiveVectorscope,
                _contentWidth,
                Mathf.Min(_contentWidth, 260f),
                ScaleMode.ScaleToFit);
        }

        DrawVectorscopeControls();
    }

    private void DrawVectorscopeControls()
    {
        using (ToolLayout.Horizontal())
        {
            ScopesRenderPass.ShowSkinToneLine = GUILayout.Toggle(
                ScopesRenderPass.ShowSkinToneLine,
                "Skin-tone line",
                SegmentedButtonStyle);
            GUILayout.Label("цели: R / Mg / B / Cy / G / Y · 75% / 100%", MutedLabelStyle);
        }

        using (ToolLayout.Horizontal())
        {
            GUILayout.Label("масштаб вектороскопа", ToolTheme.FieldLabel, ToolLayout.Width(150f));
            ScopesRenderPass.VectorscopeScale = GUILayout.HorizontalSlider(
                ScopesRenderPass.VectorscopeScale,
                0.5f,
                2f);
            GUILayout.Label(
                ZoomLabel(ScopesRenderPass.VectorscopeScale),
                ToolTheme.FieldLabel,
                ToolLayout.Width(48f));
        }
    }

    // Сегментированный ряд с переносом: кнопки раскладываются по рядам по
    // ширине окна, а не сжимаются в одну строку до нечитаемости.
    private int SegmentedRow(string title, GradingScopesOptions.Option[] options, int current)
    {
        GUILayout.Label(title, SectionLabelStyle);
        int picked = current;
        int index = 0;
        while (index < options.Length)
        {
            using (ToolLayout.Horizontal())
            {
                float used = 0f;
                do
                {
                    GradingScopesOptions.Option option = options[index];
                    float width = MeasureButton(option.Label);
                    if (used > 0f && used + width > _contentWidth)
                    {
                        break;
                    }

                    used += width;
                    bool selected = option.Value == current;
                    if (GUILayout.Toggle(selected, option.Label, SegmentedButtonStyle, ExpandWidth) && !selected)
                    {
                        picked = option.Value;
                    }

                    index++;
                }
                while (index < options.Length);
            }
        }

        return picked;
    }

    private float MeasureButton(string label)
    {
        if (!_buttonWidths.TryGetValue(label, out float width))
        {
            GUIStyle style = SegmentedButtonStyle;
            _MeasureContent.text = label;
            width = style.CalcSize(_MeasureContent).x + style.margin.horizontal;
            _buttonWidths[label] = width;
        }

        return width;
    }

    private static void ApplyCompareMode(CompareMode mode)
    {
        PostProcessRuntimeState.CompareMode = mode;
        if (mode is CompareMode.VerticalWipe or CompareMode.HorizontalWipe)
        {
            PostProcessRuntimeState.CompareSplit = 0.5f;
        }
        else if (mode == CompareMode.Off)
        {
            PostProcessRuntimeState.CompareSplit = 0f;
            PostProcessRuntimeState.CompareBefore = false;
        }
    }

    // Подписи пересобираются только при смене показанного числа, а не на
    // каждое событие IMGUI.
    private string ZoomLabel(float scale)
    {
        if (!Mathf.Approximately(scale, _zoomLabelValue))
        {
            _zoomLabelValue = scale;
            _zoomLabel = $"{scale:0.00}×";
        }

        return _zoomLabel;
    }

    private string SplitLabel(float split)
    {
        int percent = Mathf.RoundToInt(split * 100f);
        if (percent != _splitLabelPercent)
        {
            _splitLabelPercent = percent;
            _splitLabel = $"{percent}%";
        }

        return _splitLabel;
    }

    private string ClippedLabel()
    {
        double black = ScopesRenderPass.ClippedBlackSamples;
        double highlight = ScopesRenderPass.ClippedHighlightSamples;
        if (black != _clippedBlack || highlight != _clippedHighlight)
        {
            _clippedBlack = black;
            _clippedHighlight = highlight;
            _clippedLabel =
                $"5 раз/с · отсечено: тени {black:N0}, света {highlight:N0} выборок";
        }

        return _clippedLabel;
    }

    private void ApplyPendingChanges()
    {
        if (_scopesEnabledRequested.HasValue)
        {
            _scopesEnabled = _scopesEnabledRequested.Value;
            _scopesEnabledRequested = null;
        }

        if (_debugViewRequested.HasValue)
        {
            PostProcessRuntimeState.DebugView = _debugViewRequested.Value;
            _debugViewRequested = null;
        }
    }

    private static void DrawScope(
        string title,
        RenderTexture? texture,
        float width,
        float height,
        ScaleMode scaleMode = ScaleMode.StretchToFill)
    {
        using (new GUILayout.VerticalScope(ToolTheme.Scope, ToolLayout.Width(width)))
        {
            GUILayout.Label(title, ToolTheme.SectionLabel);

            Rect rect = GUILayoutUtility.GetRect(width, height, ExpandWidth);
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            if (texture != null)
            {
                // Прямоугольные приборы растягиваются, вектороскоп вписывается:
                // круговая диаграмма цветности не должна становиться эллипсом.
                GUI.DrawTexture(rect, texture, scaleMode, false);
            }
            else
            {
                GUI.Label(rect, "Нет сигнала", ToolTheme.MutedLabel);
            }
        }
    }
}
