#nullable enable

using Kern.Core.Bootstrap.WorldLighting;
using Kern.Core.Interfaces.WorldLighting;
using Kern.World.Lighting;
using Kern.World.Terrain;
using NUnit.Framework;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
using UnityEngine.Rendering;
using VContainer;

namespace Kern.Tests.World.Lighting;

[TestFixture]
public sealed class TerrainLightingExchangeTests
{
    [Test]
    public void GameScopeHasExactlyOneSingletonExchangeBinding()
    {
        string projectRoot = FindProjectRoot();
        string[] scopeSources = Directory.GetFiles(
            Path.Combine(projectRoot, "Assets", "Scripts", "Core", "Bootstrap", "Scopes"),
            "*.cs",
            SearchOption.AllDirectories);
        int singletonRegistrations = 0;
        int interfaceBindings = 0;
        foreach (string sourcePath in scopeSources)
        {
            string source = File.ReadAllText(sourcePath);
            singletonRegistrations += Count(source, "Register<TerrainLightingExchange>(Lifetime.Singleton)");
            interfaceBindings += Count(source, ".As<ITerrainLightingExchange>()");
        }

        Assert.That(singletonRegistrations, Is.EqualTo(1));
        Assert.That(interfaceBindings, Is.EqualTo(1));
    }

    [Test]
    public void ContractsAssemblyReferencesNoKernImplementationAssembly()
    {
        string projectRoot = FindProjectRoot();
        string asmdefPath = Path.Combine(
            projectRoot, "Assets", "Scripts", "Core", "Interfaces", "Contracts", "Kern.Contracts.asmdef");
        using JsonDocument assemblyDefinition = JsonDocument.Parse(File.ReadAllText(asmdefPath));
        JsonElement references = assemblyDefinition.RootElement.GetProperty("references");

        foreach (JsonElement reference in references.EnumerateArray())
        {
            string name = reference.GetString() ?? string.Empty;
            Assert.That(name.StartsWith("Kern.", System.StringComparison.Ordinal), Is.False,
                $"Kern.Contracts references implementation assembly '{name}'.");
        }
    }

    [Test]
    public void VContainerResolvesOneExchangeInstanceForConcreteAndContractTypes()
    {
        var builder = new ContainerBuilder();
        builder.Register<TerrainLightingExchange>(Lifetime.Singleton)
            .As<ITerrainLightingExchange>();
        using var resolver = builder.Build();

        ITerrainLightingExchange firstResolve = resolver.Resolve<ITerrainLightingExchange>();
        ITerrainLightingExchange secondResolve = resolver.Resolve<ITerrainLightingExchange>();

        Assert.That(firstResolve, Is.SameAs(secondResolve));
    }

