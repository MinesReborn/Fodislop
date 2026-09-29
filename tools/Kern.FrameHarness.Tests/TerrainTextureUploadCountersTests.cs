using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Kern.Core;
using NUnit.Framework;
using UnityEngine;

namespace Kern.FrameHarness.Tests;

public sealed class TerrainTextureUploadCountersTests
{
    [Test]
    public void FrameTelemetryCapturesInitialResetAndStableFrameSnapshots()
    {
        using var telemetry = new FrameTelemetry();
        var counters = new TerrainTextureUploadCounters();
        telemetry.BindTerrainTextureUploadTelemetry(counters);

        Time.frameCount = 1;
        telemetry.ResetFrameTimers();
        Assert.That(telemetry.TerrainTextureUploadSnapshot?.IsAvailable, Is.False);
        Assert.That(telemetry.TerrainTextureUploadFrameDelta, Is.Null);

        counters.BeginGeneration();
        counters.RecordApply(2, 2, 4, 1);
        Time.frameCount = 2;
        telemetry.ResetFrameTimers();
        Assert.That(telemetry.TerrainTextureUploadSnapshot?.ApplyPayloadBytes, Is.EqualTo(16));
        Assert.That(telemetry.TerrainTextureUploadFrameDelta, Is.Null);

        Time.frameCount = 3;
        telemetry.ResetFrameTimers();
        Assert.That(telemetry.TerrainTextureUploadFrameDelta?.ApplyCalls, Is.Zero);
        Assert.That(telemetry.TerrainTextureUploadFrameDelta?.StartObservationFrameId, Is.EqualTo(1));
        Assert.That(telemetry.TerrainTextureUploadFrameDelta?.EndObservationFrameId, Is.EqualTo(2));

        counters.EndGeneration();
        Time.frameCount = 4;
        telemetry.ResetFrameTimers();
        Assert.That(telemetry.TerrainTextureUploadSnapshot?.IsAvailable, Is.False);
        Assert.That(telemetry.TerrainTextureUploadFrameDelta, Is.Null);
    }

    [Test]
    public void StartsUnavailableUntilTextureGenerationExists()
    {
        var counters = new TerrainTextureUploadCounters();

        Assert.That(counters.IsAvailable, Is.False);
        Assert.That(counters.HasSourceFrame, Is.False);
        Assert.That(counters.SourceFrameId, Is.EqualTo(-1));

        counters.BeginGeneration();

        Assert.That(counters.IsAvailable, Is.True);
        Assert.That(counters.Generation, Is.EqualTo(1));
        Assert.That(counters.HasSourceFrame, Is.False);
    }

