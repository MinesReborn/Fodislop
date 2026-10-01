#nullable enable

using System.Collections.Generic;
using System.Globalization;
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
    private LightingQualityTuning _draft = LightingQualityTuningController.Current;
    private ulong _draftRevision = LightingQualityTuningController.Revision;
    private string _applyMessage = string.Empty;
    private string _dynamicDetail = NoDetail;
    private string _probeDetail = NoDetail;
    private static readonly string[] _DensityLabels = ["1", "2", "4", "8", "16", "32"];
    private static readonly string[] _ProbeLabels = ["1", "2", "4", "8", "16"];
    private static readonly string[] _StaticAngleLabels = ["4", "8", "16", "32", "64"];
    private static readonly string[] _SampleLabels = ["1", "2", "4", "8", "16", "32", "64"];
    private static readonly string[] _EmitterLabels = ["1×1", "2×2", "3×3", "4×4"];
    private static readonly string[] _PolarLabels = ["4", "8", "16", "32", "64", "128", "256", "512", "1024"];
    private static readonly string[] _NearLabels = ["0.5", "1", "2", "4", "6", "8"];
    private static readonly float[] _NearValues = [0.5f, 1f, 2f, 4f, 6f, 8f];

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
        if (_draftRevision != LightingQualityTuningController.Revision)
        {
            _draft = LightingQualityTuningController.Current;
            _draftRevision = LightingQualityTuningController.Revision;
        }
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
        _draft = LightingQualityTuningController.Current;
        _draftRevision = LightingQualityTuningController.Revision;
        _applyMessage = string.Empty;
        _dynamicDetail = NoDetail;
        _probeDetail = NoDetail;
    }

    private void RefreshLimits(LightingEngine lighting)
    {
        _fieldLimited = lighting.TextureDimensionLimited;
        _cascadeLimited = lighting.CascadeBudgetLimited;
        _fieldDetail =
            $"Перенос {lighting.FieldWidth}×{lighting.FieldHeight} при {lighting.EffectivePixelsPerCell:F2} пикс/клетку, " +
            $"карта света {lighting.LightWidth}×{lighting.LightHeight}";
        _cascadeDetail = $"{lighting.CascadeCount} каскадов, шагов до {lighting.MaximumIntervalSteps}";
        _atlasDetail = $"{lighting.AtlasEntryCount} записей, источников {lighting.DynamicLightCount}";
        _probeDetail = $"Пробы/клетку: запрос {LightingQualityTuningController.CascadeProbePixelsPerCell}, " +
            $"фактически {lighting.EffectiveCascadeProbesPerCell:0.##}";
        _dynamicDetail = $"Последний кадр: {_telemetry.LightingDynamicDispatchPixels:N0} пикселей, " +
            $"{_telemetry.LightingPolarRayWorkUnits:N0} отсчётов веера. " +
            "GPU-время отдельных этапов здесь недоступно.";
    }

    private void Recalculate()
    {
        _rows.Clear();
        _totalRaySteps = 0;
        _totalMergeTaps = 0;
        _heaviestRaySteps = 0;

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
            DrawQualityTuning();
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
            GUILayout.Label(_probeDetail, MutedLabelStyle);
            GUILayout.Label(_dynamicDetail, MutedLabelStyle);
            DrawDiagnostics();
        }
    }

    private void DrawQualityTuning()
    {
        ToolChrome.SectionHeader("КАЧЕСТВО СВЕТА · VISUAL TUNING");
        GUILayout.Label("Мир и AO: 32×32 на клетку. Поля ниже задают отдельную сетку света. " +
            "Выбери значения и нажми «Применить». Правка действует до конца сессии.", MutedLabelStyle);
        if (_lighting?.ActiveGraphicsPreset == Kern.Rendering.GraphicsPreset.Standard)
        {
            GUILayout.Label("Сейчас «Стандарт»: рассчитывается только AO. Для оценки света выбери Overdrive.",
                MutedLabelStyle);
        }

        int field = DrawPowerOfTwo("Перенос: материал и эмиссия · пикс/клетку", _draft.FieldPixelsPerCell, _DensityLabels, 1);
        _draft = _draft with
        {
            FieldPixelsPerCell = field,
            LightPixelsPerCell = System.Math.Min(field, _draft.LightPixelsPerCell),
            CascadeProbePixelsPerCell = System.Math.Min(field, _draft.CascadeProbePixelsPerCell),
        };
        GUILayout.Label("Сетка, по которой идут лучи: альбедо, эмиссия, силуэты стен. Однородные клетки " +
            "лучи проходят за один шаг, поэтому цена растёт в основном на краях и светящихся клетках.",
            MutedLabelStyle);
        _draft = _draft with
        {
            LightPixelsPerCell = System.Math.Min(field,
                DrawPowerOfTwo("Карта света · пикс/клетку", _draft.LightPixelsPerCell, _DensityLabels, 1)),
        };
        GUILayout.Label("Приёмники static/dynamic direct и итоговая карта света; не больше переноса. " +
            "Уменьшение вдвое сокращает число приёмников в четыре раза.", MutedLabelStyle);
        _draft = _draft with
        {
            CascadeProbePixelsPerCell = System.Math.Min(field,
                DrawPowerOfTwo("Статика · проб/клетку", _draft.CascadeProbePixelsPerCell, _ProbeLabels, 1)),
            MaximumStaticCascadeDirections = DrawPowerOfTwo("Статика · предел направлений",
                _draft.MaximumStaticCascadeDirections, _StaticAngleLabels, 4),
        };
        GUILayout.Label("Плотность и направления применяются точно. Если атлас не вмещает запрос, " +
            "«Применить» покажет причину; автоматического снижения нет.",
            MutedLabelStyle);

        GUILayout.Label("Динамика · радиус точного DDA, клетки", WrappedLabelStyle);
        int nearIndex = System.Array.IndexOf(_NearValues, _draft.DynamicNearCells);
        int nextNear = GUILayout.SelectionGrid(nearIndex, _NearLabels, 3, ToolTheme.SegmentedButton);
        if (nextNear >= 0)
        {
            _draft = _draft with { DynamicNearCells = _NearValues[nextNear] };
        }
        GUILayout.Label("Ближняя зона — дорогая трассировка каждого пикселя; за её границей используется веер.",
            MutedLabelStyle);
        _draft = _draft with
        {
            DynamicAngularSampleCount = DrawPowerOfTwo("Динамика · выборок на пиксель",
                _draft.DynamicAngularSampleCount, _SampleLabels, 1),
            DynamicEmitterPointsPerAxis = 1 + DrawEmitterIndex(),
            DynamicPolarDirectionCount = DrawPowerOfTwo("Динамика · углов веера на точку",
                _draft.DynamicPolarDirectionCount, _PolarLabels, 4),
        };
        GUILayout.Label("Цена веера растёт с числом углов и квадратом числа точек по оси. " +
            "Длина луча и частота обновления сохраняются.", MutedLabelStyle);

        using (ToolLayout.Horizontal())
        {
            if (GUILayout.Button("Применить") && _draft != LightingQualityTuningController.Current)
            {
                if (_lighting!.TryApplyQualityTuning(_draft, out string rejection))
                {
                    _draftRevision = LightingQualityTuningController.Revision;
                    _applyMessage = "Применено. Свет пересчитается в следующем мировом кадре.";
                    _nextUpdate = 0f;
                }
                else
                {
                    _applyMessage = rejection;
                }
            }
            if (GUILayout.Button("Загрузить текущие"))
            {
                _draft = LightingQualityTuningController.Current;
                _applyMessage = string.Empty;
            }
        }
        if (GUILayout.Button("Копировать в VisualTuning"))
        {
            GUIUtility.systemCopyBuffer =
                "public static readonly LightingQualityTuning DefaultQuality = new(\n" +
                $"    FieldPixelsPerCell: {_draft.FieldPixelsPerCell},\n" +
                $"    LightPixelsPerCell: {_draft.LightPixelsPerCell},\n" +
                $"    CascadeProbePixelsPerCell: {_draft.CascadeProbePixelsPerCell},\n" +
                $"    MaximumStaticCascadeDirections: {_draft.MaximumStaticCascadeDirections},\n" +
                $"    DynamicNearCells: {_draft.DynamicNearCells.ToString("0.0###", CultureInfo.InvariantCulture)}f,\n" +
                $"    DynamicAngularSampleCount: {_draft.DynamicAngularSampleCount},\n" +
                $"    DynamicEmitterPointsPerAxis: {_draft.DynamicEmitterPointsPerAxis},\n" +
                $"    DynamicPolarDirectionCount: {_draft.DynamicPolarDirectionCount});";
            _applyMessage = "Блок скопирован. Вставь вместо DefaultQuality в VisualTuning.cs для сохранения.";
        }
        if (_applyMessage.Length > 0)
        {
            GUILayout.Label(_applyMessage, MutedLabelStyle);
        }
    }

    private int DrawEmitterIndex()
    {
        GUILayout.Label("Динамика · точки источника", WrappedLabelStyle);
        return GUILayout.SelectionGrid(_draft.DynamicEmitterPointsPerAxis - 1,
            _EmitterLabels, 4, ToolTheme.SegmentedButton);
    }

    private static int DrawPowerOfTwo(string label, int value, string[] labels, int first)
    {
        GUILayout.Label(label, ToolTheme.WrappedLabel);
        int index = -1;
        for (int candidate = 0; candidate < labels.Length; candidate++)
        {
            if ((first << candidate) == value)
            {
                index = candidate;
                break;
            }
        }
        int selected = GUILayout.SelectionGrid(index, labels, 3, ToolTheme.SegmentedButton);
        return selected < 0 ? value : first << selected;
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
                // Значимая длина — в счётчике: массив записи переиспользуется и
                // в хвосте держит пустые строки прошлой записи.
                GUILayout.Label(
                    $"  Запущено: {string.Join(", ", rec.ExecutedPasses, 0, rec.ExecutedCount)}",
                    MutedLabelStyle);
                if (rec.SkippedCount > 0)
                {
                    GUILayout.Label(
                        $"  Пропущено: {string.Join(", ", rec.SkippedPasses, 0, rec.SkippedCount)}",
                        MutedLabelStyle);
                }
                GUILayout.Space(2f);
            }
        }
    }
}
