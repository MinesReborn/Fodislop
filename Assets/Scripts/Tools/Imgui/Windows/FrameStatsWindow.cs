#nullable enable

using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World.Lighting;
using UnityEngine;

namespace Kern.Tools.Imgui.Windows;

public sealed class FrameStatsWindow : ToolWindow
{
    private const int SampleCount = 120;

    private readonly IFrameTelemetry _telemetry;
    private readonly LightingEngine? _lighting;
    private readonly ToolGraph _frameTime = new(SampleCount);
    private readonly ToolGraph _allocations = new(SampleCount);

    private float _fpsTimer;
    private int _fpsFrames;
    private float _fps;
    private float _frameMs;
    private ulong _lastSolveCount;
    private float _solvesPerSecond;
    private Vector2 _scroll;

    private const string InitialFpsText = "--";
    private const string InitialFrameMsText = "-- мс на кадр   ·   решений света --/с";
    private const string InitialFrameStatsText = "кадр: сред --  мин --  макс -- мс";
    private const string InitialGpuText = "GPU: --";
    private const string InitialAllocationsText = "мусор: -- КБ/кадр   -- МБ/с   сборок 0";

    private string _fpsText = InitialFpsText;
    private string _frameMsText = InitialFrameMsText;
    private string _frameStatsText = InitialFrameStatsText;
    private string _gpuText = InitialGpuText;
    private string _allocationsText = InitialAllocationsText;

    // FrameTimingManager даёт gpuFrameTime в плеер-билдах без нейтива.
    // Масив один на жизнь окна: GetLatestTimings пишет в него, аллокаций нет.
    // В Editor PlayMode фича обычно недоступна — показываем прочерк, а не ноль.
    private readonly FrameTiming[] _frameTimings = new FrameTiming[1];
    private bool? _gpuTimingsSupported;

    public FrameStatsWindow(IFrameTelemetry telemetry, LightingEngine? lighting)
        : base("Производительность", new Rect(292f, 16f, 380f, 350f))
    {
        _telemetry = telemetry;
        _lighting = lighting;
        _lastSolveCount = lighting?.SolveCount ?? 0;
        Visible = true;
    }

    public override bool WantsSampling => true;

    public override Vector2 MinimumSize => new(340f, 300f);

    public float SolvesPerSecond => _solvesPerSecond;

    public override void Tick()
    {
        float delta = Time.unscaledDeltaTime;
        if (float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0f)
        {
            delta = 0f;
        }

        _frameTime.Push(delta * 1000f);
        _allocations.Push(_telemetry.GcAllocPerFrameBytes / 1024f);

        _fpsFrames++;
        _fpsTimer += delta;
        if (_fpsTimer < 0.25f)
        {
            return;
        }

        _fps = _fpsTimer > 0f ? _fpsFrames / _fpsTimer : 0f;
        _frameMs = _fpsFrames > 0 ? _fpsTimer / _fpsFrames * 1000f : 0f;

        ulong solveCount = _lighting?.SolveCount ?? _lastSolveCount;
        _solvesPerSecond = solveCount >= _lastSolveCount
            ? (solveCount - _lastSolveCount) / _fpsTimer
            : 0f;
        _lastSolveCount = solveCount;

        _fpsText = _fps.ToString("F0");
        _frameMsText = $"{_frameMs:F1} мс на кадр   ·   решений света {_solvesPerSecond:F1}/с";
        _frameStatsText = $"кадр: сред {_frameTime.Average:F1}  мин {_frameTime.Minimum:F1}  макс {_frameTime.Maximum:F1} мс";
        _gpuText = SampleGpuFrameTime();
        _allocationsText = $"мусор: {_allocations.Last:F1} КБ/кадр   {_telemetry.GcAllocTotalPerSecondBytes / (1024f * 1024f):F2} МБ/с   сборок {_telemetry.GcCollectionCount}";

        _fpsFrames = 0;
        _fpsTimer = 0f;
    }

