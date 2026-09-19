#nullable enable

using System;
using Kern.World.Streaming;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World.Streaming;

[TestFixture]
public sealed class StreamingGovernorTests
{
    private readonly StreamingGovernor _governor =
        new(StreamingPolicy.Default);

    [Test]
    public void PlanPreservesExplicitTargetOrigin()
    {
        var current = new StreamingWindow(new Vector2Int(0, 0), new Vector2Int(192, 128));

        StreamingPlan plan = _governor.Plan(
            current,
            new Vector2Int(-1, 7),
            current.Size,
            dimensionsChanged: false);

        Assert.That(plan.Delta, Is.EqualTo(new Vector2Int(-1, 7)));
    }

    [Test]
    public void StableWindowProducesKeepPlan()
    {
        var current = new StreamingWindow(new Vector2Int(0, 0), new Vector2Int(192, 128));

        StreamingPlan plan = _governor.Plan(
            current,
            new Vector2Int(0, 0),
            current.Size,
            dimensionsChanged: false);

        Assert.That(plan.Kind, Is.EqualTo(StreamingPlanKind.Keep));
        Assert.That(plan.Delta, Is.EqualTo(Vector2Int.zero));
    }

    [Test]
    public void InWindowMoveProducesScrollPlan()
    {
        var current = new StreamingWindow(new Vector2Int(0, 0), new Vector2Int(192, 128));

        StreamingPlan plan = _governor.Plan(
            current,
            new Vector2Int(7, 0),
            current.Size,
            dimensionsChanged: false);

        Assert.That(plan.Kind, Is.EqualTo(StreamingPlanKind.ScrollTerrain));
        Assert.That(plan.Delta, Is.EqualTo(new Vector2Int(7, 0)));
    }

    [Test]
    public void SizeChangeProducesResizePlan()
    {
        var current = new StreamingWindow(new Vector2Int(0, 0), new Vector2Int(192, 128));

        StreamingPlan plan = _governor.Plan(
            current,
            new Vector2Int(7, 0),
            new Vector2Int(224, 128),
            dimensionsChanged: true);

        Assert.That(plan.Kind, Is.EqualTo(StreamingPlanKind.Resize));
    }

    [Test]
    public void TeleportBeyondWindowProducesFullRebuildPlan()
    {
        var current = new StreamingWindow(new Vector2Int(0, 0), new Vector2Int(192, 128));

        StreamingPlan plan = _governor.Plan(
            current,
            new Vector2Int(256, 0),
            current.Size,
            dimensionsChanged: false);

        Assert.That(plan.Kind, Is.EqualTo(StreamingPlanKind.FullRebuild));
    }

    [Test]
    public void TargetOriginStaysWhileViewportIsInsideAllocatedWindow()
    {
        Vector2Int current = Vector2Int.zero;
        Vector2Int window = new(192, 128);

        Vector2Int target = _governor.SelectTargetOrigin(
            current,
            new Vector2Int(7, 0),
            new Vector2Int(8, 16),
            new Vector2Int(64, 48),
            window,
            dimensionsChanged: false);

        Assert.That(target, Is.EqualTo(current));
    }

    [Test]
    public void PrefetchMarginReanchorsBeforeViewportReachesWindowEdge()
    {
        Vector2Int current = Vector2Int.zero;
        Vector2Int target = _governor.SelectTargetOrigin(
            current,
            new Vector2Int(40, 0),
            new Vector2Int(49, 16),
            new Vector2Int(1, 1),
            new Vector2Int(64, 64),
            dimensionsChanged: false,
            reanchorMarginCells: 16);

        Assert.That(target, Is.EqualTo(new Vector2Int(32, 0)));
    }

    [Test]
    public void DefaultTerrainDecisionHasNoPrefetchMargin()
    {
        Vector2Int target = _governor.SelectTargetOrigin(
            Vector2Int.zero,
            new Vector2Int(80, 0),
            new Vector2Int(47, 16),
            new Vector2Int(1, 1),
            new Vector2Int(64, 64),
            dimensionsChanged: false);

        Assert.That(target, Is.EqualTo(Vector2Int.zero));
    }

