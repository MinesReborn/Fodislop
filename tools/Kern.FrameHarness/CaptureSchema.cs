using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kern.FrameHarness;

public enum CheckStatus
{
    Pass,
    Fail,
    Incomplete,
}

public enum SampleClass
{
    Steady,
    Cold,
    Reanchor,
}

public sealed class FrameCapture
{
    public int SchemaVersion { get; init; }
    public string HarnessVersion { get; init; } = string.Empty;
    public string CaptureId { get; init; } = string.Empty;
    public string CapturedAtUtc { get; init; } = string.Empty;
    public CaptureManifest? Manifest { get; init; } = new();
    public CounterSnapshot? CounterBaseline { get; init; }
    public int? CounterBaselineGeneration { get; init; }
    public long? BaselineObservationFrameId { get; init; }
    public long? BaselineProducerFrameId { get; init; }
    public bool? BaselineProducerLifecycleValid { get; init; }
    public FrameInputObservation? InputBaseline { get; init; }
    public List<FrameSample?>? Frames { get; init; } = [];
}

public sealed class CaptureManifest
{
    public string ScenarioId { get; init; } = string.Empty;
    public string CapturePhase { get; init; } = string.Empty;
    public string? WorkloadHash { get; init; }
    public BuildIdentity? Build { get; init; } = new();
    public RuntimeIdentity? Runtime { get; init; } = new();
    public Dictionary<string, string?>? UnavailableMetrics { get; init; } = new(StringComparer.Ordinal);
    public bool? VisualCoverage { get; init; }
    public string? VisualEvidence { get; init; }
    public string? ObservationEvidence { get; init; }
}

public sealed class BuildIdentity
{
    // Values describe the loaded player/editor binary only. They do not identify
    // the source tree currently on disk.
    public string? ApplicationVersion { get; init; }
    public string? LoadedArtifactId { get; init; }
    public string? ArtifactScope { get; init; }
}

