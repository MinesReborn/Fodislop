#nullable enable

using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World.Lighting;
using Kern.World.Lighting.Quality;
using UnityEngine;

namespace Kern.Tools.Imgui.Windows;

public sealed class LightingCostWindow : ToolWindow
{
    private const float RefreshInterval = 0.5f;

    private const string FieldTitle = "Разрешение поля";
    private const string FieldTitleLimited = "Разрешение поля — упёрлось в потолок";
    private const string CascadeTitle = "Число каскадов";
    private const string CascadeTitleLimited = "Число каскадов — упёрлось в потолок";
    private const string AtlasTitle = "Атлас проб";
    private const string NoDetail = "--";

    private readonly LightingEngine? _lighting;
    private readonly IFrameTelemetry _telemetry;
    private readonly List<CascadeCostSample> _samples = [];
    private readonly List<string> _rows = [];

    private long _totalRaySteps;
    private long _totalMergeTaps;
    private long _heaviestRaySteps;
    private string _summary = "нет данных";
    private string _solveMix = "решений: --";

    // Строки пределов собираются по таймеру, а не на каждое событие IMGUI:
    // окно открыто, пока смотрят на цену кадра, и мусор здесь искажает то,
    // что окно показывает.
    private bool _fieldLimited;
    private bool _cascadeLimited;
    private string _fieldDetail = NoDetail;
    private string _cascadeDetail = NoDetail;
    private string _atlasDetail = NoDetail;
    private float _nextUpdate;
    private Vector2 _scroll;

    public LightingCostWindow(LightingEngine? lighting, IFrameTelemetry telemetry)
        : base("Цена света", new Rect(292f, 382f, 320f, 340f))
    {
        _lighting = lighting;
        _telemetry = telemetry;
    }

    public override bool WantsSampling => false;

    public override Vector2 MinimumSize => new(280f, 260f);

    public override void Tick()
    {
        if (!Visible || _lighting == null || Time.unscaledTime < _nextUpdate)
        {
            return;
        }

        _nextUpdate = Time.unscaledTime + RefreshInterval;
        RefreshLimits(_lighting);
        if (!_lighting.IsInitialized)
        {
            _summary = "движок света ещё не готов";
            _rows.Clear();
            return;
        }

        _lighting.CollectCascadeCosts(_samples);
        Recalculate();
    }

    protected override void OnPlaySessionReset()
    {
        _scroll = default;
        _nextUpdate = 0f;
        _samples.Clear();
        _rows.Clear();
        _totalRaySteps = 0;
        _totalMergeTaps = 0;
        _heaviestRaySteps = 0;
        _summary = "нет данных";
        _solveMix = "решений: --";
        _fieldLimited = false;
        _cascadeLimited = false;
        _fieldDetail = NoDetail;
        _cascadeDetail = NoDetail;
        _atlasDetail = NoDetail;
    }

    private void RefreshLimits(LightingEngine lighting)
    {
        _fieldLimited = lighting.TextureDimensionLimited;
        _cascadeLimited = lighting.CascadeBudgetLimited;
        _fieldDetail =
            $"{lighting.FieldWidth}×{lighting.FieldHeight} при {lighting.EffectivePixelsPerCell:F2} пикс/клетку";
        if (lighting.ActiveLightingQuality == LightingQualityMode.PerBlock)
        {
            _cascadeDetail = "static cascade cache, результат усреднён по клетке";
            _atlasDetail = $"источников {lighting.DynamicLightCount}";
        }
        else
        {
            _cascadeDetail = $"{lighting.CascadeCount} каскадов, шагов до {lighting.MaximumIntervalSteps}";
            _atlasDetail = $"{lighting.AtlasEntryCount} записей, источников {lighting.DynamicLightCount}";
        }
    }

