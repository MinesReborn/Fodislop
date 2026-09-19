#nullable enable

namespace Kern.Tests.UI;

using Kern.UI;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class MinimapRefreshPolicyTests
{
    [Test]
    public void CanRefresh_InitialState_ReturnsTrue()
    {
        var policy = new MinimapRefreshPolicy();
        Assert.IsTrue(policy.CanRefresh(0f));
    }

    [Test]
    public void CanRefresh_ThrottlesWithinUpdateDelay()
    {
        var policy = new MinimapRefreshPolicy();
        policy.RecordRefresh(currentTime: 10f, storageRevision: 1, hadLoadedCells: true);

        Assert.IsFalse(policy.CanRefresh(10.05f));
        Assert.IsTrue(policy.CanRefresh(10.10f));
        Assert.IsTrue(policy.CanRefresh(10.20f));
    }

    [Test]
    public void NotifyPlayerMoved_WhenThrottled_SchedulesPendingRefresh()
    {
        var policy = new MinimapRefreshPolicy();
        policy.RecordRefresh(currentTime: 5f, storageRevision: 1, hadLoadedCells: true);

        policy.NotifyPlayerMoved(new Vector2Int(10, 20), currentTime: 5.04f, out bool shouldRefreshNow);

        Assert.IsFalse(shouldRefreshNow);

        // When throttled, ShouldRefreshOnStorageOrMove does not trigger yet
        bool throttledUpdate = policy.ShouldRefreshOnStorageOrMove(
            currentTime: 5.06f,
            currentStorageRevision: 1,
            isReady: true,
            isVisible: true,
            hasServerPosition: true);
        Assert.IsFalse(throttledUpdate);

        // When time advances, ShouldRefreshOnStorageOrMove triggers and consumes pending flag
        bool shouldRefreshUpdate = policy.ShouldRefreshOnStorageOrMove(
            currentTime: 5.10f,
            currentStorageRevision: 1,
            isReady: true,
            isVisible: true,
            hasServerPosition: true);

        Assert.IsTrue(shouldRefreshUpdate);

        // After consumed, subsequent calls do not re-trigger
        bool secondUpdate = policy.ShouldRefreshOnStorageOrMove(
            currentTime: 5.11f,
            currentStorageRevision: 1,
            isReady: true,
            isVisible: true,
            hasServerPosition: true);
        Assert.IsFalse(secondUpdate);
    }

    [Test]
    public void NotifyPlayerMoved_WhenNotThrottled_RefreshesImmediately()
    {
        var policy = new MinimapRefreshPolicy();
        policy.RecordRefresh(currentTime: 5f, storageRevision: 1, hadLoadedCells: true);

        policy.NotifyPlayerMoved(new Vector2Int(15, 25), currentTime: 5.15f, out bool shouldRefreshNow);

        Assert.IsTrue(shouldRefreshNow);

        // Should not have a pending move refresh
        bool shouldRefreshUpdate = policy.ShouldRefreshOnStorageOrMove(
            currentTime: 5.20f,
            currentStorageRevision: 1,
            isReady: true,
            isVisible: true,
            hasServerPosition: true);
        Assert.IsFalse(shouldRefreshUpdate);
    }

    [Test]
    public void ShouldRefreshOnStorageOrMove_StorageRevisionChanged_TriggersRefresh()
    {
        var policy = new MinimapRefreshPolicy();
        policy.RecordInitialRefresh(
            currentTime: 1f,
            playerPos: new Vector2Int(0, 0),
            storageRevision: 10,
            isVisible: true,
            hadLoadedCells: true);

        bool sameRevision = policy.ShouldRefreshOnStorageOrMove(
            currentTime: 2f,
            currentStorageRevision: 10,
            isReady: true,
            isVisible: true,
            hasServerPosition: true);

        Assert.IsFalse(sameRevision);

        bool newRevision = policy.ShouldRefreshOnStorageOrMove(
            currentTime: 2f,
            currentStorageRevision: 11,
            isReady: true,
            isVisible: true,
            hasServerPosition: true);

        Assert.IsTrue(newRevision);
    }

    [Test]
    public void ShouldRefreshOnChunkLoad_TriggersWhenRequestedAndReady()
    {
        var policy = new MinimapRefreshPolicy();
        policy.RecordRefresh(currentTime: 1f, storageRevision: 1, hadLoadedCells: true);

        policy.NotifyChunkLoaded();

        bool shouldRefreshThrottled = policy.ShouldRefreshOnChunkLoad(
            currentTime: 1.05f,
            isReady: true,
            isVisible: true,
            hasServerPosition: true);
        Assert.IsFalse(shouldRefreshThrottled);

        bool shouldRefreshElapsed = policy.ShouldRefreshOnChunkLoad(
            currentTime: 1.15f,
            isReady: true,
            isVisible: true,
            hasServerPosition: true);
        Assert.IsTrue(shouldRefreshElapsed);

        // After consumption, should not trigger again
        bool shouldRefreshAfterConsumed = policy.ShouldRefreshOnChunkLoad(
            currentTime: 1.20f,
            isReady: true,
            isVisible: true,
            hasServerPosition: true);
        Assert.IsFalse(shouldRefreshAfterConsumed);
    }

    [Test]
    public void RecordInitialRefresh_WhenNoLoadedCells_DoesNotMarkInitialDone()
    {
        var policy = new MinimapRefreshPolicy();
        policy.RecordInitialRefresh(
            currentTime: 1f,
            playerPos: new Vector2Int(5, 5),
            storageRevision: 1,
            isVisible: true,
            hadLoadedCells: false);

        Assert.IsFalse(policy.InitialRefreshDone);

        // Once cells arrive:
        policy.RecordInitialRefresh(
            currentTime: 1.2f,
            playerPos: new Vector2Int(5, 5),
            storageRevision: 1,
            isVisible: true,
            hadLoadedCells: true);

        Assert.IsTrue(policy.InitialRefreshDone);

        // Same revision won't trigger refresh:
        Assert.IsFalse(policy.ShouldRefreshOnStorageOrMove(
            currentTime: 2f,
            currentStorageRevision: 1,
            isReady: true,
            isVisible: true,
            hasServerPosition: true));

        // Different revision will:
        Assert.IsTrue(policy.ShouldRefreshOnStorageOrMove(
            currentTime: 2f,
            currentStorageRevision: 2,
            isReady: true,
            isVisible: true,
            hasServerPosition: true));
    }

    [Test]
    public void Reset_ClearsAllState()
    {
        var policy = new MinimapRefreshPolicy();
        policy.RecordRefresh(currentTime: 5f, storageRevision: 10, hadLoadedCells: true);
        policy.NotifyChunkLoaded();

        policy.Reset();

        Assert.IsFalse(policy.InitialRefreshDone);
        Assert.IsTrue(policy.CanRefresh(0f));
        Assert.IsFalse(policy.ShouldRefreshOnChunkLoad(
            currentTime: 10f,
            isReady: true,
            isVisible: true,
            hasServerPosition: true));
        Assert.IsFalse(policy.ShouldRefreshOnStorageOrMove(
            currentTime: 10f,
            currentStorageRevision: 10,
            isReady: true,
            isVisible: true,
            hasServerPosition: true));
    }
}