public sealed class RuntimeIdentity
{
    public string? UnityVersion { get; init; }
    public string? Platform { get; init; }
    public string? GraphicsApi { get; init; }
    public string? GraphicsDevice { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? QualityProfile { get; init; }
}

public sealed class FrameInputObservation
{
    public long? WorldGeneration { get; init; }
    public long? TerrainGeometryRevision { get; init; }
    public long? TerrainWindowRevision { get; init; }
    public long? LightingRegionRevision { get; init; }
    public long? DynamicLightsRevision { get; init; }
    public long? SettingsRevision { get; init; }
    public long? ResourceGeneration { get; init; }
    public long? ContributorRevision { get; init; }
    public double? CameraX { get; init; }
    public double? CameraY { get; init; }
    public bool? TerrainReady { get; init; }
    public bool? LightingReady { get; init; }
    public bool? CameraWithinPreparedWindow { get; init; }
    public bool? WorkQueuesDrained { get; init; }
    public string? SourceChangeKind { get; init; }
}

public sealed class FrameSample
{
    [JsonRequired]
    public long FrameId { get; init; }
    public long? ProducerFrameId { get; init; }
    public bool? ProducerLifecycleValid { get; init; }
    public SampleClass? Class { get; init; }
    public double? FrameDurationMs { get; init; }
    public Dictionary<string, double?>? CpuMs { get; init; } = new(StringComparer.Ordinal);
    public double? GpuFrameMs { get; init; }
    public FrameInputObservation? Inputs { get; init; }
    [JsonRequired]
    public int CounterGeneration { get; init; }
    [JsonRequired]
    public bool CounterResetObserved { get; init; }
    public CounterSnapshot? Cumulative { get; init; } = new();
    public FrameCounterSnapshot? FrameCounters { get; init; } = new();
    public TerrainTextureUploadObservation? TerrainCellDataUpload { get; init; }
}

public sealed class CounterSnapshot
{
    public long? TerrainRebuilds { get; init; }
    public long? TerrainFullPopulates { get; init; }
    public long? TerrainDirtyPatches { get; init; }
    public long? TerrainChunkLoads { get; init; }
    public long? LightingDynamicSolves { get; init; }
    public long? LightingDynamicTraces { get; init; }
    public long? LightingAtlasScrolls { get; init; }
    public long? TerrainCellDataApplyCalls { get; init; }
    public long? TerrainCellDataApplyPayloadBytes { get; init; }
    public long? TerrainCellDataCopyTextureCalls { get; init; }
    public long? TerrainCellDataCopyTexturePayloadBytes { get; init; }
    public long? TerrainCellDataUploadGeneration { get; init; }
    public bool? TerrainCellDataUploadAvailable { get; init; }
    public int? TerrainCellDataUploadSourceFrameId { get; init; }
    public bool? TerrainCellDataUploadHasSourceFrame { get; init; }
    public int? TerrainCellDataUploadObservationFrameId { get; init; }
}

public sealed class FrameCounterSnapshot
{
    public long? TerrainUploadCalls { get; init; }
    public long? TerrainUploadBytes { get; init; }
    public long? TerrainAtlasUploadCalls { get; init; }
    public long? TerrainAtlasUploadBytes { get; init; }
    public long? LightingFieldRebuilds { get; init; }
    public long? LightingStaticSolves { get; init; }
    public long? TerrainCellDataApplyCalls { get; init; }
    public long? TerrainCellDataApplyPayloadBytes { get; init; }
    public long? TerrainCellDataCopyTextureCalls { get; init; }
    public long? TerrainCellDataCopyTexturePayloadBytes { get; init; }
    public bool? TerrainCellDataUploadFrameDeltaValid { get; init; }
    public int? TerrainCellDataUploadDeltaStartObservationFrameId { get; init; }
    public int? TerrainCellDataUploadDeltaEndObservationFrameId { get; init; }
}

public sealed class TerrainTextureUploadObservation
{
    public bool? Available { get; init; }
    public long? Generation { get; init; }
    public bool? HasSourceFrame { get; init; }
    public int? SourceFrameId { get; init; }
    public int? ObservationFrameId { get; init; }
}

public sealed record MetricStatistics(int SampleCount, double P50, double P95, double P99, double Max);

public sealed record MetricComparison(MetricStatistics Before, MetricStatistics After);

public sealed record ValidationReport(
    CheckStatus InputStatus,
    CheckStatus InvariantStatus,
    CheckStatus PerformanceStatus,
    CheckStatus CoverageStatus,
    IReadOnlyList<string> Findings,
    IReadOnlyDictionary<string, MetricStatistics> Statistics)
{
    public string RuleScope { get; } = "OPT-1 counter rules: S0/S1/S4; optional cell-data Apply/CopyTexture gate runs only when telemetry endpoints exist; other OPT rules and GRID-32 unimplemented";
}

public sealed record ComparisonBudget(
    double MaxCpuP95RegressionMs,
    double MaxCpuP99RegressionMs,
    double MaxCpuMaxRegressionMs,
    double MaxGpuP95RegressionMs,
    double MaxGpuP99RegressionMs,
    double MaxGpuMaxRegressionMs);

public sealed record ComparisonReport(
    CheckStatus CompatibilityStatus,
    CheckStatus InvariantStatus,
    CheckStatus PerformanceStatus,
    CheckStatus CoverageStatus,
    CheckStatus ArtifactIdentityStatus,
    string? BeforeArtifactIdentity,
    string? AfterArtifactIdentity,
    IReadOnlyList<string> Findings,
    IReadOnlyDictionary<string, MetricComparison> Statistics)
{
    public string RuleScope { get; } = "OPT-1 counter rules: S0/S1/S4; optional cell-data Apply/CopyTexture gate runs only when telemetry endpoints exist; other OPT rules and GRID-32 unimplemented";
}

public static class CaptureJson
{
    private static readonly JsonSerializerOptions _Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static FrameCapture Read(string path)
    {
        return Parse(File.ReadAllText(path));
    }

    public static FrameCapture Parse(string json)
    {
        return JsonSerializer.Deserialize<FrameCapture>(json, _Options)
            ?? throw new InvalidDataException("Capture JSON is empty or null.");
    }

    public static string Write(FrameCapture capture) =>
        JsonSerializer.Serialize(capture, _Options);

    public static string WriteReport<T>(T report) => JsonSerializer.Serialize(report, _Options);
}
