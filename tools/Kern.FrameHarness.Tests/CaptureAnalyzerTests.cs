using System.Text.Json;
using System.Text.Json.Nodes;
using Kern.FrameHarness;
using NUnit.Framework;

namespace Kern.FrameHarness.Tests;

[TestFixture]
public sealed class CaptureAnalyzerTests
{
    private static readonly ComparisonBudget _ZeroBudget = new(0, 0, 0, 0, 0, 0);

    [Test]
    public void ProductionBloomDispatchCountIsAcceptedAsAnOptionalObservation()
    {
        var capture = Capture(root => Frame(root, 0)["frameCounters"]!["bloomDispatches"] = 9);
        Assert.That(capture.Frames![0]!.FrameCounters!.BloomDispatches, Is.EqualTo(9));
        Assert.That(CaptureAnalyzer.Validate(capture).InputStatus, Is.EqualTo(CheckStatus.Pass));
        Assert.That(CaptureAnalyzer.Validate(Capture()).InputStatus, Is.EqualTo(CheckStatus.Pass));
    }

    [Test]
    public void NegativeBloomDispatchCountFailsInputValidation()
    {
        var capture = Capture(root => Frame(root, 0)["frameCounters"]!["bloomDispatches"] = -1);
        Assert.That(CaptureAnalyzer.Validate(capture).InputStatus, Is.EqualTo(CheckStatus.Fail));
    }

    [Test]
    public void AllocationReportUsesBytesAndDoesNotInventMissingSamples()
    {
        ValidationReport complete = CaptureAnalyzer.Validate(Capture(root =>
        {
            Frame(root, 0)["frameCounters"]!["gcAllocBytes"] = 1000;
            Frame(root, 1)["frameCounters"]!["gcAllocBytes"] = 3000;
            Frame(root, 0)["frameDurationMs"] = 10;
            Frame(root, 1)["frameDurationMs"] = 10;
        }));
        Assert.That(complete.Findings, Has.Some.Contains("mean=2000 B/frame"));
        Assert.That(complete.Findings, Has.Some.Contains("0.200 MB/s"));

        ValidationReport incomplete = CaptureAnalyzer.Validate(Capture(root =>
        {
            Frame(root, 0)["frameCounters"]!["gcAllocBytes"] = null;
            Frame(root, 1)["frameCounters"]!["gcAllocBytes"] = 3000;
        }));
        Assert.That(incomplete.Findings, Has.Some.Contains("missing bytes are not zero"));
        Assert.That(incomplete.Findings.Any(finding => finding.Contains("MB/s")), Is.False);
    }

