using System.Globalization;

namespace Kern.FrameHarness;

/// <summary>Offline evidence analysis only; never executes or models the renderer.</summary>
public static class CaptureAnalyzer
{
    public const int CurrentSchemaVersion = 1;
    public const string CurrentHarnessVersion = "1";

    private static readonly string[] _RequiredCpuMetrics =
    [
        "terrainMesh", "terrainCache", "terrainFloodFill", "terrainGpuUpload",
        "terrainAtlasUpload", "lightingBuildCommands", "lightingExecuteCommands",
        "lightingCascadeTrace", "lightingCascadeMerge", "lightingDynamic", "lightingComposite",
    ];

    public static ValidationReport Validate(FrameCapture capture)
    {
        List<string> findings = [];
        bool invalid = false;
        bool missing = false;
        if (capture.Manifest is not { Runtime: not null, Build: not null } manifest ||
            capture.Frames is null || manifest.UnavailableMetrics is null)
        {
            return InvalidStructure("Non-null manifest, runtime, build, unavailableMetrics and frames are required.");
        }

        if (capture.SchemaVersion != CurrentSchemaVersion || capture.HarnessVersion != CurrentHarnessVersion)
        {
            findings.Add("Unsupported schema or harness version.");
            invalid = true;
        }

        if (string.IsNullOrWhiteSpace(capture.CaptureId) ||
            !DateTimeOffset.TryParse(capture.CapturedAtUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTimeOffset timestamp) || timestamp.Offset != TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(manifest.ScenarioId) || string.IsNullOrWhiteSpace(manifest.CapturePhase) ||
            string.IsNullOrWhiteSpace(manifest.Runtime.UnityVersion) ||
            string.IsNullOrWhiteSpace(manifest.Runtime.Platform) ||
            string.IsNullOrWhiteSpace(manifest.Runtime.GraphicsApi) ||
            string.IsNullOrWhiteSpace(manifest.Runtime.GraphicsDevice) ||
            manifest.Runtime.Width is null or <= 0 || manifest.Runtime.Height is null or <= 0 ||
            capture.Frames.Count == 0)
        {
            findings.Add("Malformed capture identity, runtime identity, UTC timestamp or empty samples.");
            invalid = true;
        }

        if (string.IsNullOrWhiteSpace(manifest.Build.LoadedArtifactId) ||
            manifest.Build.ArtifactScope != "production-content" ||
            string.IsNullOrWhiteSpace(manifest.WorkloadHash) ||
            string.IsNullOrWhiteSpace(manifest.Runtime.QualityProfile))
        {
            findings.Add("Loaded artifact, deterministic workload or effective quality identity is missing.");
            missing = true;
        }

        if (manifest.VisualCoverage is not true || string.IsNullOrWhiteSpace(manifest.VisualEvidence))
        {
            findings.Add("Independent production visual evidence is missing.");
            missing = true;
        }

        bool baselineCorrelated = BaselineCorrelated(capture);
        if (capture.CounterBaseline is null || capture.CounterBaselineGeneration is null)
        {
            findings.Add("Cumulative baseline or its generation is missing.");
            missing = true;
        }
        if (!baselineCorrelated)
        {
            findings.Add("Cumulative baseline producer stamp is absent, invalid or stale for its observation.");
            missing = true;
        }

        if (capture.CounterBaselineGeneration is < 0 ||
            Values(capture.CounterBaseline).Any(value => value is < 0) || !ValidInputs(capture.InputBaseline))
        {
            findings.Add("Invalid counter or input baseline.");
            invalid = true;
        }

        long previousFrameId = -1;
        CounterSnapshot? previousCounters = baselineCorrelated ? capture.CounterBaseline : null;
        int? previousGeneration = baselineCorrelated ? capture.CounterBaselineGeneration : null;
        bool previousCorrelated = baselineCorrelated;
        foreach (FrameSample? frame in capture.Frames)
        {
            if (frame is null)
            {
                findings.Add("Null frame entry.");
                invalid = true;
                continue;
            }

            if (frame.FrameId < 0 || frame.FrameId <= previousFrameId ||
                (previousFrameId >= 0 && frame.FrameId != previousFrameId + 1))
            {
                findings.Add($"Frame {frame.FrameId}: negative, duplicate, out-of-order or skipped frame.");
                invalid = true;
            }

            previousFrameId = frame.FrameId;
            bool correlated = Correlated(frame);
            if (!correlated)
            {
                findings.Add($"Frame {frame.FrameId}: telemetry producer stamp is absent, invalid or stale for this observation.");
                missing = true;
            }
            if (frame.Class is null)
            {
                findings.Add($"Frame {frame.FrameId}: sample class is unobserved.");
                missing = true;
            }
            else if (!Enum.IsDefined(frame.Class.Value))
            {
                findings.Add($"Frame {frame.FrameId}: invalid sample class.");
                invalid = true;
            }

            bool missingCorrelatedCpu = correlated && (frame.CpuMs is null ||
                _RequiredCpuMetrics.Any(key => frame.CpuMs is null ||
                    !frame.CpuMs.TryGetValue(key, out double? value) || value is null));
            if (frame.FrameDurationMs is null || frame.GpuFrameMs is null || missingCorrelatedCpu)
            {
                findings.Add($"Frame {frame.FrameId}: frame/GPU or correlated CPU timings are incomplete.");
                missing = true;
            }

            if (frame.FrameDurationMs is <= 0 || !ValidMetric(frame.FrameDurationMs) ||
                !ValidMetric(frame.GpuFrameMs) ||
                (frame.CpuMs is not null && frame.CpuMs.Values.Any(value => !ValidMetric(value))))
            {
                findings.Add($"Frame {frame.FrameId}: non-finite or invalid timing.");
                invalid = true;
            }

            long?[] counters = Values(frame.Cumulative);
            if (correlated && (counters.Any(value => value is null) || Values(frame.FrameCounters).Any(value => value is null)))
            {
                findings.Add($"Frame {frame.FrameId}: work counters are incomplete.");
                missing = true;
            }

            if (frame.CounterGeneration < 0 ||
                counters.Any(value => value is < 0) || Values(frame.FrameCounters).Any(value => value is < 0) ||
                !ValidInputs(frame.Inputs))
            {
                findings.Add($"Frame {frame.FrameId}: invalid counter or input observation.");
                invalid = true;
            }

            if (correlated && previousCorrelated && previousGeneration is int generation)
            {
                long increment = (long)frame.CounterGeneration - generation;
                bool reset = increment == 1 && frame.CounterResetObserved;
                if (increment < 0 || increment > 1 || frame.CounterResetObserved != (increment == 1) ||
                    (!reset && Values(previousCounters).Zip(counters).Any(pair =>
                        pair.First is long oldValue && pair.Second is long newValue && newValue < oldValue)))
                {
                    findings.Add($"Frame {frame.FrameId}: counter decrease/reset/generation disagreement.");
                    invalid = true;
                }
            }

            previousCounters = correlated ? frame.Cumulative : null;
            previousGeneration = correlated ? frame.CounterGeneration : null;
            previousCorrelated = correlated;
        }

        if (!ValidateTerrainCellDataTelemetry(capture, findings))
        {
            invalid = true;
        }

        if (invalid)
        {
            CheckStatus invalidInvariant = HasKnownPositiveCellDataViolation(capture) ? CheckStatus.Fail : CheckStatus.Incomplete;
            return new ValidationReport(CheckStatus.Fail, invalidInvariant, CheckStatus.Incomplete,
                CheckStatus.Incomplete, findings, new Dictionary<string, MetricStatistics>());
        }

        CheckStatus invariant = CheckInvariant(capture, findings);
        if (capture.Frames.Any(frame => frame!.Class != SampleClass.Steady))
        {
            findings.Add("OPT-1 rules cover steady samples only; cold/reanchor/unclassified invariants remain unimplemented.");
            missing = true;
        }

        // A single capture has no performance verdict: a compatible baseline and explicit budgets are required.
        return new ValidationReport(CheckStatus.Pass, invariant, CheckStatus.Incomplete,
            missing || invariant == CheckStatus.Incomplete ? CheckStatus.Incomplete : CheckStatus.Pass,
            findings, BuildStatistics(capture.Frames.OfType<FrameSample>().ToArray()));
    }