    [Test]
    public void InvalidCallsDoNotMutateCountersOrSourceStamp()
    {
        var counters = new TerrainTextureUploadCounters();
        counters.BeginGeneration();

        Assert.Throws<ArgumentOutOfRangeException>(() => counters.RecordApply(0, 2, 4, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => counters.RecordCopyTexture(1, 2, 4, -1));

        Assert.That(counters.ApplyCalls, Is.Zero);
        Assert.That(counters.ApplyPayloadBytes, Is.Zero);
        Assert.That(counters.CopyTextureCalls, Is.Zero);
        Assert.That(counters.CopyTexturePayloadBytes, Is.Zero);
        Assert.That(counters.SourceFrameId, Is.EqualTo(-1));
        Assert.That(counters.HasSourceFrame, Is.False);
    }

    [Test]
    public void ApplyUsesAllocatedDimensionsWhileCopyUsesRectangleDimensions()
    {
        var counters = new TerrainTextureUploadCounters();
        counters.BeginGeneration();

        counters.RecordApply(64, 256, 8, 42);
        counters.RecordApply(64, 128, 4, 42);
        counters.RecordCopyTexture(7, 3, 16, 43);
        counters.RecordCopyTexture(7, 3, 16, 43);

        Assert.That(counters.ApplyCalls, Is.EqualTo(2));
        Assert.That(counters.ApplyPayloadBytes, Is.EqualTo((64L * 256 * 8) + (64L * 128 * 4)));
        Assert.That(counters.CopyTextureCalls, Is.EqualTo(2));
        Assert.That(counters.CopyTexturePayloadBytes, Is.EqualTo(2L * 7 * 3 * 16));
        Assert.That(counters.SourceFrameId, Is.EqualTo(43));
        Assert.That(counters.HasSourceFrame, Is.True);
    }

    [Test]
    public void CountsRemainCumulativeAcrossTextureGenerations()
    {
        var counters = new TerrainTextureUploadCounters();
        counters.BeginGeneration();
        counters.RecordApply(2, 3, 4, 5);
        counters.EndGeneration();
        TerrainTextureUploadSnapshot ended = counters.Capture(6);
        counters.BeginGeneration();
        TerrainTextureUploadSnapshot reset = counters.Capture(7);
        counters.RecordCopyTexture(1, 2, 8, 9);

        Assert.That(counters.Generation, Is.EqualTo(2));
        Assert.That(counters.ApplyCalls, Is.EqualTo(1));
        Assert.That(counters.CopyTextureCalls, Is.EqualTo(1));
        Assert.That(counters.SourceFrameId, Is.EqualTo(9));
        Assert.That(reset.HasSourceFrame, Is.False);
        Assert.That(reset.SourceFrameId, Is.EqualTo(-1));
        Assert.That(TerrainTextureUploadCounters.TryGetDelta(ended, reset, out _), Is.False);
    }

    [Test]
    public void RejectsInvalidPayloadDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TerrainTextureUploadCounters.PayloadBytes(-1, 2, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => TerrainTextureUploadCounters.PayloadBytes(0, 2, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => TerrainTextureUploadCounters.PayloadBytes(1, 2, 0));
    }

    [Test]
    public void FrameDeltaAcceptsValidObservationEndpointsIncludingStableZeroDelta()
    {
        var counters = new TerrainTextureUploadCounters();
        counters.BeginGeneration();
        counters.RecordApply(2, 3, 4, 5);
        TerrainTextureUploadSnapshot previous = counters.Capture(10);
        TerrainTextureUploadSnapshot current = counters.Capture(11);

        Assert.That(TerrainTextureUploadCounters.TryGetDelta(previous, current, out TerrainTextureUploadDelta delta), Is.True);
        Assert.That(delta.ApplyCalls, Is.Zero);
        Assert.That(delta.StartObservationFrameId, Is.EqualTo(10));
        Assert.That(delta.EndObservationFrameId, Is.EqualTo(11));
        Assert.That(previous.SourceFrameId, Is.EqualTo(5));
        Assert.That(current.ObservationFrameId, Is.EqualTo(11));
    }

    [Test]
    public void FrameDeltaRejectsStaleOrUnavailableObservationEndpoints()
    {
        var counters = new TerrainTextureUploadCounters();
        counters.BeginGeneration();
        TerrainTextureUploadSnapshot current = counters.Capture(20);
        TerrainTextureUploadSnapshot stale = counters.Capture(20);
        TerrainTextureUploadSnapshot gap = counters.Capture(22);
        TerrainTextureUploadSnapshot futureSource = new(
            current.Generation, current.ApplyCalls, current.ApplyPayloadBytes,
            current.CopyTextureCalls, current.CopyTexturePayloadBytes,
            SourceFrameId: 21, HasSourceFrame: true, IsAvailable: true, ObservationFrameId: 20);
        counters.EndGeneration();
        TerrainTextureUploadSnapshot disposed = counters.Capture(21);

        Assert.That(TerrainTextureUploadCounters.TryGetDelta(current, stale, out _), Is.False);
        Assert.That(TerrainTextureUploadCounters.TryGetDelta(current, gap, out _), Is.False);
        Assert.That(TerrainTextureUploadCounters.TryGetDelta(current, futureSource, out _), Is.False);
        Assert.That(TerrainTextureUploadCounters.TryGetDelta(current, disposed, out _), Is.False);
    }

    [Test]
    public void FrameTelemetryReleaseMakesDirectCaptureUnavailable()
    {
        var telemetry = new FrameTelemetry();
        var counters = new TerrainTextureUploadCounters();
        counters.BeginGeneration();
        telemetry.BindTerrainTextureUploadTelemetry(counters);
        Assert.That(telemetry.CaptureTerrainTextureUploadSnapshot(30)?.IsAvailable, Is.True);

        telemetry.Dispose();

        Assert.That(telemetry.CaptureTerrainTextureUploadSnapshot(31), Is.Null);
        Assert.That(telemetry.TerrainTextureUploadSnapshot, Is.Null);
        Assert.That(telemetry.TerrainTextureUploadFrameDelta, Is.Null);
    }

    [Test]
    public void ExportFieldsRoundTripThroughStrictCaptureSchema()
    {
        var counters = new TerrainTextureUploadCounters();
        counters.BeginGeneration();
        counters.RecordApply(64, 128, 4, 40);
        counters.RecordCopyTexture(7, 3, 8, 40);
        TerrainTextureUploadSnapshot previous = counters.Capture(40);
        counters.RecordApply(64, 128, 4, 41);
        TerrainTextureUploadSnapshot current = counters.Capture(41);
        Dictionary<string, object?> cumulative = TerrainTextureUploadCaptureFields.Cumulative(current);
        Dictionary<string, object?> frameCounters = TerrainTextureUploadCaptureFields.FrameCounters(current, previous);
        Dictionary<string, object?> observation = TerrainTextureUploadCaptureFields.Observation(current);
        cumulative.Add("terrainRebuilds", 0L);
        cumulative.Add("terrainFullPopulates", 0L);
        cumulative.Add("terrainDirtyPatches", 0L);
        cumulative.Add("terrainChunkLoads", 0L);
        cumulative.Add("lightingDynamicSolves", 0L);
        cumulative.Add("lightingDynamicTraces", 0L);
        cumulative.Add("lightingAtlasScrolls", 0L);
        frameCounters.Add("terrainUploadCalls", null);
        frameCounters.Add("terrainUploadBytes", null);
        frameCounters.Add("terrainAtlasUploadCalls", null);
        frameCounters.Add("terrainAtlasUploadBytes", null);
        frameCounters.Add("lightingFieldRebuilds", 0L);
        frameCounters.Add("lightingStaticSolves", 0L);
        var capture = new
        {
            schemaVersion = 1,
            harnessVersion = "1",
            captureId = "source-linked-upload-fields",
            capturedAtUtc = "2026-09-28T00:00:00Z",
            manifest = new { scenarioId = "observational", capturePhase = "previous-frame" },
            frames = new[]
            {
                new
                {
                    frameId = 41,
                    counterGeneration = 0,
                    counterResetObserved = false,
                    cumulative,
                    frameCounters,
                    terrainCellDataUpload = observation,
                },
            },
        };

        FrameCapture parsed = CaptureJson.Parse(JsonSerializer.Serialize(capture));
        FrameSample sample = parsed.Frames!.Single()!;
        Assert.That(sample.Cumulative?.TerrainCellDataApplyCalls, Is.EqualTo(2));
        Assert.That(sample.Cumulative?.TerrainCellDataApplyPayloadBytes, Is.EqualTo(2L * 64 * 128 * 4));
        Assert.That(sample.Cumulative?.TerrainCellDataCopyTextureCalls, Is.EqualTo(1));
        Assert.That(sample.Cumulative?.TerrainCellDataCopyTexturePayloadBytes, Is.EqualTo(7L * 3 * 8));
        Assert.That(sample.FrameCounters?.TerrainCellDataApplyCalls, Is.EqualTo(1));
        Assert.That(sample.FrameCounters?.TerrainCellDataUploadFrameDeltaValid, Is.True);
        Assert.That(sample.TerrainCellDataUpload?.SourceFrameId, Is.EqualTo(41));
        Assert.That(sample.TerrainCellDataUpload?.ObservationFrameId, Is.EqualTo(41));
        Assert.That(sample.FrameCounters?.TerrainUploadCalls, Is.Null);
        Assert.That(sample.FrameCounters?.TerrainAtlasUploadBytes, Is.Null);
        Assert.That(CaptureJson.Parse(CaptureJson.Write(parsed)).Frames!.Single()!.TerrainCellDataUpload?.Generation, Is.EqualTo(1));
    }

    [Test]
    public void UnavailableExportFieldsStayNullInsteadOfClaimingZero()
    {
        Dictionary<string, object?> cumulative = TerrainTextureUploadCaptureFields.Cumulative(null);
        Dictionary<string, object?> frameCounters = TerrainTextureUploadCaptureFields.FrameCounters(null, null);

        Assert.That(cumulative["terrainCellDataApplyCalls"], Is.Null);
        Assert.That(cumulative["terrainCellDataCopyTexturePayloadBytes"], Is.Null);
        Assert.That(frameCounters["terrainCellDataApplyCalls"], Is.Null);
        Assert.That(frameCounters["terrainCellDataUploadFrameDeltaValid"], Is.False);
    }
}