    [Test]
    public void TerrainChangePublicationFollowsCommitAndLightingConsumesBeforeSolve()
    {
        string projectRoot = FindProjectRoot();
        string terrainPath = Path.Combine(
            projectRoot, "Assets", "Scripts", "World", "Terrain", "Core", "TerrainRenderer.cs");
        string lightingPath = Path.Combine(
            projectRoot, "Assets", "Scripts", "World", "Lighting", "Core", "LightingEngine.cs");
        string exchangeStatePath = Path.Combine(
            projectRoot, "Assets", "Scripts", "World", "Lighting", "Core", "LightingTerrainExchangeState.cs");
        string publisherPath = Path.Combine(
            projectRoot, "Assets", "Scripts", "World", "Terrain", "Core", "TerrainLightingFramePublisher.cs");
        string terrainSource = File.ReadAllText(terrainPath);
        string lightingSource = File.ReadAllText(lightingPath);
        string exchangeStateSource = File.ReadAllText(exchangeStatePath);
        string publisherSource = File.ReadAllText(publisherPath);

        Assert.That(terrainSource.Contains(".InvalidateRegion(", System.StringComparison.Ordinal), Is.False);
        Assert.That(terrainSource.Contains(".InvalidateStaticCache(", System.StringComparison.Ordinal), Is.False);
        Assert.That(terrainSource.IndexOf(
                "LightingTerrainRequirements lightingRequirements = LightingFramePublisher.ReadRequirements();",
                System.StringComparison.Ordinal),
            Is.LessThan(terrainSource.IndexOf("_planner.Plan(", System.StringComparison.Ordinal)));
        Assert.That(terrainSource.IndexOf("if (!_window.Process(", System.StringComparison.Ordinal),
            Is.LessThan(terrainSource.IndexOf("LightingFramePublisher.PublishCommittedChanges(", System.StringComparison.Ordinal)));
        Assert.That(terrainSource.IndexOf("LightingFramePublisher.PublishCommittedChanges(", System.StringComparison.Ordinal),
            Is.LessThan(terrainSource.IndexOf("PublishTerrainFrameDemand(framePlan, holdingView: false)", System.StringComparison.Ordinal)));
        Assert.That(terrainSource.IndexOf(
                "bool hasLightingOutput = LightingFramePublisher.TryReadLightingOutput(out lightingOutput);",
                System.StringComparison.Ordinal),
            Is.LessThan(terrainSource.IndexOf("LightingFramePublisher.ValidateLightingOutput(", System.StringComparison.Ordinal)));
        Assert.That(publisherSource.IndexOf(
                "if (!hasOutput || output.WorldGeneration != _worldGeneration)",
                System.StringComparison.Ordinal),
            Is.LessThan(publisherSource.IndexOf("window.Driver.Materials.ValidateLightingBinding(output);", System.StringComparison.Ordinal)));
        Assert.That(publisherSource.IndexOf("exchange.PublishTerrainFrame(frame);", System.StringComparison.Ordinal), Is.GreaterThanOrEqualTo(0));
        int stageChanges = lightingSource.IndexOf("StageTerrainChanges(", System.StringComparison.Ordinal);
        int coordinatorAfterStaging = lightingSource.IndexOf(
            "UpdateLightingCoordinator(frame)",
            stageChanges,
            System.StringComparison.Ordinal);
        int acknowledgeChanges = lightingSource.IndexOf("AcknowledgeStagedChanges(", System.StringComparison.Ordinal);
        Assert.That(stageChanges, Is.GreaterThanOrEqualTo(0));
        Assert.That(coordinatorAfterStaging, Is.GreaterThan(stageChanges));
        Assert.That(acknowledgeChanges, Is.GreaterThan(coordinatorAfterStaging));
        Assert.That(acknowledgeChanges,
            Is.LessThan(lightingSource.IndexOf("CaptureBudgetViolationIfNeeded();", System.StringComparison.Ordinal)));
        Assert.That(lightingSource.IndexOf("CaptureBudgetViolationIfNeeded();", System.StringComparison.Ordinal),
            Is.LessThan(lightingSource.IndexOf("LightingOutputState.Published", System.StringComparison.Ordinal)));
        Assert.That(exchangeStateSource.Contains("_changePump.Stage(exchange, worldGeneration, applyChange)", System.StringComparison.Ordinal), Is.True);
        Assert.That(exchangeStateSource.Contains("_changePump.AcknowledgeStaged(exchange)", System.StringComparison.Ordinal), Is.True);
        Assert.That(lightingSource.IndexOf("private void UpdateLightingCoordinator(", System.StringComparison.Ordinal),
            Is.LessThan(lightingSource.IndexOf("Composition.UpdateCoordinator.Update(", System.StringComparison.Ordinal)));
        Assert.That(lightingSource.Contains("[DefaultExecutionOrder(200)]", System.StringComparison.Ordinal), Is.True);
    }