    public static ComparisonReport Compare(FrameCapture before, FrameCapture after, ComparisonBudget budget)
    {
        List<string> findings = [];
        Dictionary<string, MetricComparison> stats = [];
        ValidationReport oldReport = Validate(before);
        ValidationReport newReport = Validate(after);
        CheckStatus invariant = Combine(oldReport.InvariantStatus, newReport.InvariantStatus);
        CheckStatus coverage = Combine(oldReport.CoverageStatus, newReport.CoverageStatus);
        string? oldArtifact = before.Manifest?.Build?.LoadedArtifactId;
        string? newArtifact = after.Manifest?.Build?.LoadedArtifactId;
        CheckStatus identity = string.IsNullOrWhiteSpace(oldArtifact) || string.IsNullOrWhiteSpace(newArtifact) ||
            before.Manifest?.Build?.ArtifactScope != "production-content" ||
            after.Manifest?.Build?.ArtifactScope != "production-content"
            ? CheckStatus.Incomplete : CheckStatus.Pass;
        findings.AddRange(oldReport.Findings.Select(finding => $"before: {finding}"));
        findings.AddRange(newReport.Findings.Select(finding => $"after: {finding}"));

        ComparisonReport Report(CheckStatus compatibility, CheckStatus performance) =>
            new(compatibility, invariant, performance, coverage, identity,
                oldArtifact, newArtifact, findings, stats);

        double[] budgets =
        [
            budget.MaxCpuP95RegressionMs, budget.MaxCpuP99RegressionMs, budget.MaxCpuMaxRegressionMs,
            budget.MaxGpuP95RegressionMs, budget.MaxGpuP99RegressionMs, budget.MaxGpuMaxRegressionMs,
        ];
        if (budgets.Any(value => !FiniteNonNegative(value)))
        {
            findings.Add("All CPU/frame and GPU budgets must be explicitly finite and non-negative.");
            return Report(CheckStatus.Fail, CheckStatus.Incomplete);
        }

        if (oldReport.InputStatus != CheckStatus.Pass || newReport.InputStatus != CheckStatus.Pass)
        {
            return Report(CheckStatus.Fail, CheckStatus.Incomplete);
        }

        if (before.CaptureId == after.CaptureId)
        {
            findings.Add("Before and after must be distinct captures.");
            return Report(CheckStatus.Fail, CheckStatus.Incomplete);
        }

        CaptureManifest left = before.Manifest!;
        CaptureManifest right = after.Manifest!;
        if (string.IsNullOrWhiteSpace(left.WorkloadHash) || string.IsNullOrWhiteSpace(right.WorkloadHash) ||
            string.IsNullOrWhiteSpace(left.Runtime!.QualityProfile) ||
            string.IsNullOrWhiteSpace(right.Runtime!.QualityProfile))
        {
            findings.Add("Compatibility cannot be established without workload and effective quality identities.");
            return Report(CheckStatus.Incomplete, CheckStatus.Incomplete);
        }

        if (!Compatible(left, right))
        {
            findings.Add("Scenario, workload, phase or runtime compatibility identities differ.");
            return Report(CheckStatus.Fail, CheckStatus.Incomplete);
        }

        FrameSample[] oldFrames = before.Frames!.OfType<FrameSample>().ToArray();
        FrameSample[] newFrames = after.Frames!.OfType<FrameSample>().ToArray();
        if (oldFrames.Any(frame => frame.Class is null) || newFrames.Any(frame => frame.Class is null) ||
            Enum.GetValues<SampleClass>().Any(kind =>
                oldFrames.Count(frame => frame.Class == kind) != newFrames.Count(frame => frame.Class == kind)))
        {
            findings.Add("Sample classes are missing or per-class sample counts differ.");
            return Report(CheckStatus.Incomplete, CheckStatus.Incomplete);
        }

        bool missing = identity != CheckStatus.Pass || !BaselineCorrelated(before) || !BaselineCorrelated(after) ||
            oldFrames.Concat(newFrames).Any(frame => frame.ProducerLifecycleValid is not true ||
                frame.ProducerFrameId is null || frame.ProducerFrameId != frame.FrameId);
        bool regression = false;
        foreach (SampleClass kind in Enum.GetValues<SampleClass>())
        {
            FrameSample[] oldGroup = oldFrames.Where(frame => frame.Class == kind).ToArray();
            FrameSample[] newGroup = newFrames.Where(frame => frame.Class == kind).ToArray();
            if (oldGroup.Length == 0)
            {
                continue;
            }

            foreach (string metric in MetricNames(oldGroup.Concat(newGroup)))
            {
                // Metric returns null for uncorrelated CPU samples. Keep those slots:
                // dropping them would compare incomplete, potentially unequal distributions.
                double?[] oldValues = oldGroup.Select(frame => Metric(frame, metric)).ToArray();
                double?[] newValues = newGroup.Select(frame => Metric(frame, metric)).ToArray();
                string name = $"{kind.ToString().ToLowerInvariant()}.{metric}";
                if (oldValues.Length == 0 || newValues.Length == 0 ||
                    oldValues.Any(value => value is null) || newValues.Any(value => value is null))
                {
                    findings.Add($"{name}: incomplete samples; no sparse comparison.");
                    missing = true;
                    continue;
                }

                MetricStatistics oldStats = Statistics(oldValues.Select(value => value!.Value));
                MetricStatistics newStats = Statistics(newValues.Select(value => value!.Value));
                stats[name] = new MetricComparison(oldStats, newStats);
                bool gpu = metric == "gpuFrameMs";
                if (newStats.P95 - oldStats.P95 > (gpu ? budget.MaxGpuP95RegressionMs : budget.MaxCpuP95RegressionMs) ||
                    newStats.P99 - oldStats.P99 > (gpu ? budget.MaxGpuP99RegressionMs : budget.MaxCpuP99RegressionMs) ||
                    newStats.Max - oldStats.Max > (gpu ? budget.MaxGpuMaxRegressionMs : budget.MaxCpuMaxRegressionMs))
                {
                    findings.Add($"{name}: regression exceeds explicit p95/p99/max budget.");
                    regression = true;
                }
            }
        }

        // Performance and correctness are independent. The CLI combines all statuses for its exit code.
        return Report(CheckStatus.Pass, regression ? CheckStatus.Fail : missing ? CheckStatus.Incomplete : CheckStatus.Pass);
    }