    private void Recalculate()
    {
        _rows.Clear();
        _totalRaySteps = 0;
        _totalMergeTaps = 0;
        _heaviestRaySteps = 0;

        if (_lighting?.ActiveLightingQuality == LightingQualityMode.PerBlock)
        {
            _summary = "Режим: По блокам (cascade cache + targeted dynamic light)";
            _solveMix = $"за секунду: {_telemetry.LightingStaticSolveCount} решений";
            _rows.Add("Динамика: только изменившиеся тайлы источников");
            _rows.Add("Разрешение: ровно 1 тексель на блок (Point sampling)");
            return;
        }

        foreach (CascadeCostSample sample in _samples)
        {
            _totalRaySteps += sample.RayStepCount;
            _totalMergeTaps += sample.MergeTapCount;
            _heaviestRaySteps = System.Math.Max(_heaviestRaySteps, sample.RayStepCount);
            _rows.Add(
                $"К{sample.Index}  {sample.DirectionCount} напр  {sample.ProbeWidth}×{sample.ProbeHeight}  " +
                $"шагов {sample.StepCount}  ·  {Millions(sample.RayStepCount)} шагов луча");
        }

        _summary =
            $"{Millions(_totalRaySteps)} шагов луча  +  {Millions(_totalMergeTaps)} выборок атласа " +
            $"за одно полное решение";
        _solveMix =
            $"за секунду: статических {_telemetry.LightingStaticSolveCount}, " +
            $"динамических {_telemetry.LightingDynamicSolveCount}";
    }

    private static string Millions(long value) =>
        value >= 1_000_000
            ? $"{value / 1_000_000.0:F1} М"
            : $"{value / 1000.0:F0} К";

    protected override void DrawContent()
    {
        if (_lighting == null)
        {
            GUILayout.Label("Движок освещения недоступен.", MutedLabelStyle);
            return;
        }

        using (ToolLayout.ScrollView(ref _scroll))
        {
            if (_lighting.BypassLightingCompute)
            {
                ToolChrome.Banner("РАСЧЁТ ОБОЙДЁН", ToolTheme.Error);
                GUILayout.Space(4f);
            }

            ToolChrome.SectionHeader("ПОЛНОЕ РЕШЕНИЕ");
            GUILayout.Label(_summary, MutedLabelStyle);
            GUILayout.Label(_solveMix, MutedLabelStyle);

            ToolChrome.SectionHeader("КАСКАДЫ");
            DrawCascadeRows();
            DrawLimits();
            DrawDiagnostics();
        }
    }

    private void DrawCascadeRows()
    {
        if (_rows.Count == 0)
        {
            GUILayout.Label("Раскладка каскадов ещё не собрана.", MutedLabelStyle);
            return;
        }

        for (int i = 0; i < _rows.Count && i < _samples.Count; i++)
        {
            GUILayout.Label(_rows[i], MutedLabelStyle);
            float share = _heaviestRaySteps > 0
                ? _samples[i].RayStepCount / (float)_heaviestRaySteps
                : 0f;
            ToolChrome.MeterLine(share, i == 0 ? ToolTheme.Warning : ToolTheme.FrameGraphColor);
            GUILayout.Space(3f);
        }
    }

    private void DrawLimits()
    {
        ToolChrome.SectionHeader("ПРЕДЕЛЫ");
        DrawLimitRow(_fieldLimited ? FieldTitleLimited : FieldTitle, _fieldLimited, _fieldDetail);
        DrawLimitRow(_cascadeLimited ? CascadeTitleLimited : CascadeTitle, _cascadeLimited, _cascadeDetail);
        DrawLimitRow(AtlasTitle, false, _atlasDetail);
    }

    private static void DrawLimitRow(string title, bool limited, string detail)
    {
        using (ToolLayout.Horizontal())
        {
            ToolChrome.StatusPip(limited ? ToolTheme.Warning : ToolTheme.Success);
            using (ToolLayout.Vertical())
            {
                GUILayout.Label(title, WrappedLabelStyle);
                GUILayout.Label(detail, MutedLabelStyle);
            }
        }

        GUILayout.Space(3f);
    }

    private void DrawDiagnostics()
    {
        ToolChrome.SectionHeader("ДАМП КАДРА");
        if (GUILayout.Button("Dump Lighting Frame"))
        {
            _lighting?.DumpCurrentFrame();
        }

        if (_lighting != null && _lighting.Journal.Count > 0)
        {
            ToolChrome.SectionHeader("ЖУРНАЛ ИНВАЛИДАЦИИ (ПОСЛЕДНИЕ СОБЫТИЯ)");
            var recent = _lighting.Journal.GetRecent(3);
            foreach (var rec in recent)
            {
                GUILayout.Label($"Кадр #{rec.FrameIndex}: {rec.Reason}", WrappedLabelStyle);
                GUILayout.Label($"  Запущено: {string.Join(", ", rec.ExecutedPasses)}", MutedLabelStyle);
                if (rec.SkippedPasses.Length > 0)
                {
                    GUILayout.Label($"  Пропущено: {string.Join(", ", rec.SkippedPasses)}", MutedLabelStyle);
                }
                GUILayout.Space(2f);
            }
        }
    }
}