    [Test]
    public void TerrainWindowFacadePreservesCompletionValidationAndPublicationOrder()
    {
        string projectRoot = FindProjectRoot();
        string terrainWindowPath = Path.Combine(
            projectRoot, "Assets", "Scripts", "World", "Terrain", "Gpu", "TerrainWindow.cs");
        string lifecyclePath = Path.Combine(
            projectRoot, "Assets", "Scripts", "World", "Terrain", "Gpu", "TerrainWindow.BuildLifecycle.cs");
        string schedulingPath = Path.Combine(
            projectRoot, "Assets", "Scripts", "World", "Terrain", "Gpu", "TerrainWindowBuildRequestScheduler.cs");
        string terrainRendererPath = Path.Combine(
            projectRoot, "Assets", "Scripts", "World", "Terrain", "Core", "TerrainRenderer.cs");
        string facade = File.ReadAllText(terrainWindowPath);
        string lifecycle = File.ReadAllText(lifecyclePath);
        string scheduling = File.ReadAllText(schedulingPath);
        string renderer = File.ReadAllText(terrainRendererPath);

        Assert.That(facade.Contains("public sealed class TerrainWindow : IDisposable", System.StringComparison.Ordinal), Is.True);
        Assert.That(facade.Contains("TryPublishCompleted(services, out failure)", System.StringComparison.Ordinal), Is.True);
        Assert.That(facade.Contains("_lifecycle.Process(", System.StringComparison.Ordinal), Is.True);
        Assert.That(facade.Contains("_lifecycle.Commit()", System.StringComparison.Ordinal), Is.True);
        Assert.That(facade.Contains("partial class TerrainWindow", System.StringComparison.Ordinal), Is.False);

        int complete = lifecycle.IndexOf("private bool Complete(", System.StringComparison.Ordinal);
        int generationGuard = lifecycle.IndexOf("request.WorldGeneration != _worldGeneration", complete, System.StringComparison.Ordinal);
        int continueBuild = lifecycle.IndexOf("_driver.TryContinueBuild(", complete, System.StringComparison.Ordinal);
        int driverPublish = lifecycle.IndexOf("_driver.Publish(context, request, result, latencySeconds * 1000f)", complete, System.StringComparison.Ordinal);
        int viewPublish = lifecycle.IndexOf("_publishedView.Publish(request)", complete, System.StringComparison.Ordinal);
        int journalPublish = lifecycle.IndexOf("_changes.AddPublishedChangedRegion(", complete, System.StringComparison.Ordinal);
        Assert.That(complete, Is.GreaterThanOrEqualTo(0));
        Assert.That(generationGuard, Is.GreaterThan(complete));
        Assert.That(continueBuild, Is.GreaterThan(generationGuard));
        Assert.That(driverPublish, Is.GreaterThan(continueBuild));
        Assert.That(viewPublish, Is.GreaterThan(driverPublish));
        Assert.That(journalPublish, Is.GreaterThan(viewPublish));
        Assert.That(lifecycle.Contains("_requestScheduler.BuildStartElapsedSeconds", System.StringComparison.Ordinal), Is.True);
        Assert.That(lifecycle.Contains("_changes.RestoreOldestChangeTimestamp(_buildOldestChangeTimestamp)", System.StringComparison.Ordinal), Is.True);
        Assert.That(lifecycle.Contains("BuildState = TerrainBuildState.WaitingForData;\n                return false;", System.StringComparison.Ordinal), Is.True);

        int schedulingIntent = lifecycle.IndexOf("new TerrainBuildSchedulingIntent(", System.StringComparison.Ordinal);
        int scheduleCall = lifecycle.IndexOf("_requestScheduler.Schedule(", schedulingIntent, System.StringComparison.Ordinal);
        int beginBuild = scheduling.IndexOf("_driver.TryBeginBuild(", System.StringComparison.Ordinal);
        int prepare = scheduling.IndexOf("_driver.Prepare(", System.StringComparison.Ordinal);
        int clearDirty = scheduling.IndexOf("_changes.ClearDirty()", prepare, System.StringComparison.Ordinal);
        int clearRefresh = scheduling.IndexOf("_changes.NeedsRefresh = false", clearDirty, System.StringComparison.Ordinal);
        int startBuild = scheduling.IndexOf("_builds.Start(request)", clearDirty, System.StringComparison.Ordinal);
        Assert.That(schedulingIntent, Is.GreaterThanOrEqualTo(0));
        Assert.That(scheduleCall, Is.GreaterThan(schedulingIntent));
        Assert.That(scheduling.Contains("internal readonly struct TerrainBuildSchedulingIntent", System.StringComparison.Ordinal), Is.True);
        Assert.That(scheduling.Contains("HasHeldCompletion { get; }", System.StringComparison.Ordinal), Is.True);
        Assert.That(beginBuild, Is.GreaterThanOrEqualTo(0));
        Assert.That(prepare, Is.GreaterThan(beginBuild));
        Assert.That(clearDirty, Is.GreaterThan(prepare));
        Assert.That(scheduling.IndexOf("_changes.SwapBuildChanges()", clearDirty, System.StringComparison.Ordinal), Is.GreaterThan(clearDirty));
        Assert.That(clearRefresh, Is.GreaterThan(clearDirty));
        Assert.That(startBuild, Is.GreaterThan(clearDirty));

        int scheduledDriverPublish = scheduling.IndexOf("_driver.Publish(context, request, result, latencySeconds * 1000f)", System.StringComparison.Ordinal);
        Assert.That(scheduledDriverPublish, Is.EqualTo(-1), "Only completion lifecycle may publish driver output.");

        int publishCompleted = renderer.IndexOf("_window.TryPublishCompleted(", System.StringComparison.Ordinal);
        int commit = renderer.IndexOf("_window.Commit()", publishCompleted, System.StringComparison.Ordinal);
        int schedule = renderer.IndexOf("_window.Process(", commit, System.StringComparison.Ordinal);
        Assert.That(publishCompleted, Is.GreaterThanOrEqualTo(0));
        Assert.That(commit, Is.GreaterThan(publishCompleted));
        Assert.That(schedule, Is.GreaterThan(commit));
    }

    [Test]
    public void RequirementsRevisionAdvancesOnlyWhenPaddingChanges()
    {
        var exchange = new TerrainLightingExchange();

        exchange.PublishLightingRequirements(new LightingTerrainRequirements(1, 4, 8));
        exchange.PublishLightingRequirements(new LightingTerrainRequirements(1, 4, 8));
        Assert.Throws<System.InvalidOperationException>(() =>
            exchange.PublishLightingRequirements(new LightingTerrainRequirements(2, 4, 8)));
        Assert.Throws<System.InvalidOperationException>(() =>
            exchange.PublishLightingRequirements(new LightingTerrainRequirements(1, 5, 8)));

        exchange.PublishLightingRequirements(new LightingTerrainRequirements(2, 5, 8));
        Assert.That(exchange.TryReadLightingRequirements(out LightingTerrainRequirements value), Is.True);
        Assert.That(value, Is.EqualTo(new LightingTerrainRequirements(2, 5, 8)));
    }