    private static CheckStatus CheckInvariant(FrameCapture capture, List<string> findings)
    {
        string scenario = capture.Manifest!.ScenarioId;
        if (scenario is not ("S0" or "S1" or "S4") ||
            string.IsNullOrWhiteSpace(capture.Manifest.ObservationEvidence))
        {
            findings.Add("No implemented scenario rule or source-observation provenance; OPT-1 is incomplete.");
            return CheckStatus.Incomplete;
        }

        FrameInputObservation? previousInputs = capture.InputBaseline;
        bool previousProducerCorrelated = BaselineCorrelated(capture);
        CounterSnapshot? previousCounters = previousProducerCorrelated ? capture.CounterBaseline : null;
        TerrainTextureUploadObservation? previousUploadObservation = previousProducerCorrelated
            ? UploadObservation(capture.CounterBaseline)
            : null;
        int? generation = previousProducerCorrelated ? capture.CounterBaselineGeneration : null;
        bool missing = false;
        bool failed = false;
        bool observedMotion = false;
        int checkedFrames = 0;
        foreach (FrameSample frame in capture.Frames!.OfType<FrameSample>())
        {
            bool producerCorrelated = Correlated(frame);
            missing |= !producerCorrelated;
            missing |= frame.Class is null;
            bool sameGeneration = producerCorrelated && previousProducerCorrelated &&
                generation == frame.CounterGeneration && !frame.CounterResetObserved;
            if (frame.Class == SampleClass.Steady)
            {
                if (!StableInputs(previousInputs, frame.Inputs, scenario))
                {
                    findings.Add($"Frame {frame.FrameId}: unchanged-input/readiness preconditions are unproven.");
                    missing = true;
                }
                else
                {
                    checkedFrames++;
                    bool dynamicMoved = previousInputs!.DynamicLightsRevision != frame.Inputs!.DynamicLightsRevision;
                    observedMotion |= scenario == "S1"
                        ? previousInputs!.CameraX != frame.Inputs!.CameraX || previousInputs.CameraY != frame.Inputs.CameraY
                        : dynamicMoved;
                    if (producerCorrelated)
                    {
                        long?[] oldValues = Values(previousCounters);
                        long?[] newValues = Values(frame.Cumulative);
                        // Dynamic solve/trace are allowed; all terrain work and atlas scroll are not.
                        foreach (int index in new[] { 0, 1, 2, 3, 6 })
                        {
                            if (!sameGeneration || oldValues[index] is not long oldValue || newValues[index] is not long newValue)
                            {
                                missing = true;
                            }
                            else if (newValue > oldValue)
                            {
                                findings.Add($"OPT-1 frame {frame.FrameId}: forbidden cumulative work counter {index} increased.");
                                failed = true;
                            }
                        }

                        foreach (long? value in Values(frame.FrameCounters))
                        {
                            missing |= value is null;
                            if (value is > 0)
                            {
                                findings.Add($"OPT-1 frame {frame.FrameId}: terrain upload or lighting field/static work occurred.");
                                failed = true;
                            }
                        }

                        if (scenario == "S4" && dynamicMoved)
                        {
                            foreach (int index in new[] { 4, 5 })
                            {
                                if (!sameGeneration || oldValues[index] is not long oldValue || newValues[index] is not long newValue)
                                {
                                    missing = true;
                                }
                                else if (newValue == oldValue)
                                {
                                    findings.Add($"OPT-1 frame {frame.FrameId}: changed dynamic source was not solved/traced.");
                                    failed = true;
                                }
                            }
                        }

                        UploadDeltaState uploadState = TerrainCellDataDelta(
                            previousCounters, previousUploadObservation, frame.Cumulative,
                            frame.TerrainCellDataUpload, previousProducerCorrelated,
                            producerCorrelated, sameGeneration);
                        if (uploadState == UploadDeltaState.Unavailable)
                        {
                            missing = true;
                        }
                        else if (uploadState == UploadDeltaState.Positive)
                        {
                            findings.Add($"OPT-1 frame {frame.FrameId}: terrain cell-data Apply or CopyTexture work occurred.");
                            failed = true;
                        }
                    }
                }
            }

            // Every frame advances the baseline, including cold and reanchor frames.
            previousInputs = frame.Inputs;
            previousProducerCorrelated = producerCorrelated;
            previousCounters = producerCorrelated ? frame.Cumulative : null;
            previousUploadObservation = producerCorrelated ? frame.TerrainCellDataUpload : null;
            generation = producerCorrelated ? frame.CounterGeneration : null;
        }

        if (checkedFrames == 0 || (scenario is "S1" or "S4" && !observedMotion))
        {
            findings.Add("No eligible steady samples or required camera/dynamic motion was not observed.");
            missing = true;
        }

        if (missing)
        {
            findings.Add("OPT-1 evidence is incomplete; absent counters/observations are never zero.");
        }

        return failed ? CheckStatus.Fail : missing ? CheckStatus.Incomplete : CheckStatus.Pass;
    }

