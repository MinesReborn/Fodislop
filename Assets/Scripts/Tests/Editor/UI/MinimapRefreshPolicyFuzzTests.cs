#nullable enable

using Kern.UI;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.UI;

[TestFixture]
public class MinimapRefreshPolicyFuzzTests
{
    [Test]
    public void CanRefresh_BeforeAnyRecord_True()
    {
        var random = new System.Random(42);
        var policy = new MinimapRefreshPolicy();
        for (int i = 0; i < 50; i++)
            Assert.IsTrue(policy.CanRefresh((float)random.NextDouble() * 1000f), $"i={i}");
    }

    [Test]
    public void CanRefresh_WithinThrottle_False()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 50; i++)
        {
            var policy = new MinimapRefreshPolicy();
            policy.RecordRefresh(10f, 1, true);
            float t = 10f + (float)random.NextDouble() * (MinimapRefreshPolicy.UpdateDelaySeconds - 0.001f);
            Assert.IsFalse(policy.CanRefresh(t), $"i={i}");
        }
    }

    [Test]
    public void CanRefresh_AfterThrottle_True()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 50; i++)
        {
            var policy = new MinimapRefreshPolicy();
            policy.RecordRefresh(10f, 1, true);
            float t = 10f + MinimapRefreshPolicy.UpdateDelaySeconds + (float)random.NextDouble() * 10f;
            Assert.IsTrue(policy.CanRefresh(t), $"i={i}");
        }
    }

    [Test]
    public void Reset_ClearsAll()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 50; i++)
        {
            var policy = new MinimapRefreshPolicy();
            policy.RecordRefresh(5f, 10, true);
            policy.NotifyChunkLoaded();
            policy.Reset();
            Assert.IsFalse(policy.InitialRefreshDone);
            Assert.IsTrue(policy.CanRefresh(0f));
        }
    }

    [Test]
    public void Invalidate_TriggersRefresh()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 50; i++)
        {
            var policy = new MinimapRefreshPolicy();
            policy.RecordInitialRefresh(1f, new Vector2Int(0, 0), 5, true, true);
            policy.InvalidateStorageRevision();
            Assert.IsTrue(policy.ShouldRefreshOnStorageOrMove(2f, 6, true, true, true), $"i={i}");
        }
    }

    [Test]
    public void RandomEventStream_NoSpuriousRefreshes()
    {
        var random = new System.Random(42);
        var policy = new MinimapRefreshPolicy();
        float time = 0f;
        long storageRev = 1;
        bool ready = true, visible = true, hasPos = true;

        for (int i = 0; i < 500; i++)
        {
            time += (float)random.NextDouble() * 0.2f;
            int action = random.Next(5);
            switch (action)
            {
                case 0: policy.RecordRefresh(time, storageRev, true); break;
                case 1: policy.NotifyPlayerMoved(new Vector2Int(i, i), time, out _); break;
                case 2: policy.NotifyChunkLoaded(); break;
                case 3: storageRev++; break;
                case 4:
                    ready = random.Next(2) == 0;
                    visible = random.Next(2) == 0;
                    hasPos = random.Next(2) == 0;
                    break;
            }

            bool sr = policy.ShouldRefreshOnStorageOrMove(time, storageRev, ready, visible, hasPos);
            if (sr)
                Assert.IsTrue(policy.CanRefresh(time), $"i={i}");
        }
    }
}
