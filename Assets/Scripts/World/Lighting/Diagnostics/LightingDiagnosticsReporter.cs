#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.World.Lighting.Quality;
using Kern.World.Streaming;
using UnityEngine;

namespace Kern.World.Lighting.Diagnostics;

/// <summary>Снимок кадра, по которому диагностика может о нём судить.</summary>
///
/// Передаётся параметром, а не читается из движка: диагностика обязана
/// описывать кадр, а не участвовать в нём.
internal readonly record struct LightingDiagnosticsContext(
    bool Initialized,
    LightingQualityMode Quality,
    Vector4 WorldRect,
    float CellSize,
    int MaximumIntervalSteps);

/// <summary>
/// Отчётность освещения: стоимость каскадов, дамп кадра и разовый снимок при
/// выходе за бюджет.
/// </summary>
///
/// Вынесено из LightingEngine целиком. Это не путь кадра: ни один метод здесь
/// не участвует в решении света и не может на него повлиять. Держать их рядом
/// с кадром значило каждый раз перечитывать сто строк отчётности, чтобы найти
/// двадцать строк работы.
internal sealed class LightingDiagnosticsReporter(
    LightingResourceManager resources,
    LightingRuntimeState runtimeState,
    IFrameTelemetry telemetry)
{
    // Снимок делается один раз за сессию: нарушение бюджета повторяется каждый
    // кадр, и второй дамп уже ничего не добавит, зато добавит стоимости.
    private bool _budgetViolationCaptured;

    public void CollectCascadeCosts(
        List<CascadeCostSample> destination,
        in LightingDiagnosticsContext context)
    {
        CascadeCostCalculator.CollectCascadeCosts(
            resources.Cascades, context.MaximumIntervalSteps, destination);
    }

    public string? DumpCurrentFrame(
        in LightingDiagnosticsContext context,
        string? targetDirectory)
    {
        if (!context.Initialized || resources.Registry.Compute == null)
        {
            return null;
        }

        return LightingFrameDumper.DumpCurrentFrame(
            resources.Registry,
            context.WorldRect,
            context.CellSize,
            context.Quality,
            telemetry,
            resources.LightingCounters,
            targetDirectory);
    }

    public void CaptureBudgetViolationIfNeeded(in LightingDiagnosticsContext context)
    {
        long estimatedRayWork = CascadeCostCalculator.EstimateRayWorkUnits(resources.Cascades);
        bool staticRayBudgetViolation =
            estimatedRayWork > LightingPerformanceBudget.MaximumStaticCascadeRayWorkUnits;
        bool measuredBudgetViolation =
            !LightingPerformanceBudget.CheckBudget(telemetry, out _) ||
            !LightingPerformanceBudget.CheckFrameBudget(telemetry, out _);
        bool repeatedStaticSolve =
            runtimeState.SolveCount > 1 &&
            telemetry.LightingStaticSolveCount > 0 &&
            (telemetry.LightingRegionChangeCount > 0 ||
                telemetry.LightingFieldRebuildCount > 0);
        bool repeatedTerrainFullRebuild =
            runtimeState.SolveCount > 1 &&
            telemetry.TerrainFullPopulateCount > 1 &&
            (telemetry.StreamingPlanKind == (int)StreamingPlanKind.FullRebuild ||
                telemetry.StreamingPlanKind == (int)StreamingPlanKind.Resize);
        if (_budgetViolationCaptured ||
            !context.Initialized ||
            resources.Registry.Compute == null ||
            !measuredBudgetViolation &&
            !staticRayBudgetViolation &&
            !repeatedStaticSolve &&
            !repeatedTerrainFullRebuild)
        {
            return;
        }

        _budgetViolationCaptured = true;
        Debug.LogWarning(
            $"[Lighting] Budget diagnostic captured: " +
            $"estimatedStaticRayWork={estimatedRayWork}, " +
            $"limit={LightingPerformanceBudget.MaximumStaticCascadeRayWorkUnits}, " +
            $"staticRayBudgetViolation={staticRayBudgetViolation}, " +
            $"measuredBudgetViolation={measuredBudgetViolation}, " +
            $"repeatedStaticSolve={repeatedStaticSolve}, " +
            $"repeatedTerrainFullRebuild={repeatedTerrainFullRebuild}, " +
            $"staticSolves={telemetry.LightingStaticSolveCount}, " +
            $"terrainFullPopulates={telemetry.TerrainFullPopulateCount}.");
        try
        {
            // A violation is already the expensive frame. Full ReadPixels
            // plus PNG encoding here would add seven synchronous GPU/CPU
            // readbacks and turn one bad frame into a visible freeze.
            LightingFrameDumper.DumpCurrentFrame(
                resources.Registry,
                context.WorldRect,
                context.CellSize,
                context.Quality,
                telemetry,
                lightingCounters: null,
                targetDirectory: null,
                includeTextures: false);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Lighting] Budget diagnostic capture failed: {exception.Message}");
        }
    }

    /// <summary>Раскладка каждого каскада строками — для окна инструментов.</summary>
    public static IReadOnlyList<string> DescribeCascadeUniforms(IReadOnlyList<CascadeLayout> cascades)
    {
        var summaries = new List<string>(cascades.Count);
        for (int index = 0; index < cascades.Count; index++)
        {
            CascadeLayout cascade = cascades[index];
            summaries.Add(
                $"Cascade {index}: offset={cascade.Offset}, entries={cascade.EntryCount}, " +
                $"probe={cascade.ProbeWidth}x{cascade.ProbeHeight}, spacing={cascade.ProbeSpacing}, " +
                $"directions={cascade.DirectionCount}, interval={cascade.IntervalStart:F2}..{cascade.IntervalEnd:F2}");
        }

        return summaries;
    }
}