    private static bool Correlated(FrameSample frame) =>
        frame.ProducerLifecycleValid is true && frame.ProducerFrameId is long producerFrameId &&
        producerFrameId == frame.FrameId;

    private static bool BaselineCorrelated(FrameCapture capture)
    {
        FrameSample? firstFrame = capture.Frames?.OfType<FrameSample>().FirstOrDefault();
        return capture.BaselineProducerLifecycleValid is true &&
            capture.BaselineObservationFrameId is long observationFrameId &&
            capture.BaselineProducerFrameId is long producerFrameId &&
            observationFrameId == producerFrameId && firstFrame is not null &&
            observationFrameId == firstFrame.FrameId - 1;
    }

    private static bool StableInputs(FrameInputObservation? oldInput, FrameInputObservation? input, string scenario)
    {
        if (oldInput is null || input is null ||
            oldInput.TerrainReady is not true || input.TerrainReady is not true ||
            oldInput.LightingReady is not true || input.LightingReady is not true ||
            oldInput.WorkQueuesDrained is not true || input.WorkQueuesDrained is not true ||
            oldInput.CameraX is null || oldInput.CameraY is null || input.CameraX is null || input.CameraY is null ||
            oldInput.DynamicLightsRevision is null || input.DynamicLightsRevision is null ||
            !Same(oldInput.WorldGeneration, input.WorldGeneration) ||
            !Same(oldInput.TerrainGeometryRevision, input.TerrainGeometryRevision) ||
            !Same(oldInput.TerrainWindowRevision, input.TerrainWindowRevision) ||
            !Same(oldInput.LightingRegionRevision, input.LightingRegionRevision) ||
            !Same(oldInput.SettingsRevision, input.SettingsRevision) ||
            !Same(oldInput.ResourceGeneration, input.ResourceGeneration) ||
            !Same(oldInput.ContributorRevision, input.ContributorRevision))
        {
            return false;
        }

        return (scenario == "S1"
                ? oldInput.CameraWithinPreparedWindow is true && input.CameraWithinPreparedWindow is true
                : oldInput.CameraX == input.CameraX && oldInput.CameraY == input.CameraY) &&
            (scenario == "S4"
                ? input.DynamicLightsRevision >= oldInput.DynamicLightsRevision
                : Same(oldInput.DynamicLightsRevision, input.DynamicLightsRevision));
    }