    protected override void OnPlaySessionReset()
    {
        _scroll = default;
        _frameTime.Clear();
        _allocations.Clear();
        _frameTime.DestroyTexture();
        _allocations.DestroyTexture();
        _fpsTimer = 0f;
        _fpsFrames = 0;
        _fps = 0f;
        _frameMs = 0f;
        _gpuText = InitialGpuText;
        _lastSolveCount = _lighting?.SolveCount ?? 0;
        _solvesPerSecond = 0f;
        _fpsText = InitialFpsText;
        _frameMsText = InitialFrameMsText;
        _frameStatsText = InitialFrameStatsText;
        _allocationsText = InitialAllocationsText;
    }

    protected override void OnDispose()
    {
        _frameTime.Dispose();
        _allocations.Dispose();
    }

    protected override void DrawContent()
    {
        using (ToolLayout.ScrollView(ref _scroll))
        {
            float graphWidth = Mathf.Max(120f, Rect.width - 54f);

            ToolChrome.SectionHeader("КАДР");
            DrawHero(_fpsText, "КАДР/С", FrameHealthColor());
            GUILayout.Label(_frameMsText, MutedLabelStyle);

            // Доля бюджета в 16.7 мс. Число «11 мс» само по себе ничего не
            // говорит; доля бюджета говорит сразу, есть ли ещё запас.
            ToolChrome.MeterLine(_frameMs / 16.7f, FrameHealthColor());
            GUILayout.Space(4f);
            GUILayout.Label(_frameStatsText, MutedLabelStyle);
            GUILayout.Label(_gpuText, MutedLabelStyle);

            // Шкала прибита к 33 мс — двум кадрам при шестидесяти. Без опоры график
            // самонормируется, и ровный участок выглядит так же, как провал.
            _frameTime.Draw(
                GUILayoutUtility.GetRect(graphWidth, 70f),
                ToolTheme.FrameGraphColor,
                33f);

            ToolChrome.SectionHeader("ПАМЯТЬ");
            GUILayout.Label(_allocationsText, MutedLabelStyle);

            // Опора — килобайт на кадр. Ноль мусора это ровная пустая полоса,
            // и любой всплеск над ней виден сразу, без чтения чисел.
            ToolChrome.MeterLine(_allocations.Last / 16f, ToolTheme.AllocationGraphColor);
            GUILayout.Space(2f);

            _allocations.Draw(
                GUILayoutUtility.GetRect(graphWidth, 70f),
                ToolTheme.AllocationGraphColor,
                16f);
        }
    }

    private Color FrameHealthColor()
    {
        if (_frameMs <= 0f)
        {
            return ToolTheme.FrameGraphColor;
        }

        if (_frameMs > 33.3f)
        {
            return ToolTheme.Error;
        }

        return _frameMs > 16.7f ? ToolTheme.Warning : ToolTheme.Success;
    }

    // Общие на все события: GUIContent и GUILayoutOption — классы, и
    // создавать их в отрисовке значит мусорить на каждое событие IMGUI.
    private string SampleGpuFrameTime()
    {
        _gpuTimingsSupported ??= FrameTimingManager.IsFeatureEnabled();
        if (_gpuTimingsSupported != true)
        {
            return "GPU: — (тайминги недоступны)";
        }

        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, _frameTimings) == 0)
        {
            return "GPU: — (нет данных)";
        }

        double gpuMs = _frameTimings[0].gpuFrameTime;
        if (double.IsNaN(gpuMs) || double.IsInfinity(gpuMs) || gpuMs < 0.0)
        {
            return "GPU: — (нет данных)";
        }

        return $"GPU: {gpuMs:F1} мс";
    }
    private static readonly GUIContent _HeroContent = new();
    private static readonly GUILayoutOption _NoExpandWidth = GUILayout.ExpandWidth(false);

    private static void DrawHero(string value, string unit, Color color)
    {
        using (ToolLayout.Horizontal())
        {
            ToolChrome.StatusPip(color);

            // Крупное число рисуется общим стилем с временной подменой цвета:
            // копия стиля означала бы новую GUIStyle на каждое событие IMGUI —
            // мусор в окне, которое этот мусор и считает.
            GUIStyle style = MetricLabelStyle;
            _HeroContent.text = value;
            Rect area = GUILayoutUtility.GetRect(_HeroContent, style, _NoExpandWidth);
            ToolChrome.DrawTinted(area, value, style, color);

            GUILayout.Label(unit, ToolTheme.UnitLabel);
            GUILayout.FlexibleSpace();
        }
    }
}