    [Test]
    public void TerrainChangeRejectsGapsAndGenerationMismatchAndRetainsUnacknowledgedValue()
    {
        var exchange = NewExchange();
        exchange.PublishTerrainChange(Reset(1, 1));
        exchange.PublishTerrainChange(Region(1, 2, new RectInt(3, 5, 7, 9)));

        Assert.Throws<System.InvalidOperationException>(() => exchange.PublishTerrainChange(Region(1, 4)));
        Assert.Throws<System.InvalidOperationException>(() => exchange.PublishTerrainChange(Region(2, 3)));
        Assert.That(exchange.TryReadNextTerrainChange(1, 1, out TerrainLightingChange firstRead), Is.True);
        Assert.That(firstRead.Sequence, Is.EqualTo(2));

        // A failed consumer transfer has no acknowledgement; retry returns the same record.
        Assert.That(exchange.TryReadNextTerrainChange(1, 1, out TerrainLightingChange retry), Is.True);
        Assert.That(retry, Is.EqualTo(firstRead));
        Assert.Throws<System.InvalidOperationException>(() => exchange.AcknowledgeTerrainChanges(1, 2));

        exchange.AcknowledgeTerrainChanges(1, 1);
        exchange.AcknowledgeTerrainChanges(1, 2);
        Assert.That(exchange.TryReadNextTerrainChange(1, 2, out _), Is.False);
    }

    [Test]
    public void FullResetSupersedesPendingRegionsAndStartsNewGenerationAtSequenceOne()
    {
        var exchange = NewExchange();
        exchange.PublishTerrainChange(Reset(1, 1));
        exchange.PublishTerrainChange(Region(1, 2));
        exchange.PublishTerrainChange(Reset(1, 3));

        Assert.That(exchange.TryReadNextTerrainChange(1, 1, out TerrainLightingChange reset), Is.True);
        Assert.That(reset.Sequence, Is.EqualTo(3));
        exchange.AcknowledgeTerrainChanges(1, 3);

        exchange.PublishTerrainChange(Reset(2, 1));
        Assert.Throws<System.InvalidOperationException>(() => exchange.TryReadNextTerrainChange(1, 0, out _));
        Assert.That(exchange.TryReadNextTerrainChange(2, 0, out TerrainLightingChange nextWorld), Is.True);
        Assert.That(nextWorld.Sequence, Is.EqualTo(1));
    }

    [Test]
    public void FramePublicationRequiresContiguousSequenceAndActiveGeneration()
    {
        var exchange = NewExchange();
        exchange.PublishTerrainChange(Reset(4, 1));
        TerrainLightingFrameSnapshot frame = Frame(4, 1);

        Assert.Throws<System.InvalidOperationException>(() => exchange.PublishTerrainFrame(Frame(4, 2)));
        Assert.Throws<System.InvalidOperationException>(() => exchange.PublishTerrainFrame(Frame(5, 1)));
        exchange.PublishTerrainFrame(frame);
        Assert.That(exchange.TryReadLatestTerrainFrame(0, out TerrainLightingFrameSnapshot copy), Is.True);

        // Snapshot is a value; changing a caller-owned copy cannot mutate exchange state.
        copy = Frame(4, 99);
        Assert.That(exchange.TryReadLatestTerrainFrame(0, out TerrainLightingFrameSnapshot reread), Is.True);
        Assert.That(reread.FrameSequence, Is.EqualTo(1));
        exchange.AcknowledgeTerrainFrame(1);
        Assert.Throws<System.InvalidOperationException>(() => exchange.AcknowledgeTerrainFrame(1));
    }

    [Test]
    public void FrameSequenceRestartsPerGenerationWithoutReplayingOldAcknowledgement()
    {
        var exchange = NewExchange();
        exchange.PublishTerrainChange(Reset(4, 1));
        exchange.PublishTerrainFrame(Frame(4, 1));
        exchange.AcknowledgeTerrainFrame(1);
        exchange.PublishTerrainChange(Reset(5, 1));
        exchange.PublishTerrainFrame(Frame(5, 1));

        Assert.That(exchange.TryReadLatestTerrainFrame(1, out TerrainLightingFrameSnapshot current), Is.True);
        Assert.That(current.WorldGeneration, Is.EqualTo(5));
        Assert.That(current.FrameSequence, Is.EqualTo(1));
        exchange.AcknowledgeTerrainFrame(1);
        Assert.That(exchange.TryReadLatestTerrainFrame(0, out _), Is.False);
    }