    private static bool Same(long? left, long? right) => left is not null && left == right;

    private enum UploadDeltaState
    {
        NotPresent,
        Unavailable,
        Zero,
        Positive,
    }

    private readonly record struct UploadEndpoint(
        long Generation,
        long ApplyCalls,
        long ApplyPayloadBytes,
        long CopyTextureCalls,
        long CopyTexturePayloadBytes,
        int ObservationFrameId);

    private static UploadDeltaState TerrainCellDataDelta(
        CounterSnapshot? previousCounters,
        TerrainTextureUploadObservation? previousObservation,
        CounterSnapshot? currentCounters,
        TerrainTextureUploadObservation? currentObservation,
        bool previousProducerCorrelated,
        bool currentProducerCorrelated,
        bool sameHarnessGeneration)
    {
        bool previousPresent = HasUploadEndpointData(previousCounters, previousObservation);
        bool currentPresent = HasUploadEndpointData(currentCounters, currentObservation);
        if (!previousPresent && !currentPresent)
        {
            return UploadDeltaState.NotPresent;
        }

        if (!previousProducerCorrelated || !currentProducerCorrelated || !sameHarnessGeneration ||
            !TryGetUploadEndpoint(previousCounters, previousObservation, out UploadEndpoint previous) ||
            !TryGetUploadEndpoint(currentCounters, currentObservation, out UploadEndpoint current) ||
            current.Generation != previous.Generation ||
            previous.ObservationFrameId == int.MaxValue ||
            current.ObservationFrameId != previous.ObservationFrameId + 1 ||
            current.ApplyCalls < previous.ApplyCalls ||
            current.ApplyPayloadBytes < previous.ApplyPayloadBytes ||
            current.CopyTextureCalls < previous.CopyTextureCalls ||
            current.CopyTexturePayloadBytes < previous.CopyTexturePayloadBytes)
        {
            return UploadDeltaState.Unavailable;
        }

        if (!TryDeriveUploadDelta(previous, current, out long[] delta))
        {
            return UploadDeltaState.Unavailable;
        }

        return delta.Any(value => value > 0) ? UploadDeltaState.Positive : UploadDeltaState.Zero;
    }