    [Test]
    public void NegativeAllocationObservationFailsInputValidation()
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
            Frame(root, 0)["frameCounters"]!["gcAllocBytes"] = -1));
        Assert.That(report.InputStatus, Is.EqualTo(CheckStatus.Fail));
    }

    [Test]
    public void StableWorkHasIndependentInvariantAndPerformanceVerdicts()
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture());
        Assert.That(report.InputStatus, Is.EqualTo(CheckStatus.Pass));
        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Pass));
        Assert.That(report.PerformanceStatus, Is.EqualTo(CheckStatus.Incomplete));
        Assert.That(report.CoverageStatus, Is.EqualTo(CheckStatus.Pass));
    }

    [Test]
    public void RepeatedFrameFindingsAreSummarizedWithoutLosingAffectedFrameIds()
    {
        FrameCapture capture = Capture(root =>
        {
            Frame(root, 0)["class"] = null;
            Frame(root, 1)["class"] = null;
        });
        ValidationReport report = CaptureAnalyzer.Validate(capture);

        Assert.That(report.Findings, Has.Some.Contains("Frame *: sample class is unobserved."));
        Assert.That(report.Findings.Any(finding => finding.Contains("repeated on 2 frames; IDs 100…101")), Is.True);
    }

    [Test]
    public void MissingGpuDoesNotHideStaticSolveViolation()
    {
        FrameCapture capture = Capture(root =>
        {
            Frame(root, 1)["gpuFrameMs"] = null;
            Frame(root, 1)["frameCounters"]!["lightingStaticSolves"] = 1;
        });
        ValidationReport report = CaptureAnalyzer.Validate(capture);
        Assert.That(report.InputStatus, Is.EqualTo(CheckStatus.Pass));
        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Fail));
        Assert.That(report.CoverageStatus, Is.EqualTo(CheckStatus.Incomplete));
    }

    [Test]
    public void MissingCounterDoesNotHideKnownViolation()
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
        {
            Frame(root, 0)["frameCounters"]!["terrainUploadBytes"] = null;
            Frame(root, 1)["frameCounters"]!["lightingFieldRebuilds"] = 1;
        }));
        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Fail));
    }

    [TestCase("absent")]
    [TestCase("invalid")]
    [TestCase("stale")]
    public void ProducerFrameCorrelationCannotPass(string defect)
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
        {
            JsonObject frame = Frame(root, 1);
            if (defect == "absent")
            {
                frame.Remove("producerFrameId");
            }
            else if (defect == "invalid")
            {
                frame["producerLifecycleValid"] = false;
            }
            else
            {
                frame["producerFrameId"] = 999;
            }
        }));

        Assert.That(report.InputStatus, Is.EqualTo(CheckStatus.Pass));
        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Incomplete));
        Assert.That(report.PerformanceStatus, Is.EqualTo(CheckStatus.Incomplete));
    }

    [TestCase("cpuMs", "terrainMesh")]
    [TestCase("cumulative", "terrainRebuilds")]
    [TestCase("frameCounters", "lightingStaticSolves")]
    public void StaleProducerDoesNotLegitimizeMalformedValues(string section, string metric)
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
        {
            Frame(root, 1)["producerLifecycleValid"] = false;
            Frame(root, 1)[section]![metric] = -1;
        }));

        Assert.That(report.InputStatus, Is.EqualTo(CheckStatus.Fail));
        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Incomplete));
    }

    [Test]
    public void MissingProducerCorrelationDoesNotHideKnownViolation()
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
        {
            Frame(root, 1)["producerLifecycleValid"] = false;
            Frame(root, 0)["frameCounters"]!["lightingStaticSolves"] = 1;
        }));

        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Fail));
        Assert.That(report.PerformanceStatus, Is.EqualTo(CheckStatus.Incomplete));
    }

    [Test]
    public void StaleFrameCountersAreExcludedAndBreakCumulativeDeltaChain()
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
        {
            JsonObject stale = Frame(root, 0);
            stale["producerFrameId"] = 999;
            stale["cumulative"]!["terrainRebuilds"] = 11;
            stale["frameCounters"]!["lightingStaticSolves"] = 1;
            Frame(root, 1)["cumulative"]!["terrainRebuilds"] = 12;
        }));

        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Incomplete));
        Assert.That(report.Findings.Any(finding => finding.Contains("forbidden cumulative work counter")), Is.False);
        Assert.That(report.Findings.Any(finding => finding.Contains("terrain upload or lighting field/static work")), Is.False);
    }

    [Test]
    public void InvalidBaselineIsNotUsedForFirstCumulativeDelta()
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
        {
            root["baselineProducerLifecycleValid"] = false;
            Frame(root, 0)["cumulative"]!["terrainRebuilds"] = 11;
            Frame(root, 1)["cumulative"]!["terrainRebuilds"] = 11;
        }));

        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Incomplete));
        Assert.That(report.Findings.Any(finding => finding.Contains("forbidden cumulative work counter")), Is.False);
    }

    [Test]
    public void StaleCpuSampleIsExcludedFromDescriptiveStatisticsAndComparison()
    {
        FrameCapture before = Capture();
        FrameCapture after = Capture(root =>
        {
            root["captureId"] = "after";
            JsonObject stale = Frame(root, 1);
            stale["producerLifecycleValid"] = false;
            stale["cpuMs"]!["terrainMesh"] = 100;
        });
        ComparisonReport report = CaptureAnalyzer.Compare(before, after, _ZeroBudget);

        Assert.That(report.PerformanceStatus, Is.EqualTo(CheckStatus.Incomplete));
        Assert.That(report.Statistics.ContainsKey("steady.cpu.terrainMesh"), Is.False);
        ValidationReport validation = CaptureAnalyzer.Validate(after);
        Assert.That(validation.Statistics["steady.cpu.terrainMesh"].SampleCount, Is.EqualTo(1));
        Assert.That(validation.Statistics["steady.cpu.terrainMesh"].Max, Is.Zero);
    }

    [TestCase(true, 0)]
    [TestCase(true, 1)]
    [TestCase(false, 0)]
    [TestCase(false, 1)]
    public void MissingCpuCorrelationCannotCompareSparseDistributions(bool staleBefore, int staleIndex)
    {
        FrameCapture MakeCapture(bool candidate) => Capture(root =>
        {
            root["captureId"] = candidate ? "after" : "before";
            Frame(root, 0)["cpuMs"]!["terrainMesh"] = 1;
            Frame(root, 1)["cpuMs"]!["terrainMesh"] = 100;
            if (candidate != staleBefore)
            {
                Frame(root, staleIndex)["producerLifecycleValid"] = false;
            }
        });

        ComparisonReport report = CaptureAnalyzer.Compare(MakeCapture(false), MakeCapture(true), _ZeroBudget);
        Assert.That(report.PerformanceStatus, Is.EqualTo(CheckStatus.Incomplete));
        Assert.That(report.Statistics.ContainsKey("steady.cpu.terrainMesh"), Is.False);
        Assert.That(report.Statistics.ContainsKey("steady.frameDurationMs"), Is.True);
        Assert.That(report.Findings.Any(finding => finding.Contains("no sparse comparison")), Is.True);
    }

    [Test]
    public void CorrelatedFramesAfterStaleSampleRestoreCumulativeDetection()
    {
        FrameCapture capture = Capture(root =>
        {
            JsonObject third = Frame(root, 1).DeepClone().AsObject();
            third["frameId"] = 102;
            third["producerFrameId"] = 102;
            root["frames"]!.AsArray().Add(third);
            Frame(root, 0)["producerLifecycleValid"] = false;
            Frame(root, 0)["cumulative"]!["terrainRebuilds"] = 100;
            Frame(root, 2)["cumulative"]!["terrainRebuilds"] = 11;
        });
        ValidationReport report = CaptureAnalyzer.Validate(capture);
        Assert.That(report.InputStatus, Is.EqualTo(CheckStatus.Pass));
        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Fail));
        Assert.That(report.Findings.Any(finding => finding.Contains("frame 102: forbidden cumulative work counter")), Is.True);
    }

    [TestCase("absent")]
    [TestCase("stale")]
    [TestCase("gap")]
    public void BaselineStampMustNameImmediatelyPrecedingObservation(string defect)
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
        {
            if (defect == "absent")
            {
                root.Remove("baselineProducerFrameId");
            }
            else if (defect == "stale")
            {
                root["baselineProducerFrameId"] = 98;
            }
            else
            {
                root["baselineProducerFrameId"] = 98;
                root["baselineObservationFrameId"] = 98;
            }

            Frame(root, 0)["cumulative"]!["terrainRebuilds"] = 11;
            Frame(root, 1)["cumulative"]!["terrainRebuilds"] = 11;
        }));

        Assert.That(report.InputStatus, Is.EqualTo(CheckStatus.Pass));
        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Incomplete));
        Assert.That(report.CoverageStatus, Is.EqualTo(CheckStatus.Incomplete));
    }

    [Test]
    public void CompareKeepsKnownPerformanceRegressionFailWhenCorrelationIsMissing()
    {
        FrameCapture before = Capture();
        FrameCapture after = Capture(root =>
        {
            root["captureId"] = "after";
            Frame(root, 1)["producerLifecycleValid"] = false;
            Frame(root, 1)["frameDurationMs"] = 100;
        });
        ComparisonReport report = CaptureAnalyzer.Compare(before, after, _ZeroBudget);

        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Incomplete));
        Assert.That(report.PerformanceStatus, Is.EqualTo(CheckStatus.Fail));
    }

    [Test]
    public void ZeroCpuUploadTimerIsNotEvidenceOfZeroUploads()
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
            Frame(root, 0)["frameCounters"]!["terrainUploadCalls"] = null));
        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Incomplete));
    }

    [Test]
    public void ColdWorkIsNotChargedToNextSteadyFrame()
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
        {
            Frame(root, 0)["class"] = "cold";
            Frame(root, 0)["cumulative"]!["terrainRebuilds"] = 11;
            Frame(root, 1)["cumulative"]!["terrainRebuilds"] = 11;
        }));
        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Pass));
        Assert.That(report.CoverageStatus, Is.EqualTo(CheckStatus.Incomplete));
        Assert.That(report.Statistics["cold.frameDurationMs"].SampleCount, Is.EqualTo(1));
    }

    [TestCase("cold")]
    [TestCase("reanchor")]
    public void NoSteadySamplesCannotPass(string kind)
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
        {
            Frame(root, 0)["class"] = kind;
            Frame(root, 1)["class"] = kind;
        }));
        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Incomplete));
    }

    [Test]
    public void CumulativeDecreaseWithoutResetFails()
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
            Frame(root, 1)["cumulative"]!["terrainRebuilds"] = 0));
        Assert.That(report.InputStatus, Is.EqualTo(CheckStatus.Fail));
    }

    [Test]
    public void ExplicitResetIsUnknownDeltaNotZero()
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
        {
            Frame(root, 1)["counterGeneration"] = 1;
            Frame(root, 1)["counterResetObserved"] = true;
            Frame(root, 1)["cumulative"]!["terrainRebuilds"] = 0;
        }));
        Assert.That(report.InputStatus, Is.EqualTo(CheckStatus.Pass));
        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Incomplete));
    }

    [Test]
    public void ColdResetAllowsNewSteadyBaseline()
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
        {
            Frame(root, 0)["class"] = "cold";
            for (int i = 0; i < 2; i++)
            {
                Frame(root, i)["counterGeneration"] = 1;
                Frame(root, i)["counterResetObserved"] = i == 0;
                Frame(root, i)["cumulative"]!["terrainRebuilds"] = 0;
            }
        }));
        Assert.That(report.InputStatus, Is.EqualTo(CheckStatus.Pass));
        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Pass));
    }

    [TestCase(1, false)]
    [TestCase(0, true)]
    [TestCase(2, true)]
    [TestCase(-1, false)]
    public void InvalidGenerationTransitionFails(int generation, bool reset)
    {
        Assert.That(CaptureAnalyzer.Validate(Capture(root =>
        {
            Frame(root, 1)["counterGeneration"] = generation;
            Frame(root, 1)["counterResetObserved"] = reset;
        })).InputStatus, Is.EqualTo(CheckStatus.Fail));
    }

    [TestCase("manifest")]
    [TestCase("frames")]
    public void NullRootCollectionsFailWithoutException(string property)
    {
        Assert.That(CaptureAnalyzer.Validate(Capture(root => root[property] = null)).InputStatus,
            Is.EqualTo(CheckStatus.Fail));
    }

    [TestCase("runtime")]
    [TestCase("build")]
    [TestCase("unavailableMetrics")]
    public void NullManifestObjectsFailWithoutException(string property)
    {
        Assert.That(CaptureAnalyzer.Validate(Capture(root => root["manifest"]![property] = null)).InputStatus,
            Is.EqualTo(CheckStatus.Fail));
    }

    [Test]
    public void NullFrameFailsWithoutException()
    {
        Assert.That(CaptureAnalyzer.Validate(Capture(root => root["frames"]![0] = null)).InputStatus,
            Is.EqualTo(CheckStatus.Fail));
    }

    [TestCase("inputs")]
    [TestCase("class")]
    [TestCase("frameCounters")]
    [TestCase("cumulative")]
    public void UnobservedEvidenceIsIncomplete(string property)
    {
        Assert.That(CaptureAnalyzer.Validate(Capture(root => Frame(root, 0)[property] = null)).InvariantStatus,
            Is.EqualTo(CheckStatus.Incomplete));
    }

    [TestCase(-1)]
    [TestCase(100)]
    [TestCase(103)]
    public void NegativeDuplicateOrSkippedFrameFails(long frameId)
    {
        Assert.That(CaptureAnalyzer.Validate(Capture(root => Frame(root, 1)["frameId"] = frameId)).InputStatus,
            Is.EqualTo(CheckStatus.Fail));
    }

    [Test]
    public void UnknownScenarioDoesNotGetFreePass()
    {
        Assert.That(CaptureAnalyzer.Validate(Capture(root => root["manifest"]!["scenarioId"] = "S3")).InvariantStatus,
            Is.EqualTo(CheckStatus.Incomplete));
    }

    [TestCase("terrainGeometryRevision")]
    [TestCase("resourceGeneration")]
    [TestCase("contributorRevision")]
    [TestCase("settingsRevision")]
    [TestCase("worldGeneration")]
    public void ChangedStaticDependencyIsNotAStableInput(string property)
    {
        Assert.That(CaptureAnalyzer.Validate(Capture(root => Frame(root, 1)["inputs"]![property] = 2)).InvariantStatus,
            Is.EqualTo(CheckStatus.Incomplete));
    }

    [Test]
    public void PendingWorkCannotBeDeclaredSteady()
    {
        Assert.That(CaptureAnalyzer.Validate(Capture(root => Frame(root, 1)["inputs"]!["workQueuesDrained"] = false))
            .InvariantStatus, Is.EqualTo(CheckStatus.Incomplete));
    }

    [TestCase("S1")]
    [TestCase("S4")]
    public void MotionScenarioRequiresObservedMotion(string scenario)
    {
        Assert.That(CaptureAnalyzer.Validate(Capture(root => root["manifest"]!["scenarioId"] = scenario)).InvariantStatus,
            Is.EqualTo(CheckStatus.Incomplete));
    }

    [Test]
    public void CameraMovesWithinPreparedWindowWithoutRebuild()
    {
        Assert.That(CaptureAnalyzer.Validate(Capture(root =>
        {
            root["manifest"]!["scenarioId"] = "S1";
            Frame(root, 1)["inputs"]!["cameraX"] = 0.125;
        })).InvariantStatus, Is.EqualTo(CheckStatus.Pass));
    }

    [TestCase(true, CheckStatus.Pass)]
    [TestCase(false, CheckStatus.Fail)]
    public void SubCellDynamicMotionRequiresSolveAndTrace(bool traced, CheckStatus expected)
    {
        Assert.That(CaptureAnalyzer.Validate(Capture(root =>
        {
            root["manifest"]!["scenarioId"] = "S4";
            Frame(root, 1)["inputs"]!["dynamicLightsRevision"] = 2;
            Frame(root, 1)["cumulative"]!["lightingDynamicSolves"] = 11;
            Frame(root, 1)["cumulative"]!["lightingDynamicTraces"] = traced ? 11 : 10;
        })).InvariantStatus, Is.EqualTo(expected));
    }

    [TestCase("terrainCellDataApplyCalls", "terrainCellDataApplyPayloadBytes", 1, 4096)]
    [TestCase("terrainCellDataApplyCalls", "terrainCellDataApplyPayloadBytes", 9, 36864)]
    [TestCase("terrainCellDataCopyTextureCalls", "terrainCellDataCopyTexturePayloadBytes", 9, 9216)]
    public void PositiveCellDataUploadFailsSteadyOpt1EvenWhenGpuIsMissing(
        string callsMetric, string bytesMetric, long calls, long bytes)
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
        {
            EnableUploadTelemetry(root);
            JsonObject current = Frame(root, 1);
            current["gpuFrameMs"] = null;
            current["cumulative"]![callsMetric] = calls;
            current["cumulative"]![bytesMetric] = bytes;
            current["cumulative"]!["terrainCellDataUploadHasSourceFrame"] = true;
            current["cumulative"]!["terrainCellDataUploadSourceFrameId"] = 100;
            current["terrainCellDataUpload"]!["hasSourceFrame"] = true;
            current["terrainCellDataUpload"]!["sourceFrameId"] = 100;
            current["frameCounters"]![callsMetric] = calls;
            current["frameCounters"]![bytesMetric] = bytes;
            current["frameCounters"]!["terrainCellDataUploadFrameDeltaValid"] = true;
            current["frameCounters"]!["terrainCellDataUploadDeltaStartObservationFrameId"] = 100;
            current["frameCounters"]!["terrainCellDataUploadDeltaEndObservationFrameId"] = 101;
        }));

        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Fail));
        Assert.That(report.Findings.Any(finding => finding.Contains("terrain cell-data Apply or CopyTexture work occurred")), Is.True);
    }

    [Test]
    public void AdjacentAvailableCellDataEndpointsWithZeroDeltaPass()
    {
        EnableUploadTelemetry(Fixture());
        Assert.That(CaptureAnalyzer.Validate(Capture(EnableUploadTelemetry)).InvariantStatus, Is.EqualTo(CheckStatus.Pass));
    }

    [Test]
    public void StaleLastUploadSourceFrameIsNotConfusedWithCurrentObservationFrame()
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
        {
            EnableUploadTelemetry(root);
            JsonObject baseline = root["counterBaseline"]!.AsObject();
            baseline["terrainCellDataApplyCalls"] = 1;
            baseline["terrainCellDataApplyPayloadBytes"] = 1024;
            SetUploadSource(root, -1, hasSourceFrame: true, sourceFrameId: 70);
            Frame(root, 0)["cumulative"]!["terrainCellDataApplyCalls"] = 1;
            Frame(root, 0)["cumulative"]!["terrainCellDataApplyPayloadBytes"] = 1024;
            Frame(root, 1)["cumulative"]!["terrainCellDataApplyCalls"] = 1;
            Frame(root, 1)["cumulative"]!["terrainCellDataApplyPayloadBytes"] = 1024;
            SetUploadSource(root, 0, hasSourceFrame: true, sourceFrameId: 70);
            SetUploadSource(root, 1, hasSourceFrame: true, sourceFrameId: 70);
        }));

        Assert.That(report.InputStatus, Is.EqualTo(CheckStatus.Pass));
        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Pass));
    }

    [Test]
    public void UploadGenerationChangeMakesDeltaIncomplete()
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
        {
            EnableUploadTelemetry(root);
            SetUploadGeneration(root, 1, 2);
            SetUploadDeltaUnavailable(root, 1);
        }));

        Assert.That(report.InputStatus, Is.EqualTo(CheckStatus.Pass));
        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Incomplete));
    }

    [Test]
    public void UploadObservationGapMakesDeltaIncomplete()
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
        {
            EnableUploadTelemetry(root);
            SetUploadObservationFrame(root, 1, 105);
            SetUploadDeltaUnavailable(root, 1);
        }));

        Assert.That(report.InputStatus, Is.EqualTo(CheckStatus.Pass));
        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Incomplete));
    }

    [Test]
    public void FalseExportedDeltaFlagCannotHidePositiveCumulativeWork()
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
        {
            EnableUploadTelemetry(root);
            JsonObject current = Frame(root, 1);
            current["cumulative"]!["terrainCellDataApplyCalls"] = 1;
            current["cumulative"]!["terrainCellDataApplyPayloadBytes"] = 1024;
            current["cumulative"]!["terrainCellDataUploadHasSourceFrame"] = true;
            current["cumulative"]!["terrainCellDataUploadSourceFrameId"] = 100;
            current["terrainCellDataUpload"]!["hasSourceFrame"] = true;
            current["terrainCellDataUpload"]!["sourceFrameId"] = 100;
            current["frameCounters"]!["terrainCellDataUploadFrameDeltaValid"] = false;
            current["frameCounters"]!["terrainCellDataApplyCalls"] = null;
            current["frameCounters"]!["terrainCellDataApplyPayloadBytes"] = null;
            current["frameCounters"]!["terrainCellDataUploadDeltaStartObservationFrameId"] = null;
            current["frameCounters"]!["terrainCellDataUploadDeltaEndObservationFrameId"] = null;
        }));

        Assert.That(report.InputStatus, Is.EqualTo(CheckStatus.Fail));
        Assert.That(report.InvariantStatus, Is.Not.EqualTo(CheckStatus.Pass));
    }

    [Test]
    public void MismatchingExportedDeltaIsMalformed()
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
        {
            EnableUploadTelemetry(root);
            Frame(root, 1)["frameCounters"]!["terrainCellDataApplyCalls"] = 1;
        }));

        Assert.That(report.InputStatus, Is.EqualTo(CheckStatus.Fail));
    }

    [TestCase("negative")]
    [TestCase("payload-mismatch")]
    public void MalformedUploadCumulativeValuesFailInputValidation(string defect)
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
        {
            EnableUploadTelemetry(root);
            JsonObject current = Frame(root, 1)["cumulative"]!.AsObject();
            current["terrainCellDataApplyCalls"] = defect == "negative" ? -1 : 1;
        }));

        Assert.That(report.InputStatus, Is.EqualTo(CheckStatus.Fail));
    }

    [Test]
    public void LegacyCaptureWithoutOptionalCellDataMetricsKeepsExistingVerdict()
    {
        Assert.That(CaptureAnalyzer.Validate(Capture()).InvariantStatus, Is.EqualTo(CheckStatus.Pass));
    }

    [Test]
    public void MissingOptionalCellDataMetricsDoNotHideUnrelatedKnownFailure()
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
            Frame(root, 1)["frameCounters"]!["lightingStaticSolves"] = 1));

        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Fail));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AdjacentButShiftedUploadObservationsCannotEstablishWork(bool positive)
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
        {
            EnableUploadTelemetry(root);
            root["counterBaseline"]!["terrainCellDataUploadObservationFrameId"] = 89;
            for (int i = 0; i < 2; i++)
            {
                SetUploadObservationFrame(root, i, 90 + i);
                Frame(root, i)["frameCounters"]!["terrainCellDataUploadDeltaStartObservationFrameId"] = 89 + i;
                Frame(root, i)["frameCounters"]!["terrainCellDataUploadDeltaEndObservationFrameId"] = 90 + i;
            }

            if (positive)
            {
                SetUploadSource(root, 1, true, 91);
                Frame(root, 1)["cumulative"]!["terrainCellDataApplyCalls"] = 1;
                Frame(root, 1)["cumulative"]!["terrainCellDataApplyPayloadBytes"] = 64;
                Frame(root, 1)["frameCounters"]!["terrainCellDataApplyCalls"] = 1;
                Frame(root, 1)["frameCounters"]!["terrainCellDataApplyPayloadBytes"] = 64;
            }
        }));
        Assert.That(report.InputStatus, Is.EqualTo(CheckStatus.Pass));
        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Incomplete));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NullOrUnavailableExporterSnapshotsAreIncompleteNotMalformed(bool disposed)
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
        {
            EnableUploadTelemetry(root);
            JsonObject[] endpoints = [root["counterBaseline"]!.AsObject(),
                Frame(root, 0)["cumulative"]!.AsObject(), Frame(root, 1)["cumulative"]!.AsObject()];
            foreach (JsonObject endpoint in endpoints)
            {
                foreach (string key in endpoint.Select(pair => pair.Key).Where(key => key.StartsWith("terrainCellData")).ToArray())
                {
                    if (!disposed || key.EndsWith("Calls") || key.EndsWith("Bytes"))
                    {
                        endpoint[key] = null;
                    }
                }
                if (disposed) { endpoint["terrainCellDataUploadAvailable"] = false; }
            }

            for (int i = 0; i < 2; i++)
            {
                SetUploadDeltaUnavailable(root, i);
                JsonObject observation = Frame(root, i)["terrainCellDataUpload"]!.AsObject();
                if (disposed) { observation["available"] = false; }
                else
                {
                    foreach (string key in observation.Select(pair => pair.Key).ToArray()) { observation[key] = null; }
                }
            }
        }));
        Assert.That(report.InputStatus, Is.EqualTo(CheckStatus.Pass));
        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Incomplete));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void InconsistentCallByteDeltaIsMalformedAndNeverPositive(bool flag)
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
        {
            EnableUploadTelemetry(root);
            foreach (JsonObject counters in new[] { root["counterBaseline"]!.AsObject(),
                Frame(root, 0)["cumulative"]!.AsObject(), Frame(root, 1)["cumulative"]!.AsObject() })
            {
                counters["terrainCellDataApplyCalls"] = 1;
                counters["terrainCellDataApplyPayloadBytes"] = 64;
            }
            Frame(root, 1)["cumulative"]!["terrainCellDataApplyCalls"] = 2;
            if (!flag) { SetUploadDeltaUnavailable(root, 1); }
        }));
        Assert.That(report.InputStatus, Is.EqualTo(CheckStatus.Fail));
        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Incomplete));
    }

    [TestCase("S0")]
    [TestCase("S1")]
    [TestCase("S4")]
    public void CellDataViolationSurvivesMissingAggregateAndGpuMetrics(string scenario)
    {
        ValidationReport report = CaptureAnalyzer.Validate(Capture(root =>
        {
            EnableUploadTelemetry(root);
            root["manifest"]!["scenarioId"] = scenario;
            JsonObject frame = Frame(root, 1);
            if (scenario == "S1") { frame["inputs"]!["cameraX"] = 0.125; }
            if (scenario == "S4")
            {
                frame["inputs"]!["dynamicLightsRevision"] = 2;
                frame["cumulative"]!["lightingDynamicSolves"] = 11;
                frame["cumulative"]!["lightingDynamicTraces"] = 11;
            }
            frame["gpuFrameMs"] = null;
            foreach (string key in new[] { "terrainUploadCalls", "terrainUploadBytes", "terrainAtlasUploadCalls", "terrainAtlasUploadBytes" })
            { frame["frameCounters"]![key] = null; }
            SetUploadSource(root, 1, true, 101);
            frame["cumulative"]!["terrainCellDataApplyCalls"] = 1;
            frame["cumulative"]!["terrainCellDataApplyPayloadBytes"] = 64;
            frame["frameCounters"]!["terrainCellDataApplyCalls"] = 1;
            frame["frameCounters"]!["terrainCellDataApplyPayloadBytes"] = 64;
        }));
        Assert.That(report.InputStatus, Is.EqualTo(CheckStatus.Pass));
        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Fail));
    }

    [Test]
    public void CpuImprovementCannotMaskGpuRegression()
    {
        ComparisonReport report = Compare(root =>
        {
            Frame(root, 1)["frameDurationMs"] = 0.5;
            Frame(root, 1)["gpuFrameMs"] = 20;
        });
        Assert.That(report.PerformanceStatus, Is.EqualTo(CheckStatus.Fail));
        Assert.That(report.Statistics["steady.gpuFrameMs"].After.Max, Is.EqualTo(20));
    }

    [Test]
    public void StageRegressionIsMeasuredIndependentlyOfWholeFrame()
    {
        Assert.That(Compare(root => Frame(root, 1)["cpuMs"]!["lightingCascadeTrace"] = 2).PerformanceStatus,
            Is.EqualTo(CheckStatus.Fail));
    }

    [Test]
    public void KnownTimingRegressionSurvivesMissingGpu()
    {
        Assert.That(Compare(root =>
        {
            Frame(root, 1)["gpuFrameMs"] = null;
            Frame(root, 1)["frameDurationMs"] = 20;
        }).PerformanceStatus, Is.EqualTo(CheckStatus.Fail));
    }

    [TestCase("frameDurationMs")]
    [TestCase("gpuFrameMs")]
    public void SparseTimingsNeverProducePerformancePass(string metric)
    {
        Assert.That(Compare(root => Frame(root, 1)[metric] = null).PerformanceStatus,
            Is.EqualTo(CheckStatus.Incomplete));
    }

    [Test]
    public void CorrectnessFailureIsNotHiddenByFastFrames()
    {
        ComparisonReport report = Compare(root => Frame(root, 1)["frameCounters"]!["lightingStaticSolves"] = 1);
        Assert.That(report.PerformanceStatus, Is.EqualTo(CheckStatus.Pass));
        Assert.That(report.InvariantStatus, Is.EqualTo(CheckStatus.Fail));
    }

    [TestCase("workloadHash")]
    [TestCase("capturePhase")]
    [TestCase("scenarioId")]
    public void IncompatibleScenarioFails(string property)
    {
        Assert.That(Compare(root => root["manifest"]![property] = "different").CompatibilityStatus,
            Is.EqualTo(CheckStatus.Fail));
    }

    [TestCase("qualityProfile")]
    [TestCase("graphicsApi")]
    [TestCase("graphicsDevice")]
    [TestCase("unityVersion")]
    public void IncompatibleRuntimeFails(string property)
    {
        Assert.That(Compare(root => root["manifest"]!["runtime"]![property] = "different").CompatibilityStatus,
            Is.EqualTo(CheckStatus.Fail));
    }

    [Test]
    public void UnknownWorkloadCannotCompare()
    {
        Assert.That(Compare(root => root["manifest"]!["workloadHash"] = null).CompatibilityStatus,
            Is.EqualTo(CheckStatus.Incomplete));
    }

    [Test]
    public void LoadedArtifactsMayDifferButMustBeIdentified()
    {
        ComparisonReport report = Compare(root => root["manifest"]!["build"]!["loadedArtifactId"] = "build-b");
        Assert.That(report.CompatibilityStatus, Is.EqualTo(CheckStatus.Pass));
        Assert.That(report.ArtifactIdentityStatus, Is.EqualTo(CheckStatus.Pass));
        Assert.That(report.BeforeArtifactIdentity, Is.EqualTo("build-a"));
        Assert.That(report.AfterArtifactIdentity, Is.EqualTo("build-b"));
        Assert.That(Compare(root => root["manifest"]!["build"]!["loadedArtifactId"] = null).PerformanceStatus,
            Is.EqualTo(CheckStatus.Incomplete));
    }

    [Test]
    public void SameCaptureCannotBeItsOwnBaseline()
    {
        FrameCapture capture = Capture();
        Assert.That(CaptureAnalyzer.Compare(capture, capture, _ZeroBudget).CompatibilityStatus,
            Is.EqualTo(CheckStatus.Fail));
    }

    [Test]
    public void UnequalClassCountsCannotPass()
    {
        Assert.That(Compare(root => Frame(root, 1)["class"] = "reanchor").CompatibilityStatus,
            Is.EqualTo(CheckStatus.Incomplete));
    }

    [TestCase("cold")]
    [TestCase("reanchor")]
    public void RareTransitionRegressionHasItsOwnBudget(string kind)
    {
        FrameCapture before = Capture(root => Frame(root, 0)["class"] = kind);
        FrameCapture after = Capture(root =>
        {
            root["captureId"] = "after";
            Frame(root, 0)["class"] = kind;
            Frame(root, 0)["gpuFrameMs"] = 50;
        });
        ComparisonReport report = CaptureAnalyzer.Compare(before, after, _ZeroBudget);
        Assert.That(report.PerformanceStatus, Is.EqualTo(CheckStatus.Fail));
        Assert.That(report.Statistics[$"{kind}.gpuFrameMs"].After.Max, Is.EqualTo(50));
    }

    [Test]
    public void OneSpikeInSixHundredCannotHideBehindPercentiles()
    {
        FrameCapture before = LongCapture(false);
        FrameCapture after = LongCapture(true);
        ComparisonReport report = CaptureAnalyzer.Compare(before, after, _ZeroBudget);
        MetricStatistics stats = report.Statistics["steady.frameDurationMs"].After;
        Assert.That(stats.SampleCount, Is.EqualTo(600));
        Assert.That(stats.P95, Is.EqualTo(1));
        Assert.That(stats.P99, Is.EqualTo(1));
        Assert.That(stats.Max, Is.EqualTo(100));
        Assert.That(report.PerformanceStatus, Is.EqualTo(CheckStatus.Fail));
    }

    [TestCase(-1)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void InvalidBudgetsFail(double value)
    {
        Assert.That(CaptureAnalyzer.Compare(Capture(), Capture(root => root["captureId"] = "after"),
            new ComparisonBudget(value, 0, 0, 0, 0, 0)).CompatibilityStatus, Is.EqualTo(CheckStatus.Fail));
    }

    [TestCase("-1")]
    [TestCase("0")]
    public void NonpositiveFrameTimeFails(string value)
    {
        Assert.That(CaptureAnalyzer.Validate(Capture(root => Frame(root, 0)["frameDurationMs"] = JsonNode.Parse(value)))
            .InputStatus, Is.EqualTo(CheckStatus.Fail));
    }

    [TestCase("99")]
    [TestCase("\"bogus\"")]
    public void InvalidEnumJsonRejected(string value)
    {
        JsonObject root = Fixture();
        Frame(root, 0)["class"] = JsonNode.Parse(value);
        Assert.Throws<JsonException>(() => CaptureJson.Parse(root.ToJsonString()));
    }

    [Test]
    public void UnknownPropertyRejected()
    {
        JsonObject root = Fixture();
        root["typo"] = 0;
        Assert.Throws<JsonException>(() => CaptureJson.Parse(root.ToJsonString()));
    }

    [TestCase("frameId")]
    [TestCase("counterGeneration")]
    [TestCase("counterResetObserved")]
    public void RequiredObservationCannotDefaultToZero(string property)
    {
        JsonObject root = Fixture();
        Frame(root, 0).Remove(property);
        Assert.Throws<JsonException>(() => CaptureJson.Parse(root.ToJsonString()));
    }

    [Test]
    public void DuplicatePropertiesCannotOverwriteEvidence()
    {
        string json = Fixture().ToJsonString().Replace("\"lightingStaticSolves\":0",
            "\"lightingStaticSolves\":1,\"lightingStaticSolves\":0", StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => CaptureJson.Parse(json));
    }

    [Test]
    public void ManagedAssemblyIdentityDoesNotIdentifyProductionContent()
    {
        ComparisonReport report = Compare(root => root["manifest"]!["build"]!["artifactScope"] = "managed-code-only");
        Assert.That(report.ArtifactIdentityStatus, Is.EqualTo(CheckStatus.Incomplete));
        Assert.That(report.PerformanceStatus, Is.EqualTo(CheckStatus.Incomplete));
    }

    [Test]
    public void JsonReportIncludesBothMetricDistributions()
    {
        JsonNode json = JsonNode.Parse(CaptureJson.WriteReport(Compare(_ => { })))!;
        Assert.That(json["statistics"]!["steady.gpuFrameMs"]!["before"]!["sampleCount"]!.GetValue<int>(), Is.EqualTo(2));
        Assert.That(json["statistics"]!["steady.gpuFrameMs"]!["after"]!["max"]!.GetValue<double>(), Is.EqualTo(1));
    }

    [Test]
    public void JsonNullRejected() => Assert.Throws<InvalidDataException>(() => CaptureJson.Parse("null"));

    [Test]
    public void SchemaRoundTripPreservesUnknowns()
    {
        FrameCapture capture = Capture(root => Frame(root, 0)["gpuFrameMs"] = null);
        FrameCapture roundTrip = CaptureJson.Parse(CaptureJson.Write(capture));
        Assert.That(roundTrip.Frames![0]!.GpuFrameMs, Is.Null);
        Assert.That(CaptureAnalyzer.Validate(roundTrip).CoverageStatus, Is.EqualTo(CheckStatus.Incomplete));
    }

    private static ComparisonReport Compare(Action<JsonObject> change) =>
        CaptureAnalyzer.Compare(Capture(), Capture(root =>
        {
            root["captureId"] = "after";
            change(root);
        }), _ZeroBudget);

    private static FrameCapture LongCapture(bool spike) => Capture(root =>
    {
        root["captureId"] = spike ? "after" : "before";
        JsonObject template = (JsonObject)Frame(root, 0).DeepClone();
        JsonArray frames = [];
        for (int i = 0; i < 600; i++)
        {
            JsonObject frame = (JsonObject)template.DeepClone();
            frame["frameId"] = 100 + i;
            frame["producerFrameId"] = 100 + i;
            frame["frameDurationMs"] = spike && i == 599 ? 100 : 1;
            frames.Add(frame);
        }

        root["frames"] = frames;
    });

    private static FrameCapture Capture(Action<JsonObject>? change = null)
    {
        JsonObject fixture = Fixture();
        change?.Invoke(fixture);
        return CaptureJson.Parse(fixture.ToJsonString());
    }

    private static JsonObject Frame(JsonObject root, int index) => root["frames"]![index]!.AsObject();

    private static void EnableUploadTelemetry(JsonObject root)
    {
        SetUploadEndpoint(root["counterBaseline"]!.AsObject(), observationFrameId: 99);
        for (int index = 0; index < root["frames"]!.AsArray().Count; index++)
        {
            JsonObject frame = Frame(root, index);
            SetUploadEndpoint(frame["cumulative"]!.AsObject(), observationFrameId: 100 + index);
            frame["terrainCellDataUpload"] = UploadObservation(observationFrameId: 100 + index);
            JsonObject counters = frame["frameCounters"]!.AsObject();
            counters["terrainCellDataApplyCalls"] = 0;
            counters["terrainCellDataApplyPayloadBytes"] = 0;
            counters["terrainCellDataCopyTextureCalls"] = 0;
            counters["terrainCellDataCopyTexturePayloadBytes"] = 0;
            counters["terrainCellDataUploadFrameDeltaValid"] = true;
            counters["terrainCellDataUploadDeltaStartObservationFrameId"] = 99 + index;
            counters["terrainCellDataUploadDeltaEndObservationFrameId"] = 100 + index;
        }
    }

    private static void SetUploadEndpoint(JsonObject cumulative, int observationFrameId)
    {
        cumulative["terrainCellDataApplyCalls"] = 0;
        cumulative["terrainCellDataApplyPayloadBytes"] = 0;
        cumulative["terrainCellDataCopyTextureCalls"] = 0;
        cumulative["terrainCellDataCopyTexturePayloadBytes"] = 0;
        cumulative["terrainCellDataUploadGeneration"] = 1;
        cumulative["terrainCellDataUploadAvailable"] = true;
        cumulative["terrainCellDataUploadSourceFrameId"] = null;
        cumulative["terrainCellDataUploadHasSourceFrame"] = false;
        cumulative["terrainCellDataUploadObservationFrameId"] = observationFrameId;
    }

    private static JsonObject UploadObservation(int observationFrameId) => new()
    {
        ["available"] = true,
        ["generation"] = 1,
        ["hasSourceFrame"] = false,
        ["sourceFrameId"] = null,
        ["observationFrameId"] = observationFrameId,
    };

    private static void SetUploadSource(JsonObject root, int frameIndex, bool hasSourceFrame, int? sourceFrameId)
    {
        JsonObject cumulative = frameIndex < 0
            ? root["counterBaseline"]!.AsObject()
            : Frame(root, frameIndex)["cumulative"]!.AsObject();
        cumulative["terrainCellDataUploadHasSourceFrame"] = hasSourceFrame;
        cumulative["terrainCellDataUploadSourceFrameId"] = sourceFrameId;
        if (frameIndex >= 0)
        {
            Frame(root, frameIndex)["terrainCellDataUpload"]!["hasSourceFrame"] = hasSourceFrame;
            Frame(root, frameIndex)["terrainCellDataUpload"]!["sourceFrameId"] = sourceFrameId;
        }
    }

    private static void SetUploadGeneration(JsonObject root, int frameIndex, int generation)
    {
        Frame(root, frameIndex)["cumulative"]!["terrainCellDataUploadGeneration"] = generation;
        Frame(root, frameIndex)["terrainCellDataUpload"]!["generation"] = generation;
    }

    private static void SetUploadObservationFrame(JsonObject root, int frameIndex, int observationFrameId)
    {
        Frame(root, frameIndex)["cumulative"]!["terrainCellDataUploadObservationFrameId"] = observationFrameId;
        Frame(root, frameIndex)["terrainCellDataUpload"]!["observationFrameId"] = observationFrameId;
    }

    private static void SetUploadDeltaUnavailable(JsonObject root, int frameIndex)
    {
        JsonObject counters = Frame(root, frameIndex)["frameCounters"]!.AsObject();
        counters["terrainCellDataApplyCalls"] = null;
        counters["terrainCellDataApplyPayloadBytes"] = null;
        counters["terrainCellDataCopyTextureCalls"] = null;
        counters["terrainCellDataCopyTexturePayloadBytes"] = null;
        counters["terrainCellDataUploadFrameDeltaValid"] = false;
        counters["terrainCellDataUploadDeltaStartObservationFrameId"] = null;
        counters["terrainCellDataUploadDeltaEndObservationFrameId"] = null;
    }

    // Independent, explicit observations. No production renderer helper generates expected outcomes.
    internal static JsonObject Fixture()
    {
        JsonObject root = JsonNode.Parse("""
        {
          "schemaVersion": 1,
          "harnessVersion": "1",
          "captureId": "before",
          "capturedAtUtc": "2026-09-28T00:00:00.0000000Z",
          "manifest": {
            "scenarioId": "S0",
            "capturePhase": "fixture-end-of-frame",
            "workloadHash": "fixture-world-and-inputs-v1",
            "build": { "applicationVersion": "fixture", "loadedArtifactId": "build-a", "artifactScope": "production-content" },
            "runtime": {
              "unityVersion": "fixture", "platform": "fixture", "graphicsApi": "fixture",
              "graphicsDevice": "fixture", "width": 640, "height": 480, "qualityProfile": "fixture-full"
            },
            "unavailableMetrics": {},
            "observationEvidence": "independent fixture, not game evidence",
            "visualCoverage": true,
            "visualEvidence": "fixture oracle only; never a production claim"
          },
          "counterBaselineGeneration": 0,
          "baselineObservationFrameId": 99,
          "baselineProducerFrameId": 99,
          "baselineProducerLifecycleValid": true,
          "counterBaseline": {
            "terrainRebuilds": 10, "terrainFullPopulates": 10, "terrainDirtyPatches": 10,
            "terrainChunkLoads": 10, "lightingDynamicSolves": 10, "lightingDynamicTraces": 10,
            "lightingAtlasScrolls": 10
          },
          "inputBaseline": {
            "worldGeneration": 1, "terrainGeometryRevision": 1, "terrainWindowRevision": 1,
            "lightingRegionRevision": 1, "dynamicLightsRevision": 1, "settingsRevision": 1,
            "resourceGeneration": 1, "contributorRevision": 1, "cameraX": 0, "cameraY": 0,
            "terrainReady": true, "lightingReady": true, "cameraWithinPreparedWindow": true,
            "workQueuesDrained": true
          },
          "frames": []
        }
        """)!.AsObject();
        for (int i = 0; i < 2; i++)
        {
            JsonObject frame = JsonNode.Parse("""
            {
              "frameId": 0, "producerFrameId": 0, "producerLifecycleValid": true,
              "class": "steady", "frameDurationMs": 1, "gpuFrameMs": 1,
              "counterGeneration": 0, "counterResetObserved": false,
              "cpuMs": {
                "terrainMesh": 0, "terrainCache": 0, "terrainFloodFill": 0, "terrainGpuUpload": 0,
                "terrainAtlasUpload": 0, "lightingBuildCommands": 0, "lightingExecuteCommands": 0,
                "lightingCascadeTrace": 0, "lightingCascadeMerge": 0, "lightingDynamic": 0, "lightingComposite": 0
              },
              "frameCounters": {
                "terrainUploadCalls": 0, "terrainUploadBytes": 0,
                "terrainAtlasUploadCalls": 0, "terrainAtlasUploadBytes": 0,
                "lightingFieldRebuilds": 0, "lightingStaticSolves": 0
              }
            }
            """)!.AsObject();
            frame["frameId"] = 100 + i;
            frame["producerFrameId"] = 100 + i;
            frame["inputs"] = root["inputBaseline"]!.DeepClone();
            frame["cumulative"] = root["counterBaseline"]!.DeepClone();
            root["frames"]!.AsArray().Add(frame);
        }

        return root;
    }
}