    [Test]
    public void MalformedRectanglesAndUndefinedEnumsAreRejected()
    {
        var exchange = NewExchange();
        exchange.PublishTerrainChange(Reset(1, 1));
        Assert.Throws<System.ArgumentException>(() => exchange.PublishTerrainChange(
            Region(1, 2, new RectInt(int.MaxValue, 0, 1, 1))));
        Assert.Throws<System.ArgumentException>(() => exchange.PublishTerrainChange(
            new TerrainLightingChange(1, 2, 1, (TerrainLightingChangeKind)88,
                TerrainLightingChannels.All, new RectInt(0, 0, 1, 1), TerrainLightingFullResetReason.WorldReplaced)));
        Assert.Throws<System.ArgumentException>(() => exchange.PublishTerrainFrame(
            new TerrainLightingFrameSnapshot(
                1,
                1,
                TerrainLightingFrameState.Ready,
                1,
                new RectInt(0, 0, 0, 4),
                new RectInt(0, 0, 4, 4),
                new Camera(),
                new TestContributor())));
    }

    [Test]
    public void OutputSnapshotRejectsGenerationRegressionAndCallerCopyDoesNotMutatePublishedValue()
    {
        var exchange = NewExchange();
        exchange.PublishTerrainChange(Reset(3, 1));
        var output = new LightingOutputSnapshot(1, 3, LightingOutputState.Published, new RectInt(-4, 8, 20, 30));
        exchange.PublishLightingOutput(output);
        Assert.Throws<System.InvalidOperationException>(() => exchange.PublishLightingOutput(output));
        Assert.Throws<System.InvalidOperationException>(() => exchange.PublishLightingOutput(
            new LightingOutputSnapshot(2, 2, LightingOutputState.Published, output.WorldRectCells)));
        Assert.That(exchange.TryReadLightingOutput(out LightingOutputSnapshot published), Is.True);
        published = new LightingOutputSnapshot(
            published.OutputGeneration,
            published.WorldGeneration,
            published.State,
            new RectInt(0, 0, 1, 1));
        Assert.That(exchange.TryReadLightingOutput(out LightingOutputSnapshot reread), Is.True);
        Assert.That(reread.WorldRectCells, Is.EqualTo(new RectInt(-4, 8, 20, 30)));
    }

    [Test]
    public void ChangePumpTransfersEachRegionAndAcknowledgesOnlyAfterTransfer()
    {
        var exchange = NewExchange();
        exchange.PublishTerrainChange(Reset(1, 1));
        exchange.PublishTerrainChange(Region(1, 2, new RectInt(-8, 3, 5, 6)));
        exchange.PublishTerrainChange(Region(1, 3, new RectInt(20, -4, 2, 9)));
        var pump = new TerrainLightingChangePump();
        var transferred = new List<TerrainLightingChange>();

        pump.Stage(exchange, 1, change => transferred.Add(change));

        Assert.That(transferred, Has.Count.EqualTo(3));
        Assert.That(transferred[1].Region, Is.EqualTo(new RectInt(-8, 3, 5, 6)));
        Assert.That(transferred[2].Region, Is.EqualTo(new RectInt(20, -4, 2, 9)));
        Assert.That(exchange.TryReadNextTerrainChange(1, 0, out _), Is.True,
            "Staged changes remain unacknowledged until the coordinator succeeds.");
        pump.AcknowledgeStaged(exchange);
        Assert.That(exchange.TryReadNextTerrainChange(1, 3, out _), Is.False);
    }

    [Test]
    public void ChangePumpReplaysUnacknowledgedRecordAfterFailedTransfer()
    {
        var exchange = NewExchange();
        exchange.PublishTerrainChange(Reset(1, 1));
        exchange.PublishTerrainChange(Region(1, 2));
        var pump = new TerrainLightingChangePump();
        int transferAttempts = 0;
        Assert.Throws<System.InvalidOperationException>(() =>
            pump.Stage(exchange, 1, change =>
            {
                if (++transferAttempts == 2)
                {
                    throw new System.InvalidOperationException("transfer failed");
                }
            }));
        Assert.That(exchange.TryReadNextTerrainChange(1, 1, out TerrainLightingChange retained), Is.True);
        Assert.That(retained.Sequence, Is.EqualTo(2));

        var replayed = new List<TerrainLightingChange>();
        pump.Stage(exchange, 1, change => replayed.Add(change));
        pump.AcknowledgeStaged(exchange);

        Assert.That(replayed, Has.Count.EqualTo(1));
        Assert.That(replayed[0].Sequence, Is.EqualTo(2));
        Assert.That(exchange.TryReadNextTerrainChange(1, 2, out _), Is.False);
    }