    private static bool ValidateTerrainCellDataTelemetry(FrameCapture capture, List<string> findings)
    {
        bool valid = true;
        CounterSnapshot? previousCounters = capture.CounterBaseline;
        TerrainTextureUploadObservation? previousObservation = UploadObservation(previousCounters);
        if (HasUploadEndpointData(previousCounters, previousObservation) &&
            (!WellFormedUploadEndpoint(previousCounters, previousObservation) ||
             previousCounters?.TerrainCellDataUploadObservationFrameId != capture.BaselineObservationFrameId))
        {
            findings.Add("Malformed or inconsistent terrain cell-data upload baseline endpoint.");
            valid = false;
        }

        foreach (FrameSample? frame in capture.Frames!.OfType<FrameSample>())
        {
            if (frame is null)
            {
                continue;
            }

            if (HasUploadEndpointData(frame.Cumulative, frame.TerrainCellDataUpload) &&
                !WellFormedUploadEndpoint(frame.Cumulative, frame.TerrainCellDataUpload))
            {
                findings.Add($"Frame {frame.FrameId}: malformed or inconsistent terrain cell-data upload endpoint.");
                valid = false;
            }

            FrameCounterSnapshot? delta = frame.FrameCounters;
            bool hasDeltaFields = delta?.TerrainCellDataApplyCalls is not null ||
                delta?.TerrainCellDataApplyPayloadBytes is not null ||
                delta?.TerrainCellDataCopyTextureCalls is not null ||
                delta?.TerrainCellDataCopyTexturePayloadBytes is not null ||
                delta?.TerrainCellDataUploadFrameDeltaValid is not null ||
                delta?.TerrainCellDataUploadDeltaStartObservationFrameId is not null ||
                delta?.TerrainCellDataUploadDeltaEndObservationFrameId is not null;
            bool telemetryPresent = HasUploadEndpointData(frame.Cumulative, frame.TerrainCellDataUpload) ||
                HasUploadEndpointData(previousCounters, previousObservation) || hasDeltaFields;
            if (telemetryPresent && delta?.TerrainCellDataUploadFrameDeltaValid is null)
            {
                findings.Add($"Frame {frame.FrameId}: terrain cell-data upload delta validity is missing.");
                valid = false;
            }

            if (delta is not null && new long?[]
                {
                    delta.TerrainCellDataApplyCalls, delta.TerrainCellDataApplyPayloadBytes,
                    delta.TerrainCellDataCopyTextureCalls, delta.TerrainCellDataCopyTexturePayloadBytes,
                }.Any(value => value is < 0))
            {
                findings.Add($"Frame {frame.FrameId}: negative terrain cell-data upload delta.");
                valid = false;
            }

            bool hasPreviousEndpoint = TryGetUploadEndpoint(previousCounters, previousObservation, out UploadEndpoint previous);
            bool hasCurrentEndpoint = TryGetUploadEndpoint(frame.Cumulative, frame.TerrainCellDataUpload, out UploadEndpoint current);
            bool adjacentSameGeneration = hasPreviousEndpoint && hasCurrentEndpoint &&
                previous.Generation == current.Generation && previous.ObservationFrameId != int.MaxValue &&
                current.ObservationFrameId == previous.ObservationFrameId + 1;
            if (adjacentSameGeneration && (current.ApplyCalls < previous.ApplyCalls ||
                current.ApplyPayloadBytes < previous.ApplyPayloadBytes || current.CopyTextureCalls < previous.CopyTextureCalls ||
                current.CopyTexturePayloadBytes < previous.CopyTexturePayloadBytes))
            {
                findings.Add($"Frame {frame.FrameId}: terrain cell-data cumulative counters regressed within a generation.");
                valid = false;
            }

            if (adjacentSameGeneration && TryDeriveUploadDelta(previous, current, out long[] expected))
            {
                bool exportedValid = delta?.TerrainCellDataUploadFrameDeltaValid is true;
                bool exportedMatches = delta?.TerrainCellDataApplyCalls == expected[0] &&
                    delta.TerrainCellDataApplyPayloadBytes == expected[1] &&
                    delta.TerrainCellDataCopyTextureCalls == expected[2] &&
                    delta.TerrainCellDataCopyTexturePayloadBytes == expected[3] &&
                    delta.TerrainCellDataUploadDeltaStartObservationFrameId == previous.ObservationFrameId &&
                    delta.TerrainCellDataUploadDeltaEndObservationFrameId == current.ObservationFrameId;
                if (!exportedValid || !exportedMatches)
                {
                    findings.Add($"Frame {frame.FrameId}: exported terrain cell-data delta disagrees with adjacent cumulative endpoints.");
                    valid = false;
                }
            }
            else if (delta?.TerrainCellDataUploadFrameDeltaValid is true)
            {
                findings.Add($"Frame {frame.FrameId}: terrain cell-data delta is marked valid without matching adjacent endpoints.");
                valid = false;
            }
            else if (delta?.TerrainCellDataUploadFrameDeltaValid is false &&
                     (delta.TerrainCellDataApplyCalls is not null || delta.TerrainCellDataApplyPayloadBytes is not null ||
                      delta.TerrainCellDataCopyTextureCalls is not null || delta.TerrainCellDataCopyTexturePayloadBytes is not null ||
                      delta.TerrainCellDataUploadDeltaStartObservationFrameId is not null ||
                      delta.TerrainCellDataUploadDeltaEndObservationFrameId is not null))
            {
                findings.Add($"Frame {frame.FrameId}: invalid terrain cell-data delta carries values.");
                valid = false;
            }

            previousCounters = frame.Cumulative;
            previousObservation = frame.TerrainCellDataUpload;
        }

        return valid;
    }

    private static bool HasKnownPositiveCellDataViolation(FrameCapture capture)
    {
        string? scenario = capture.Manifest?.ScenarioId;
        if (scenario is not ("S0" or "S1" or "S4"))
        {
            return false;
        }

        FrameInputObservation? previousInputs = capture.InputBaseline;
        bool previousCorrelated = BaselineCorrelated(capture);
        CounterSnapshot? previousCounters = previousCorrelated ? capture.CounterBaseline : null;
        TerrainTextureUploadObservation? previousObservation = previousCorrelated
            ? UploadObservation(capture.CounterBaseline)
            : null;
        long? previousExpectedObservationFrame = capture.BaselineObservationFrameId;
        int? previousGeneration = previousCorrelated ? capture.CounterBaselineGeneration : null;
        foreach (FrameSample? frame in capture.Frames!.OfType<FrameSample>())
        {
            if (frame is null)
            {
                continue;
            }

            bool correlated = Correlated(frame);
            bool sameGeneration = correlated && previousCorrelated &&
                previousGeneration == frame.CounterGeneration && !frame.CounterResetObserved;
            bool uploadObservationMapped =
                (!HasUploadEndpointData(previousCounters, previousObservation) ||
                 previousCounters?.TerrainCellDataUploadObservationFrameId == previousExpectedObservationFrame) &&
                (!HasUploadEndpointData(frame.Cumulative, frame.TerrainCellDataUpload) ||
                 frame.Cumulative?.TerrainCellDataUploadObservationFrameId == frame.FrameId);
            if (frame.Class == SampleClass.Steady && correlated &&
                uploadObservationMapped && StableInputs(previousInputs, frame.Inputs, scenario) &&
                TerrainCellDataDelta(previousCounters, previousObservation, frame.Cumulative,
                    frame.TerrainCellDataUpload, previousCorrelated, correlated, sameGeneration) == UploadDeltaState.Positive)
            {
                return true;
            }

            previousInputs = frame.Inputs;
            previousCorrelated = correlated;
            previousCounters = correlated ? frame.Cumulative : null;
            previousObservation = correlated ? frame.TerrainCellDataUpload : null;
            previousExpectedObservationFrame = frame.FrameId;
            previousGeneration = correlated ? frame.CounterGeneration : null;
        }

        return false;
    }

