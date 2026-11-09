#nullable enable

using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World.Lighting;
using UnityEngine;

using Kern.Rendering.Diagnostics.Tools;

using Kern.Diagnostics.World;

namespace Kern.Rendering.Diagnostics.Windows;

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

    private const string InitialFPSText = "--";
    private const string InitialFrameMsText = "-- мс на кадр   ·   решений света --/с";
    private const string InitialFrameStatsText = "кадр: сред --  мин --  макс -- мс";
    private const string InitialGPUText = "GPU: --";
    private const string InitialAllocationsText = "мусор: -- КБ/кадр   -- МБ/с   сборок —";

    private string _fpsText = InitialFPSText;
    private string _frameMsText = InitialFrameMsText;
    private string _frameStatsText = InitialFrameStatsText;
    private string _gpuText = InitialGPUText;
    private string _allocationsText = InitialAllocationsText;
    private bool _allocationTrackingWasAvailable;
    private bool _hasAllocationSample;
    private bool _hasFrameSummary;

    // FrameTimingManager даёт gpuFrameTime в плеер-билдах без нейтива.
    // Масив один на жизнь окна: GetLatestTimings пишет в него, аллокаций нет.
    // Если текущий runtime не отдаёт замер, показываем прочерк, а не ноль.
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
        if (float.IsNaN(delta) || float.IsInfinity(delta) || delta <= 0f)
        {
            return;
        }

        _frameTime.Push(delta * 1000f);
        IFrameAllocationTelemetry? allocationTelemetry = _telemetry as IFrameAllocationTelemetry;
        bool allocationTrackingAvailable = allocationTelemetry?.IsGcAllocationTrackingAvailable == true;
        if (!allocationTrackingAvailable && _allocationTrackingWasAvailable)
        {
            _allocations.Clear();
        }

        _allocationTrackingWasAvailable = allocationTrackingAvailable;
        _hasAllocationSample = allocationTelemetry?.HasGcAllocationSample == true;
        if (_hasAllocationSample)
        {
            _allocations.Push(allocationTelemetry.GcAllocPerFrameBytes / 1024f);
        }

        _fpsFrames++;
        _fpsTimer += delta;
        if (_fpsTimer < 0.25f)
        {
            return;
        }

        _fps = _fpsTimer > 0f ? _fpsFrames / _fpsTimer : 0f;
        _frameMs = _fpsFrames > 0 ? _fpsTimer / _fpsFrames * 1000f : 0f;
        _hasFrameSummary = _fpsFrames > 0;

        ulong solveCount = _lighting?.SolveCount ?? _lastSolveCount;
        _solvesPerSecond = solveCount >= _lastSolveCount
            ? (solveCount - _lastSolveCount) / _fpsTimer
            : 0f;
        _lastSolveCount = solveCount;

        _fpsText = _fps.ToString("F0");
        _frameMsText = $"{_frameMs:F1} мс/кадр за {_fpsTimer:F2} с   ·   решений света {_solvesPerSecond:F1}/с";
        _frameStatsText = $"последние {_frameTime.Count} кадров: сред {_frameTime.Average:F1}  мин {_frameTime.Minimum:F1}  макс {_frameTime.Maximum:F1} мс";
        _gpuText = SampleGPUFrameTime();
        _allocationsText = allocationTelemetry?.HasGcAllocationSample == true
                ? $"мусор: {_allocations.Last:F1} КБ/кадр   {(allocationTelemetry.HasGcAllocationRateSample ? $"{allocationTelemetry.GcAllocTotalPerSecondBytes / (1024f * 1024f):F2}" : "—")} МБ/с   сборок {allocationTelemetry.GcCollectionCount}"
            : allocationTelemetry?.IsGcAllocationTrackingAvailable == true
                ? $"мусор: нет свежей выборки; история графика — {_allocations.Count} кадров   сборок {allocationTelemetry.GcCollectionCount}"
                : "мусор: замер недоступен";

        _fpsFrames = 0;
        _fpsTimer = 0f;
    }

    protected override void OnPlaySessionReset()
    {
        _scroll = default;
        _frameTime.Clear();
        _allocations.Clear();
        _allocationTrackingWasAvailable = false;
        _hasAllocationSample = false;
        _hasFrameSummary = false;
        _frameTime.DestroyTexture();
        _allocations.DestroyTexture();
        _fpsTimer = 0f;
        _fpsFrames = 0;
        _fps = 0f;
        _frameMs = 0f;
        _gpuText = InitialGPUText;
        _lastSolveCount = _lighting?.SolveCount ?? 0;
        _solvesPerSecond = 0f;
        _fpsText = InitialFPSText;
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
            if (_hasFrameSummary)
            {
                ToolChrome.MeterLine(_frameMs / 16.7f, FrameHealthColor());
            }
            GUILayout.Space(4f);
            GUILayout.Label(_frameStatsText, MutedLabelStyle);
            GUILayout.Label(_gpuText, MutedLabelStyle);

            // Шкала прибита к 33 мс — двум кадрам при шестидесяти. Без опоры график
            // самонормируется, и ровный участок выглядит так же, как провал.
            if (_frameTime.Count > 0)
            {
                _frameTime.Draw(
                    GUILayoutUtility.GetRect(graphWidth, 70f),
                    ToolTheme.FrameGraphColor,
                    33f);
            }

            ToolChrome.SectionHeader("ПАМЯТЬ");
            GUILayout.Label(_allocationsText, MutedLabelStyle);

            if (_hasAllocationSample)
            {
                ToolChrome.MeterLine(_allocations.Last / 16f, ToolTheme.AllocationGraphColor);
            }

            if (_allocations.Count > 0)
            {
                GUILayout.Space(2f);
                _allocations.Draw(
                    GUILayoutUtility.GetRect(graphWidth, 70f),
                    ToolTheme.AllocationGraphColor,
                    16f);
            }
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
    private string SampleGPUFrameTime()
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
        if (double.IsNaN(gpuMs) || double.IsInfinity(gpuMs) || gpuMs <= 0.0)
        {
            return "GPU: — (нет данных)";
        }

        return $"GPU: {gpuMs:F1} мс";
    }
    private static readonly GUIContent s_heroContent = new();
    private static readonly GUILayoutOption s_noExpandWidth = GUILayout.ExpandWidth(false);

    private static void DrawHero(string value, string unit, Color color)
    {
        using (ToolLayout.Horizontal())
        {
            ToolChrome.StatusPip(color);

            // Крупное число рисуется общим стилем с временной подменой цвета:
            // копия стиля означала бы новую GUIStyle на каждое событие IMGUI —
            // мусор в окне, которое этот мусор и считает.
            GUIStyle style = MetricLabelStyle;
            s_heroContent.text = value;
            Rect area = GUILayoutUtility.GetRect(s_heroContent, style, s_noExpandWidth);
            ToolChrome.DrawTinted(area, value, style, color);

            GUILayout.Label(unit, ToolTheme.UnitLabel);
            GUILayout.FlexibleSpace();
        }
    }
}