    [Test]
    public void ChangePumpTransfersNewGenerationResetAndDoesNotReuseOldSequenceWatermark()
    {
        var exchange = NewExchange();
        exchange.PublishTerrainChange(Reset(1, 1));
        var pump = new TerrainLightingChangePump();
        pump.Stage(exchange, 1, _ => { });
        pump.AcknowledgeStaged(exchange);
        exchange.PublishTerrainChange(Reset(2, 1));
        var transferred = new List<TerrainLightingChange>();

        pump.Stage(exchange, 2, change => transferred.Add(change));
        pump.AcknowledgeStaged(exchange);

        Assert.That(transferred, Has.Count.EqualTo(1));
        Assert.That(transferred[0].WorldGeneration, Is.EqualTo(2));
        Assert.That(transferred[0].Sequence, Is.EqualTo(1));
    }

    [Test]
    public void ChangeApplierKeepsStableRegionEditsAndDiscardsOnlyOutsideStableRegion()
    {
        var state = new LightingRuntimeState
        {
            FieldDirty = false,
            LastVisibleRegion = new Vector4(0, 0, 10, 10),
        };

        bool stableEdgeQueued = TerrainLightingChangeApplier.Apply(
            Region(1, 1, new RectInt(10, 4, 1, 1)), state);
        bool outsideQueued = TerrainLightingChangeApplier.Apply(
            Region(1, 2, new RectInt(11, 4, 1, 1)), state);

        Assert.That(stableEdgeQueued, Is.True);
        Assert.That(outsideQueued, Is.False);
        Assert.That(state.ActivatePendingRegionIfVisible(new RectInt(0, 0, 10, 10)), Is.False);
        Assert.That(state.FieldDirty, Is.False);
        Assert.That(state.ActivatePendingRegionIfVisible(new RectInt(10, 4, 1, 1)), Is.True);
    }

    [Test]
    public void WorldResetClearsOldGenerationRegionsAndForcesFullFieldRebuild()
    {
        var state = new LightingRuntimeState
        {
            FieldDirty = false,
            LastVisibleRegion = new Vector4(0, 0, 20, 20),
        };
        state.QueueRegionInvalidation(new RectInt(2, 2, 3, 3));

        TerrainLightingChangeApplier.Apply(Reset(2, 1), state);

        Assert.That(state.FieldDirty, Is.True);
        Assert.That(state.ActivatePendingRegionIfVisible(new RectInt(0, 0, 20, 20)), Is.False);
    }

    [Test]
    public void OutOfStableInvalidationIsDiscardedBeforeForcedFullReanchor()
    {
        var state = new LightingRuntimeState
        {
            FieldDirty = false,
            LastVisibleRegion = new Vector4(0, 0, 20, 20),
        };
        bool queued = TerrainLightingChangeApplier.Apply(
            Region(1, 1, new RectInt(21, 4, 1, 1)), state);

        LightingRegionInvalidationPolicy.OnRegionChanged(
            state,
            regionChanged: true,
            canReuseStaticAtlas: false,
            new Vector4(32, 0, 64, 64));

        Assert.That(queued, Is.False);
        Assert.That(state.FieldDirty, Is.False);
        Assert.That(state.ActivatePendingRegionIfVisible(new RectInt(32, 0, 64, 64)), Is.False);
    }