    [Test]
    public void ForwardPrefetchMovesOnlyByPolicyQuantum()
    {
        Vector2Int current = new(64, 0);
        int margin = _governor.Policy.ResolvePrefetchMarginCells(64);
        for (int playerX = 100; playerX < 220; playerX++)
        {
            Vector2Int next = _governor.SelectTargetOrigin(
                current,
                new Vector2Int(playerX - 32, 0),
                new Vector2Int(playerX, 0),
                new Vector2Int(1, 1),
                new Vector2Int(64, 64),
                dimensionsChanged: false,
                reanchorMarginCells: margin);
            Assert.That(Math.Abs(next.x - current.x), Is.EqualTo(0).Or.EqualTo(32));
            current = next;
        }
    }

    [Test]
    public void TerrainWindowAdvancesByOneQuantumAtPrefetchBoundary()
    {
        Vector2Int target = _governor.SelectTargetOrigin(
            Vector2Int.zero,
            new Vector2Int(32, 0),
            new Vector2Int(160, 32),
            new Vector2Int(64, 48),
            new Vector2Int(192, 128),
            dimensionsChanged: false,
            reanchorMarginCells: _governor.Policy.ResolvePrefetchMarginCells(128));

        Assert.That(target, Is.EqualTo(new Vector2Int(32, 0)));
    }

    [Test]
    public void LargeJumpReanchorsImmediatelyInsteadOfWalkingTheWindow()
    {
        Vector2Int target = _governor.SelectTargetOrigin(
            Vector2Int.zero,
            new Vector2Int(1000, 0),
            new Vector2Int(1000, 0),
            new Vector2Int(1, 1),
            new Vector2Int(160, 160),
            dimensionsChanged: false,
            reanchorMarginCells: 31);

        Assert.That(target, Is.EqualTo(new Vector2Int(992, 0)));
    }

    [Test]
    public void TargetOriginReanchorsOnPolicyQuantum()
    {
        Vector2Int current = Vector2Int.zero;
        Vector2Int desired = new(80, 0);

        Vector2Int target = _governor.SelectTargetOrigin(
            current,
            desired,
            new Vector2Int(-5, 16),
            new Vector2Int(64, 48),
            new Vector2Int(192, 128),
            dimensionsChanged: false);

        Assert.That(target, Is.EqualTo(new Vector2Int(-32, 0)));
    }

    [Test]
    public void ReanchorDecisionUsesWindowBoundaryInsteadOfCellStep()
    {
        Vector2Int current = Vector2Int.zero;
        Vector2Int window = new(32, 32);
        Vector2Int viewportSize = new(16, 16);

        Vector2Int atBoundary = _governor.SelectTargetOrigin(
            current,
            new Vector2Int(16, 0),
            new Vector2Int(16, 8),
            viewportSize,
            window,
            dimensionsChanged: false);
        Vector2Int beyondBoundary = _governor.SelectTargetOrigin(
            current,
            new Vector2Int(17, 0),
            new Vector2Int(17, 8),
            viewportSize,
            window,
            dimensionsChanged: false);

        Assert.That(atBoundary, Is.EqualTo(current));
        Assert.That(beyondBoundary, Is.EqualTo(new Vector2Int(0, 0)));
    }

    [TestCase(-1, -32)]
    [TestCase(-32, -32)]
    [TestCase(-33, -64)]
    [TestCase(0, 0)]
    [TestCase(31, 0)]
    [TestCase(32, 32)]
    public void OriginAlignmentUsesOnePolicyQuantum(int coordinate, int expected)
    {
        Assert.That(_governor.Policy.AlignOrigin(coordinate), Is.EqualTo(expected));
    }

    [Test]
    public void QuantizationClampsExtremeRequestsWithoutOverflow()
    {
        int quantized = _governor.Policy.QuantizeDimensionWithHeadroom(int.MaxValue);

        Assert.That(quantized, Is.EqualTo(StreamingPolicy.DefaultMaximumWindowDimension));
    }

    [Test]
    public void MapPrefetchWindowCoversViewportBeforeChunkBoundary()
    {
        int dimension = _governor.Policy.QuantizeDimensionWithHeadroom(
            StreamingPolicy.DefaultMapWindowDimensionCells);

        Assert.That(dimension, Is.EqualTo(160));
        Assert.That(
            dimension,
            Is.GreaterThan(StreamingPolicy.DefaultMapWindowDimensionCells));
    }

    [Test]
    public void ShrinkHysteresisDoesNotOverflowAtMaximumRequest()
    {
        int dimension = _governor.Policy.SelectWindowDimension(
            int.MaxValue,
            384,
            isInitialized: true);

        Assert.That(dimension, Is.EqualTo(StreamingPolicy.DefaultMaximumWindowDimension));
    }

}
