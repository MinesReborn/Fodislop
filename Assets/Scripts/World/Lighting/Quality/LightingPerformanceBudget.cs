#nullable enable

using Kern.Core;

namespace Kern.World.Lighting.Quality;

public static class LightingPerformanceBudget
{
    public const float FrameBudgetMs = 16.7f;
    public const float CascadeTraceMaxMs = 1.0f;
    public const float CascadeMergeMaxMs = 0.25f;
    public const float DynamicLightingMaxMs = 0.7f;
    public const float BounceMaxMs = 0.5f;
    public const float CompositeMaxMs = 0.15f;
    // Preserve the configured spatial quality and up to 64 angular directions
    // on the normal lighting region. This remains below the previous
    // 256-direction transport estimate.
    public const long MaximumStaticCascadeRayWorkUnits = 1_500_000_000;

    public const float MaxAllowedRegressionFactor = 1.20f; // 20% regression threshold

    public static bool CheckBudget(IFrameTelemetry telemetry, out string? violationReport)
    {
        violationReport = null;

        if (telemetry.LightingCascadeTraceTimeMs > CascadeTraceMaxMs * MaxAllowedRegressionFactor)
        {
            violationReport = $"CascadeTrace ({telemetry.LightingCascadeTraceTimeMs:F2} ms) exceeded budget ({CascadeTraceMaxMs:F2} ms)";
            return false;
        }

        if (telemetry.LightingCascadeMergeTimeMs > CascadeMergeMaxMs * MaxAllowedRegressionFactor)
        {
            violationReport = $"CascadeMerge ({telemetry.LightingCascadeMergeTimeMs:F2} ms) exceeded budget ({CascadeMergeMaxMs:F2} ms)";
            return false;
        }

        if (telemetry.LightingDynamicLightingTimeMs > DynamicLightingMaxMs * MaxAllowedRegressionFactor)
        {
            violationReport = $"DynamicLighting ({telemetry.LightingDynamicLightingTimeMs:F2} ms) exceeded budget ({DynamicLightingMaxMs:F2} ms)";
            return false;
        }

        if (telemetry.LightingBounceTimeMs > BounceMaxMs * MaxAllowedRegressionFactor)
        {
            violationReport = $"Bounce ({telemetry.LightingBounceTimeMs:F2} ms) exceeded budget ({BounceMaxMs:F2} ms)";
            return false;
        }

        if (telemetry.LightingCompositeTimeMs > CompositeMaxMs * MaxAllowedRegressionFactor)
        {
            violationReport = $"Composite ({telemetry.LightingCompositeTimeMs:F2} ms) exceeded budget ({CompositeMaxMs:F2} ms)";
            return false;
        }

        return true;
    }

    public static bool CheckFrameBudget(IFrameTelemetry telemetry, out string? violationReport)
    {
        float terrainMs = telemetry.TerrainMeshTimeMs +
            telemetry.TerrainCacheTimeMs +
            telemetry.TerrainFloodFillTimeMs +
            telemetry.TerrainGpuUploadTimeMs +
            telemetry.TerrainAtlasUploadTimeMs;
        // Stage record times are sub-phases of the build and must not be
        // added on top of it: only top-level phases sum into the frame.
        float lightingMs = telemetry.LightingBuildCommandsTimeMs +
            telemetry.LightingExecuteCommandsTimeMs;
        float totalMs = terrainMs + lightingMs;
        if (totalMs <= FrameBudgetMs)
        {
            violationReport = null;
            return true;
        }

        violationReport =
            $"Frame transport work ({totalMs:F2} ms) exceeded budget ({FrameBudgetMs:F2} ms): " +
            $"terrain={terrainMs:F2} ms, lighting={lightingMs:F2} ms";
        return false;
    }
}