    private static bool TryGetUploadEndpoint(
        CounterSnapshot? counters,
        TerrainTextureUploadObservation? observation,
        out UploadEndpoint endpoint)
    {
        endpoint = default;
        if (counters is null || observation is null ||
            counters.TerrainCellDataUploadAvailable is not true || observation.Available is not true ||
            counters.TerrainCellDataUploadGeneration is not long generation || generation < 0 ||
            observation.Generation != generation ||
            counters.TerrainCellDataUploadHasSourceFrame is not bool hasSourceFrame ||
            observation.HasSourceFrame != hasSourceFrame ||
            counters.TerrainCellDataUploadObservationFrameId is not int observationFrameId || observationFrameId < 0 ||
            observation.ObservationFrameId != observationFrameId ||
            counters.TerrainCellDataApplyCalls is not long applyCalls || applyCalls < 0 ||
            counters.TerrainCellDataApplyPayloadBytes is not long applyBytes || applyBytes < 0 ||
            counters.TerrainCellDataCopyTextureCalls is not long copyCalls || copyCalls < 0 ||
            counters.TerrainCellDataCopyTexturePayloadBytes is not long copyBytes || copyBytes < 0 ||
            (hasSourceFrame && (counters.TerrainCellDataUploadSourceFrameId is not int sourceFrameId ||
                                sourceFrameId < 0 || sourceFrameId > observationFrameId)) ||
            (!hasSourceFrame && counters.TerrainCellDataUploadSourceFrameId is not null) ||
            observation.SourceFrameId != counters.TerrainCellDataUploadSourceFrameId ||
            (applyCalls == 0) != (applyBytes == 0) || (copyCalls == 0) != (copyBytes == 0))
        {
            return false;
        }

        endpoint = new UploadEndpoint(generation, applyCalls, applyBytes, copyCalls, copyBytes, observationFrameId);
        return true;
    }

    private static bool WellFormedUploadEndpoint(
        CounterSnapshot? counters,
        TerrainTextureUploadObservation? observation)
    {
        if (counters is null || observation is null ||
            counters.TerrainCellDataUploadAvailable is not bool available ||
            observation.Available != available ||
            counters.TerrainCellDataUploadGeneration is not long generation || generation < 0 ||
            observation.Generation != generation ||
            counters.TerrainCellDataUploadHasSourceFrame is not bool hasSourceFrame ||
            observation.HasSourceFrame != hasSourceFrame ||
            counters.TerrainCellDataUploadObservationFrameId is not int observationFrameId || observationFrameId < 0 ||
            observation.ObservationFrameId != observationFrameId ||
            (hasSourceFrame && (counters.TerrainCellDataUploadSourceFrameId is not int sourceFrameId ||
                                sourceFrameId < 0 || sourceFrameId > observationFrameId)) ||
            (!hasSourceFrame && counters.TerrainCellDataUploadSourceFrameId is not null) ||
            observation.SourceFrameId != counters.TerrainCellDataUploadSourceFrameId)
        {
            return false;
        }

        long?[] values =
        [
            counters.TerrainCellDataApplyCalls, counters.TerrainCellDataApplyPayloadBytes,
            counters.TerrainCellDataCopyTextureCalls, counters.TerrainCellDataCopyTexturePayloadBytes,
        ];
        if (available)
        {
            return TryGetUploadEndpoint(counters, observation, out _);
        }

        return values.All(value => value is null);
    }

    private static bool TryDeriveUploadDelta(UploadEndpoint previous, UploadEndpoint current, out long[] delta)
    {
        delta = [];
        if (previous.Generation != current.Generation || previous.ObservationFrameId == int.MaxValue ||
            current.ObservationFrameId != previous.ObservationFrameId + 1 ||
            current.ApplyCalls < previous.ApplyCalls || current.ApplyPayloadBytes < previous.ApplyPayloadBytes ||
            current.CopyTextureCalls < previous.CopyTextureCalls || current.CopyTexturePayloadBytes < previous.CopyTexturePayloadBytes)
        {
            return false;
        }

        delta =
        [
            current.ApplyCalls - previous.ApplyCalls,
            current.ApplyPayloadBytes - previous.ApplyPayloadBytes,
            current.CopyTextureCalls - previous.CopyTextureCalls,
            current.CopyTexturePayloadBytes - previous.CopyTexturePayloadBytes,
        ];
        return (delta[0] == 0) == (delta[1] == 0) && (delta[2] == 0) == (delta[3] == 0);
    }

    private static bool HasUploadEndpointData(CounterSnapshot? counters, TerrainTextureUploadObservation? observation) =>
        counters?.TerrainCellDataApplyCalls is not null || counters?.TerrainCellDataApplyPayloadBytes is not null ||
        counters?.TerrainCellDataCopyTextureCalls is not null || counters?.TerrainCellDataCopyTexturePayloadBytes is not null ||
        counters?.TerrainCellDataUploadGeneration is not null || counters?.TerrainCellDataUploadAvailable is not null ||
        counters?.TerrainCellDataUploadSourceFrameId is not null || counters?.TerrainCellDataUploadHasSourceFrame is not null ||
        counters?.TerrainCellDataUploadObservationFrameId is not null || observation?.Available is not null ||
        observation?.Generation is not null || observation?.HasSourceFrame is not null ||
        observation?.SourceFrameId is not null || observation?.ObservationFrameId is not null;