    [Test]
    public void FramePumpSkipsAbsentAndAlreadyAcknowledgedSnapshots()
    {
        var exchange = NewExchange();
        var pump = new TerrainLightingFramePump();
        int calls = 0;

        Assert.That(pump.ProcessLatest(exchange, _ => { calls++; return true; }), Is.False);
        exchange.PublishTerrainChange(Reset(1, 1));
        exchange.PublishTerrainFrame(Frame(1, 1));
        Assert.That(pump.ProcessLatest(exchange, _ => { calls++; return true; }), Is.True);
        Assert.That(pump.ProcessLatest(exchange, _ => { calls++; return true; }), Is.False);
        Assert.That(new TerrainLightingFramePump().ProcessLatest(exchange, _ => { calls++; return true; }), Is.False,
            "A newly constructed consumer must not replay a frame acknowledged by the exchange.");
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void FramePumpInvokesTickForEachReadyFrameAndPreservesHeldViewSnapshot()
    {
        var exchange = NewExchange();
        exchange.PublishTerrainChange(Reset(1, 1));
        exchange.PublishTerrainFrame(Frame(1, 1));
        var pump = new TerrainLightingFramePump();
        int calls = 0;
        Assert.That(pump.ProcessLatest(exchange, frame =>
        {
            Assert.That(frame.State, Is.EqualTo(TerrainLightingFrameState.Ready));
            calls++;
            return true;
        }), Is.True);

        TerrainLightingFrameSnapshot held = new(
            1,
            2,
            TerrainLightingFrameState.HoldingPublishedView,
            2,
            new RectInt(-12, -4, 8, 6),
            new RectInt(-20, -12, 24, 22),
            new Camera(),
            new TestContributor());
        exchange.PublishTerrainFrame(held);
        TerrainLightingFrameSnapshot processed = default;
        Assert.That(pump.ProcessLatest(exchange, frame =>
        {
            processed = frame;
            return true;
        }), Is.True);

        Assert.That(calls, Is.EqualTo(1));
        Assert.That(processed.State, Is.EqualTo(TerrainLightingFrameState.HoldingPublishedView));
        Assert.That(processed.CameraViewportCells, Is.EqualTo(held.CameraViewportCells));
    }

    [Test]
    public void FramePumpUsesExchangeWatermarkAfterWorldGenerationResetsSequence()
    {
        var exchange = NewExchange();
        var pump = new TerrainLightingFramePump();
        exchange.PublishTerrainChange(Reset(1, 1));
        for (ulong sequence = 1; sequence <= 128; sequence++)
        {
            exchange.PublishTerrainFrame(Frame(1, sequence));
        }

        ulong processedGeneration = 0;
        ulong processedSequence = 0;
        Assert.That(pump.ProcessLatest(exchange, frame =>
        {
            processedGeneration = frame.WorldGeneration;
            processedSequence = frame.FrameSequence;
            return true;
        }), Is.True);
        Assert.That(processedGeneration, Is.EqualTo(1));
        Assert.That(processedSequence, Is.EqualTo(128));

        exchange.PublishTerrainChange(Reset(2, 1));
        exchange.PublishTerrainFrame(Frame(2, 1));

        Assert.That(pump.ProcessLatest(exchange, frame =>
        {
            processedGeneration = frame.WorldGeneration;
            processedSequence = frame.FrameSequence;
            return true;
        }), Is.True);
        Assert.That(processedGeneration, Is.EqualTo(2));
        Assert.That(processedSequence, Is.EqualTo(1));
    }

    [Test]
    public void CommittedCameraShiftDuringTerrainBuildMovesStableLightingOutputRect()
    {
        var committedTerrainWindow = new RectInt(0, 0, 384, 192);
        var previousCameraViewport = new RectInt(40, 40, 40, 30);
        var previousLightingViewport = previousCameraViewport;
        Vector4 previousOutputRect = LightingRegionCalculator.GetStableLightingRegion(
            previousLightingViewport.x,
            previousLightingViewport.y,
            previousLightingViewport.width,
            previousLightingViewport.height,
            new Vector4(float.NaN, 0, 0, 0));
        var currentCameraViewport = new RectInt(150, 40, 40, 30);

        RectInt pendingBuildLightingViewport = TerrainLightingViewportPolicy.ResolveLightingViewport(
            currentCameraViewport,
            previousLightingViewport,
            holdingPublishedView: false);
        Assert.That(pendingBuildLightingViewport.x, Is.EqualTo(currentCameraViewport.x));

        Assert.That(TerrainLightingViewportPolicy.TrySelectCommittedView(
            holdingPublishedView: false,
            hasPublishedView: true,
            previousCameraViewport,
            previousLightingViewport,
            currentCameraViewport,
            pendingBuildLightingViewport,
            committedTerrainWindow,
            out RectInt publishedCameraViewport,
            out RectInt publishedLightingViewport), Is.True);
        Assert.That(publishedCameraViewport.x, Is.EqualTo(currentCameraViewport.x));
        Assert.That(publishedLightingViewport.x, Is.EqualTo(currentCameraViewport.x));

        Vector4 outputRect = LightingRegionCalculator.GetStableLightingRegion(
            publishedLightingViewport.x,
            publishedLightingViewport.y,
            publishedLightingViewport.width,
            publishedLightingViewport.height,
            previousOutputRect);
        Assert.That(outputRect.x, Is.Not.EqualTo(previousOutputRect.x));
        Assert.That(outputRect.x, Is.LessThanOrEqualTo(publishedCameraViewport.x));
        Assert.That(outputRect.x + outputRect.z,
            Is.GreaterThanOrEqualTo(publishedCameraViewport.xMax));
    }

    [Test]
    public void HeldOrUncoveredCameraCannotPublishPendingDestinationView()
    {
        var committedTerrainWindow = new RectInt(0, 0, 384, 192);
        var previousCameraViewport = new RectInt(40, 40, 40, 30);
        var previousLightingViewport = new RectInt(24, 24, 72, 62);
        var pendingDestinationViewport = new RectInt(500, 40, 40, 30);

        RectInt heldLightingViewport = TerrainLightingViewportPolicy.ResolveLightingViewport(
            pendingDestinationViewport,
            previousLightingViewport,
            holdingPublishedView: true);
        Assert.That(TerrainLightingViewportPolicy.TrySelectCommittedView(
            holdingPublishedView: true,
            hasPublishedView: true,
            previousCameraViewport,
            previousLightingViewport,
            pendingDestinationViewport,
            heldLightingViewport,
            committedTerrainWindow,
            out RectInt heldCamera,
            out RectInt heldLighting), Is.True);
        Assert.That(heldCamera.x, Is.EqualTo(previousCameraViewport.x));
        Assert.That(heldLighting.x, Is.EqualTo(previousLightingViewport.x));

        Assert.That(TerrainLightingViewportPolicy.TrySelectCommittedView(
            holdingPublishedView: false,
            hasPublishedView: true,
            previousCameraViewport,
            previousLightingViewport,
            pendingDestinationViewport,
            pendingDestinationViewport,
            committedTerrainWindow,
            out _,
            out _), Is.False);
    }

    [Test]
    public void TerrainPlannerAndPublisherUseCommittedViewportPolicy()
    {
        string projectRoot = FindProjectRoot();
        string viewportPath = Path.Combine(
            projectRoot, "Assets", "Scripts", "World", "Terrain", "Core", "TerrainViewportCalculator.cs");
        string publisherPath = Path.Combine(
            projectRoot, "Assets", "Scripts", "World", "Terrain", "Core", "TerrainLightingFramePublisher.cs");
        string viewportSource = File.ReadAllText(viewportPath);
        string publisherSource = File.ReadAllText(publisherPath);

        Assert.That(viewportSource.Contains(
            "TerrainLightingViewportPolicy.ResolveLightingViewport(",
            System.StringComparison.Ordinal), Is.True);
        Assert.That(publisherSource.Contains(
            "TerrainLightingViewportPolicy.TrySelectCommittedView(",
            System.StringComparison.Ordinal), Is.True);
        Assert.That(publisherSource.Contains("_lastCameraViewport = cameraViewport;", System.StringComparison.Ordinal), Is.True);
        Assert.That(publisherSource.Contains("cameraViewport,\n            lightingViewport,", System.StringComparison.Ordinal), Is.True);
    }

    [Test]
    public void FramePumpDoesNotAcknowledgeWhenLightingTickFails()
    {
        var exchange = NewExchange();
        exchange.PublishTerrainChange(Reset(1, 1));
        exchange.PublishTerrainFrame(Frame(1, 1));
        var pump = new TerrainLightingFramePump();

        Assert.Throws<System.InvalidOperationException>(() =>
            pump.ProcessLatest(exchange, _ => throw new System.InvalidOperationException("solve failed")));
        Assert.That(pump.ProcessLatest(exchange, _ => true), Is.True);
        Assert.That(pump.ProcessLatest(exchange, _ => true), Is.False);
    }

    [Test]
    public void FramePumpLeavesDemandPendingWhenLightingIsDisabledOrBypassed()
    {
        var exchange = NewExchange();
        exchange.PublishTerrainChange(Reset(1, 1));
        exchange.PublishTerrainFrame(Frame(1, 1));
        var pump = new TerrainLightingFramePump();

        Assert.That(pump.ProcessLatest(exchange, _ => false), Is.False);
        Assert.That(pump.ProcessLatest(exchange, _ => true), Is.True);
    }

    private static TerrainLightingExchange NewExchange() => new();

    private static int Count(string source, string value)
    {
        int count = 0;
        int offset = 0;
        while ((offset = source.IndexOf(value, offset, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string FindProjectRoot()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Assets", "Scripts")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Unity project root for composition-source test.");
    }

    private static TerrainLightingChange Reset(ulong generation, ulong sequence) =>
        new(generation, sequence, sequence, TerrainLightingChangeKind.FullReset,
            TerrainLightingChannels.All, default, TerrainLightingFullResetReason.WorldReplaced);

    private static TerrainLightingChange Region(ulong generation, ulong sequence, RectInt? region = null) =>
        new(generation, sequence, sequence, TerrainLightingChangeKind.Region,
            TerrainLightingChannels.Material, region ?? new RectInt(0, 0, 2, 2), default);

    private static TerrainLightingFrameSnapshot Frame(ulong generation, ulong sequence) =>
        new(generation, sequence, TerrainLightingFrameState.Ready, sequence,
            new RectInt(0, 0, 20, 10), new RectInt(-4, -4, 28, 18), new Camera(), new TestContributor());

    private sealed class TestContributor : ILightingGeometryContributor
    {
        public ulong LightingGeometryRevision => 1;

        public void RenderMaterialEmissionFields(
            CommandBuffer commandBuffer,
            in LightingMaterialEmissionContext context)
        {
        }

        public void RenderAmbientOcclusionField(
            CommandBuffer commandBuffer,
            in LightingAmbientOcclusionContext context)
        {
        }
    }
}
