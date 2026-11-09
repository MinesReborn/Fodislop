#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World.Lighting;
using Kern.World.Lighting.Quality;
using UnityEngine;

using Kern.Rendering.Diagnostics.Tools;

using Kern.Diagnostics.World;

namespace Kern.Rendering.Diagnostics.Windows;

public sealed class LightingCostWindow : ToolWindow
{
    private const float RefreshInterval = 0.5f;

    private const string FieldTitle = "Разрешение поля";
    private const string CascadeTitle = "Число каскадов";
    private const string AtlasTitle = "Атлас проб";
    private const string NoDetail = "--";

    private readonly LightingEngine? _lighting;
    private readonly IFrameTelemetry _telemetry;
    private readonly List<CascadeCostSample> _samples = new();
    private readonly List<string> _rows = new();

    private long _totalRaySteps;
    private long _totalMergeTaps;
    private long _heaviestRaySteps;
    private int _lastStaticSolveCount;
    private int _lastDynamicSolveCount;
    private double _lastSolveSampleTime;
    private bool _hasSolveSample;
    private string _summary = "нет данных";
    private string _solveMix = "решений/с: --";

    // Строки пределов собираются по таймеру, а не на каждое событие IMGUI:
    // окно открыто, пока смотрят на цену кадра, и мусор здесь искажает то,
    // что окно показывает.
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
    private static readonly string[] s_densityLabels = { "1", "2", "4", "8", "16", "32" };
    private static readonly string[] s_probeLabels = { "1", "2", "4", "8", "16" };
    private static readonly string[] s_staticAngleLabels = { "4", "8", "16", "32", "64" };
    private static readonly int[] s_sampleValues = { 1, 2, 4, 6, 8, 12, 16, 32, 64 };
    private static readonly string[] s_sampleLabels = { "1", "2", "4", "6", "8", "12", "16", "32", "64" };
    private static readonly string[] s_emitterLabels = { "1×1", "2×2", "3×3", "4×4" };
    private static readonly string[] s_polarLabels = { "4", "8", "16", "32", "64", "128", "256", "512", "1024" };
    private static readonly string[] s_nearLabels = { "0.5", "1", "2", "4", "6", "8" };
    private static readonly float[] s_nearValues = { 0.5f, 1f, 2f, 4f, 6f, 8f };

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
        UpdateSolveMix(Time.unscaledTimeAsDouble);
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
        _solveMix = "решений/с: --";
        _lastStaticSolveCount = 0;
        _lastDynamicSolveCount = 0;
        _lastSolveSampleTime = 0d;
        _hasSolveSample = false;
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
        Vector4 worldRect = lighting.WorldRect;
        int aoWidth = Mathf.RoundToInt(worldRect.z / lighting.CellSize * LightingConfigHolder.AmbientOcclusionPixelsPerCell);
        int aoHeight = Mathf.RoundToInt(worldRect.w / lighting.CellSize * LightingConfigHolder.AmbientOcclusionPixelsPerCell);
        long aoBytes = (long)aoWidth * aoHeight;
        _fieldDetail =
            $"Перенос {lighting.FieldWidth}×{lighting.FieldHeight} при {lighting.EffectivePixelsPerCell:F2} пикс/клетку, " +
            $"карта света {lighting.LightWidth}×{lighting.LightHeight} " +
            $"({LightingQualityTuningController.LightPixelsPerCell} пикс/клетку); " +
            $"AO R8 {aoWidth}×{aoHeight} ({LightingConfigHolder.AmbientOcclusionPixelsPerCell} пикс/клетку, фиксировано, " +
            $"около {aoBytes / (1024f * 1024f):N0} МиБ)";
        _cascadeDetail = $"{lighting.CascadeCount} каскадов";
        _atlasDetail = $"каскады: {lighting.AtlasEntryCount:N0}/{lighting.AtlasCapacity:N0} записей, " +
            $"источников динамики {lighting.DynamicLightCount}";
        _probeDetail = $"Пробы/клетку: запрос {LightingQualityTuningController.CascadeProbePixelsPerCell}, " +
            $"фактически {lighting.EffectiveCascadeProbesPerCell:0.##}";
        int lightDensity = LightingQualityTuningController.LightPixelsPerCell;
        int probeDensity = LightingQualityTuningController.CascadeProbePixelsPerCell;
        if (probeDensity < lightDensity)
        {
            // Static light is reconstructed between probes; dynamic light is
            // evaluated at every receiver. Unequal densities give unequal edges.
            _probeDetail += $". Статика грубее карты света в {lightDensity / probeDensity} раз: " +
                "тени от светящихся блоков мягче теней от фонарей.";
        }
        string transport = LightingQualityTuningController.DynamicTransportMode switch
        {
            DynamicLightingTransportMode.ExactDda => "точный DDA",
            DynamicLightingTransportMode.AcceleratedUniformRegions => "DDA + uniform skip",
            DynamicLightingTransportMode.JumpFloodSdfSphereTracing => "JFA SDF + sphere tracing",
            _ => throw new ArgumentOutOfRangeException(),
        };
        string batching = LightingQualityTuningController.BatchDynamicLights ? "batch включён" : "batch выключен";
        string applied = LightingQualityTuningController.IsDynamicExecutionModeApplied
            ? "применён"
            : "ожидает пересчёта";
        string execution = _lighting?.ActiveGraphicsPreset == Kern.Rendering.GraphicsPreset.Classic
            ? "не выполняется: пресет «Классика» отключает динамический свет"
            : applied;
        IFrameTelemetryProducerStamp? producerStamp = _telemetry as IFrameTelemetryProducerStamp;
        int producerFrameAge = producerStamp is { ProducerLifecycleValid: true }
            ? Time.frameCount - producerStamp.ProducerFrameId
            : int.MaxValue;
        bool hasCurrentFrameSnapshot = producerStamp is { ProducerLifecycleValid: true } &&
            producerStamp.ProducerFrameId >= 0 && producerFrameAge is >= 0 and <= 1;
        string frameMetrics = hasCurrentFrameSnapshot
            ? $"Снимок кадра {producerStamp!.ProducerFrameId}: {_telemetry.LightingDynamicReceiverDispatchCount} dispatch-приёмников, " +
              $"{_telemetry.LightingDynamicPolarDispatchCount} dispatch-вейеров, " +
              $"дескрипторы {_telemetry.LightingDynamicBatchDescriptorBytes:N0} Б; " +
              $"{_telemetry.LightingDynamicDispatchPixels:N0} пикселей и " +
              $"{_telemetry.LightingPolarRayWorkUnits:N0} отсчётов веера."
            : "Покадровые показатели: нет актуального снимка.";
        _dynamicDetail = $"Режим: {transport}, {batching} ({execution}). {frameMetrics} " +
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
                $"оценка {sample.StepCount} шагов на луч  ·  {Millions(sample.RayStepCount)} шагов луча");
        }

        _summary =
            $"верхняя оценка на полное решение: {Millions(_totalRaySteps)} шагов луча  +  " +
            $"{Millions(_totalMergeTaps)} выборок атласа; это не GPU-замер";
    }

    private void UpdateSolveMix(double nowSeconds)
    {
        int staticSolveCount = _telemetry.LightingStaticSolveCount;
        int dynamicSolveCount = _telemetry.LightingDynamicSolveCount;
        if (_hasSolveSample && nowSeconds > _lastSolveSampleTime &&
            staticSolveCount >= _lastStaticSolveCount && dynamicSolveCount >= _lastDynamicSolveCount)
        {
            double elapsedSeconds = nowSeconds - _lastSolveSampleTime;
            double staticPerSecond = (staticSolveCount - _lastStaticSolveCount) / elapsedSeconds;
            double dynamicPerSecond = (dynamicSolveCount - _lastDynamicSolveCount) / elapsedSeconds;
            _solveMix = $"решений/с за {elapsedSeconds:F1} с: статических {staticPerSecond:F1}, динамических {dynamicPerSecond:F1}";
        }
        else
        {
            _solveMix = "решений/с: нет интервала для расчёта";
        }

        _lastStaticSolveCount = staticSolveCount;
        _lastDynamicSolveCount = dynamicSolveCount;
        _lastSolveSampleTime = nowSeconds;
        _hasSolveSample = true;
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
        ToolChrome.SectionHeader("АЛГОРИТМ ДИНАМИЧЕСКОГО СВЕТА");
        if (_lighting == null)
        {
            ToolChrome.Banner("СИСТЕМА ОСВЕЩЕНИЯ НЕДОСТУПНА", ToolTheme.Warning);
            return;
        }

        if (_lighting.ActiveGraphicsPreset == Kern.Rendering.GraphicsPreset.Classic)
        {
            ToolChrome.Banner(
                "ПРЕСЕТ «КЛАССИКА» ОТКЛЮЧАЕТ ДИНАМИЧЕСКИЙ СВЕТ · DDA / JFA СЕЙЧАС НЕ ВЫПОЛНЯЮТСЯ",
                ToolTheme.Warning);
        }
        GUILayout.Label("Выбор действует сразу до конца сессии; при смене света пересчитывается.", MutedLabelStyle);
        int transportMode = (int)LightingQualityTuningController.DynamicTransportMode;
        int nextTransportMode = GUILayout.SelectionGrid(transportMode,
            new[] { "Точный DDA", "Однородные области", "SDF / JFA" }, 3, ToolTheme.SegmentedButton);
        if (nextTransportMode >= 0 && nextTransportMode != transportMode)
        {
            if (!_lighting.TrySetDynamicTransportMode(
                (DynamicLightingTransportMode)nextTransportMode, out string rejection))
            {
                _applyMessage = rejection;
            }
            else
            {
                _applyMessage = string.Empty;
            }
        }
        GUILayout.Label("Сравнивай режимы на одной сцене при одинаковых настройках и источниках света.", MutedLabelStyle);

        GUILayout.Label("Батчинг нескольких фонарей включён постоянно.", MutedLabelStyle);
        bool acceleratedTransport = LightingQualityTuningController.DynamicTransportMode ==
            DynamicLightingTransportMode.AcceleratedUniformRegions;
        bool sdfTransport = LightingQualityTuningController.DynamicTransportMode ==
            DynamicLightingTransportMode.JumpFloodSdfSphereTracing;
        string selectedTransport = acceleratedTransport
            ? "DDA + проверка однородной области"
            : sdfTransport ? "JFA SDF + sphere tracing у препятствий" : "точный DDA";
        GUILayout.Label($"Выбрано: {selectedTransport}; " +
            $"batch {(LightingQualityTuningController.BatchDynamicLights ? "включён" : "выключен")}; " +
            (LightingQualityTuningController.IsDynamicExecutionModeApplied
                ? "настройка принята движком"
                : "ожидается следующий пересчёт"), MutedLabelStyle);
        GUILayout.Label(acceleratedTransport
            ? "Ускоряется только ближний луч в доказанно однородной области; на остальных участках работает DDA."
            : sdfTransport
                ? "JFA строит поле расстояний до занятых текселей; сфера проходит свободный воздух, затем точный DDA обрабатывает границу и материал."
                : "Точный DDA проходит геометрию динамического света.", MutedLabelStyle);

        GUILayout.Space(10f);
        ToolChrome.SectionHeader("КАЧЕСТВО СВЕТА · VISUAL TUNING");
        GUILayout.Label("Мир и AO остаются в точной сетке 32×32 на клетку. Ниже настраиваются " +
            "плотность переноса и карта света; плотность проб каскадов настраивается отдельно. " +
            "Выбери значения и нажми «Применить». Правка действует до конца сессии.", MutedLabelStyle);
        DrawQualityApplyControls();
        if (_lighting?.ActiveGraphicsPreset == Kern.Rendering.GraphicsPreset.Classic)
        {
            GUILayout.Label("Сейчас «Классика»: рассчитывается только AO. Для оценки света выбери «Стандарт».",
                MutedLabelStyle);
        }

        int field = LightingQualitySelectionControls.DrawPowerOfTwo("Перенос: материал и свечение · пикс/клетку", _draft.FieldPixelsPerCell, s_densityLabels, 1);
        _draft = _draft with
        {
            FieldPixelsPerCell = field,
            LightPixelsPerCell = System.Math.Min(field, _draft.LightPixelsPerCell),
            CascadeProbePixelsPerCell = System.Math.Min(field, _draft.CascadeProbePixelsPerCell),
        };
        GUILayout.Label("Сетка, по которой идут лучи: альбедо, свечение, силуэты стен. Однородные клетки " +
            "лучи проходят за один шаг, поэтому цена растёт в основном на краях и светящихся клетках.",
            MutedLabelStyle);
        _draft = _draft with
        {
            LightPixelsPerCell = System.Math.Min(field,
                LightingQualitySelectionControls.DrawPowerOfTwo("Карта света · пикс/клетку", _draft.LightPixelsPerCell, s_densityLabels, 1)),
        };
        GUILayout.Label("Приёмники static/dynamic direct и итоговая карта света; не больше переноса. " +
            "Уменьшение вдвое сокращает число приёмников в четыре раза.", MutedLabelStyle);
        _draft = _draft with
        {
            CascadeProbePixelsPerCell = System.Math.Min(field,
                LightingQualitySelectionControls.DrawPowerOfTwo("Статика · проб/клетку", _draft.CascadeProbePixelsPerCell, s_probeLabels, 1)),
            MaximumStaticCascadeDirections = LightingQualitySelectionControls.DrawPowerOfTwo("Статика · предел направлений",
                _draft.MaximumStaticCascadeDirections, s_staticAngleLabels, 4),
        };
        GUILayout.Label("Плотность и направления применяются точно. Если поле или атлас " +
            "не вмещаются, «Применить» покажет причину; автоматического снижения нет.",
            MutedLabelStyle);

        GUILayout.Label("Динамика · радиус точного DDA, клетки", WrappedLabelStyle);
        int nearIndex = System.Array.IndexOf(s_nearValues, _draft.DynamicNearCells);
        int nextNear = GUILayout.SelectionGrid(nearIndex, s_nearLabels, 3, ToolTheme.SegmentedButton);
        if (nextNear >= 0)
        {
            _draft = _draft with { DynamicNearCells = s_nearValues[nextNear] };
        }
        GUILayout.Label("Ближняя зона — дорогая трассировка каждого пикселя; за её границей используется веер.",
            MutedLabelStyle);
        _draft = _draft with
        {
            DynamicAngularSampleCount = LightingQualitySelectionControls.DrawDiscreteSamples("Динамика · выборок на пиксель",
                _draft.DynamicAngularSampleCount, s_sampleLabels, s_sampleValues),
            DynamicEmitterPointsPerAxis = 1 + DrawEmitterIndex(),
            DynamicPolarDirectionCount = LightingQualitySelectionControls.DrawPowerOfTwo("Динамика · углов веера на точку",
                _draft.DynamicPolarDirectionCount, s_polarLabels, 4),
        };
        GUILayout.Label("Цена веера растёт с числом углов и квадратом числа точек по оси. " +
            "Длина луча и частота обновления сохраняются.", MutedLabelStyle);
        if (_lighting != null && _lighting.CellSize > 0f)
        {
            Vector4 worldRect = _lighting.WorldRect;
            int gridWidth = Mathf.RoundToInt(worldRect.z / _lighting.CellSize);
            int gridHeight = Mathf.RoundToInt(worldRect.w / _lighting.CellSize);
            long receiverSamples = (long)gridWidth * _draft.LightPixelsPerCell *
                gridHeight * _draft.LightPixelsPerCell * _lighting.DynamicLightCount *
                _draft.DynamicAngularSampleCount;
            GUILayout.Label($"Верхняя оценка при полном покрытии карты: {receiverSamples:N0} " +
                "пиксель-сэмплов приёмника (площадь карты × фонари × выборки). Это не замер GPU.",
                MutedLabelStyle);
        }

        DrawQualityApplyControls();
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

    private void DrawQualityApplyControls()
    {
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
        if (_applyMessage.Length > 0)
        {
            GUILayout.Label(_applyMessage, MutedLabelStyle);
        }
    }

    private int DrawEmitterIndex()
    {
        GUILayout.Label("Динамика · точки источника", WrappedLabelStyle);
        return GUILayout.SelectionGrid(_draft.DynamicEmitterPointsPerAxis - 1,
            s_emitterLabels, 4, ToolTheme.SegmentedButton);
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
        DrawLimitRow(FieldTitle, false, _fieldDetail);
        DrawLimitRow(CascadeTitle, false, _cascadeDetail);
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
