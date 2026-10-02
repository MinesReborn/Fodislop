#nullable enable

using System;
using Kern.World.Lighting;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Editor;

public sealed class LightingAmbientOcclusionUpdatePolicyTests
{
    [Test]
    public void CommittedDig_IsPartialEvenThoughActivationMarksFieldDirty()
    {
        var state = new LightingRuntimeState { FieldDirty = false, StagedTerrainGeometryRevision = 7 };
        bool fieldWasDirty = state.FieldDirty;
        state.QueueRegionInvalidation(new RectInt(10, 20, 1, 1));
        state.ActivatePendingRegionIfVisible(new RectInt(0, 0, 192, 128));
        Assert.That(state.FieldDirty, Is.True);
        Assert.That(LightingAmbientOcclusionUpdatePolicy.CanUpdatePartially(
            state, 7, fieldWasDirty, false, false, false), Is.True);
    }

    [TestCase(true, false, false, false, 7UL)]
    [TestCase(false, true, false, false, 7UL)]
    [TestCase(false, false, true, false, 7UL)]
    [TestCase(false, false, false, true, 7UL)]
    [TestCase(false, false, false, false, 8UL)]
    public void InvalidRetainedField_RequiresFullRaster(
        bool fieldWasDirty, bool resized, bool moved, bool contributorChanged, ulong revision)
    {
        var state = new LightingRuntimeState { StagedTerrainGeometryRevision = 7 };
        state.QueueRegionInvalidation(new RectInt(10, 20, 1, 1));
        state.ActivatePendingRegionIfVisible(new RectInt(0, 0, 192, 128));
        Assert.That(LightingAmbientOcclusionUpdatePolicy.CanUpdatePartially(
            state, revision, fieldWasDirty, resized, moved, contributorChanged), Is.False);
    }

    [Test]
    public void StableFrame_DoesNotSchedulePartialRaster()
    {
        var state = new LightingRuntimeState { FieldDirty = false, StagedTerrainGeometryRevision = 7 };
        Assert.That(LightingAmbientOcclusionUpdatePolicy.CanUpdatePartially(
            state, 7, false, false, false, false), Is.False);
    }

    [Test]
    public void SingleDig_PreservesFieldOutsideSevenCellSupport()
    {
        RectInt rect = LightingAmbientOcclusionUpdatePolicy.ResolveRasterRect(
            [new RectInt(10, 20, 1, 1)], new RectInt(0, 0, 192, 128), 32);
        AssertRect(rect, 224, 544, 224, 224);
        Assert.That((long)rect.width * rect.height, Is.EqualTo(50_176));
    }

    [Test]
    public void NegativeOriginAndLowerEdge_ClipInWorldSpaceBeforePixelMapping()
    {
        RectInt rect = LightingAmbientOcclusionUpdatePolicy.ResolveRasterRect(
            [new RectInt(-10, -20, 1, 1)], new RectInt(-10, -20, 8, 8), 32);
        AssertRect(rect, 0, 0, 128, 128);
    }

    [Test]
    public void UpperEdge_UsesBottomLeftRasterRowsAndHalfOpenBounds()
    {
        RectInt rect = LightingAmbientOcclusionUpdatePolicy.ResolveRasterRect(
            [new RectInt(7, 7, 1, 1)], new RectInt(0, 0, 8, 8), 32);
        AssertRect(rect, 128, 128, 128, 128);
    }

    [Test]
    public void SeparatedEdits_IncludeBothSupports()
    {
        RectInt rect = LightingAmbientOcclusionUpdatePolicy.ResolveRasterRect(
            [new RectInt(5, 5, 1, 1), new RectInt(10, 9, 1, 1)], new RectInt(0, 0, 32, 32), 32);
        AssertRect(rect, 64, 64, 384, 352);
    }

    [Test]
    public void EditJustOutsideField_StillUpdatesItsContactSupport()
    {
        RectInt rect = LightingAmbientOcclusionUpdatePolicy.ResolveRasterRect(
            [new RectInt(-1, 4, 1, 1)], new RectInt(0, 0, 8, 8), 32);
        AssertRect(rect, 0, 32, 96, 224);
    }

    [Test]
    public void DistantEdit_DoesNotTouchField()
    {
        AssertRect(LightingAmbientOcclusionUpdatePolicy.ResolveRasterRect(
            [new RectInt(-10, -10, 1, 1)], new RectInt(0, 0, 8, 8), 32), 0, 0, 0, 0);
    }

    [Test]
    public void InvalidEdit_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => LightingAmbientOcclusionUpdatePolicy.ResolveRasterRect(
            [new RectInt(0, 0, 0, 1)], new RectInt(0, 0, 8, 8), 32));
    }

    private static void AssertRect(RectInt rect, int x, int y, int width, int height)
    {
        Assert.That(rect.x, Is.EqualTo(x));
        Assert.That(rect.y, Is.EqualTo(y));
        Assert.That(rect.width, Is.EqualTo(width));
        Assert.That(rect.height, Is.EqualTo(height));
    }
}