    private static TerrainTextureUploadObservation? UploadObservation(CounterSnapshot? counters) => counters is null
        ? null
        : new TerrainTextureUploadObservation
        {
            Available = counters.TerrainCellDataUploadAvailable,
            Generation = counters.TerrainCellDataUploadGeneration,
            HasSourceFrame = counters.TerrainCellDataUploadHasSourceFrame,
            SourceFrameId = counters.TerrainCellDataUploadSourceFrameId,
            ObservationFrameId = counters.TerrainCellDataUploadObservationFrameId,
        };

    private static bool ValidInputs(FrameInputObservation? input) =>
        input is null ||
        (new[] { input.WorldGeneration, input.TerrainGeometryRevision, input.TerrainWindowRevision,
            input.LightingRegionRevision, input.DynamicLightsRevision, input.SettingsRevision,
            input.ResourceGeneration, input.ContributorRevision }.All(value => value is null or >= 0) &&
            (input.CameraX is null || double.IsFinite(input.CameraX.Value)) &&
            (input.CameraY is null || double.IsFinite(input.CameraY.Value)));

    private static long?[] Values(CounterSnapshot? counters) =>
    [
        counters?.TerrainRebuilds, counters?.TerrainFullPopulates, counters?.TerrainDirtyPatches,
        counters?.TerrainChunkLoads, counters?.LightingDynamicSolves, counters?.LightingDynamicTraces,
        counters?.LightingAtlasScrolls,
    ];

    private static long?[] Values(FrameCounterSnapshot? counters) =>
    [
        counters?.TerrainUploadCalls, counters?.TerrainUploadBytes,
        counters?.TerrainAtlasUploadCalls, counters?.TerrainAtlasUploadBytes,
        counters?.LightingFieldRebuilds, counters?.LightingStaticSolves,
    ];

    private static Dictionary<string, MetricStatistics> BuildStatistics(IReadOnlyList<FrameSample> frames)
    {
        Dictionary<string, MetricStatistics> result = [];
        foreach (IGrouping<SampleClass?, FrameSample> group in frames.GroupBy(frame => frame.Class))
        {
            foreach (string metric in MetricNames(group))
            {
                double[] values = group.Select(frame => Metric(frame, metric))
                    .OfType<double>().ToArray();
                if (values.Length > 0)
                {
                    result[$"{group.Key?.ToString().ToLowerInvariant() ?? "unknown"}.{metric}"] = Statistics(values);
                }
            }
        }

        return result;
    }

    private static IEnumerable<string> MetricNames(IEnumerable<FrameSample> frames) =>
        new[] { "frameDurationMs", "gpuFrameMs" }
            .Concat(_RequiredCpuMetrics.Select(name => $"cpu.{name}"))
            .Concat(frames.SelectMany(frame => frame.CpuMs?.Keys.AsEnumerable() ?? [])
                .Select(name => $"cpu.{name}"))
            .Distinct(StringComparer.Ordinal);

    private static double? Metric(FrameSample frame, string name)
    {
        if (name.StartsWith("cpu.", StringComparison.Ordinal) && !Correlated(frame))
        {
            return null;
        }

        return name switch
        {
            "frameDurationMs" => frame.FrameDurationMs,
            "gpuFrameMs" => frame.GpuFrameMs,
            _ => frame.CpuMs is not null && frame.CpuMs.TryGetValue(name[4..], out double? value) ? value : null,
        };
    }

    private static MetricStatistics Statistics(IEnumerable<double> values)
    {
        double[] sorted = values.Order().ToArray();
        // Callers never substitute an empty sample set with zero-valued statistics.
        return new MetricStatistics(sorted.Length, Percentile(sorted, 0.50),
            Percentile(sorted, 0.95), Percentile(sorted, 0.99), sorted[^1]);
    }

    private static double Percentile(double[] sorted, double fraction) =>
        sorted[Math.Max(0, (int)Math.Ceiling(fraction * sorted.Length) - 1)];

    private static bool Compatible(CaptureManifest left, CaptureManifest right) =>
        left.ScenarioId == right.ScenarioId && left.CapturePhase == right.CapturePhase &&
        left.WorkloadHash == right.WorkloadHash &&
        left.Runtime!.UnityVersion == right.Runtime!.UnityVersion &&
        left.Runtime.Platform == right.Runtime.Platform &&
        left.Runtime.GraphicsApi == right.Runtime.GraphicsApi &&
        left.Runtime.GraphicsDevice == right.Runtime.GraphicsDevice &&
        left.Runtime.Width == right.Runtime.Width && left.Runtime.Height == right.Runtime.Height &&
        left.Runtime.QualityProfile == right.Runtime.QualityProfile;

    private static bool ValidMetric(double? value) => value is null || FiniteNonNegative(value.Value);
    private static bool FiniteNonNegative(double value) => double.IsFinite(value) && value >= 0;
    private static CheckStatus Combine(CheckStatus left, CheckStatus right) =>
        left == CheckStatus.Fail || right == CheckStatus.Fail ? CheckStatus.Fail :
        left == CheckStatus.Incomplete || right == CheckStatus.Incomplete ? CheckStatus.Incomplete : CheckStatus.Pass;

    private static ValidationReport InvalidStructure(string finding) =>
        new(CheckStatus.Fail, CheckStatus.Incomplete, CheckStatus.Incomplete, CheckStatus.Incomplete,
            new[] { finding }, new Dictionary<string, MetricStatistics>());
}
