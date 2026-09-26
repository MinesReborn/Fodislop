#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MinesServer.Networking.Connection.Client;
using NUnit.Framework;

namespace Kern.Tests.Networking;

public sealed class VirtualDummyClockTests
{
    [Test]
    public void Advance_CompletesWaitersByDueTimeThenByScheduleOrder()
    {
        var clock = new VirtualDummyClock(seed: 1);
        var order = new List<string>();
        Wait(clock.Delay(200), () => order.Add("b@200"));
        Wait(clock.Delay(100), () => order.Add("a@100"));
        Wait(clock.Delay(200), () => order.Add("c@200"));

        clock.Advance(199);
        Assert.That(order, Is.EqualTo(new[] { "a@100" }));

        clock.Advance(1);
        Assert.That(order, Is.EqualTo(new[] { "a@100", "b@200", "c@200" }));
        Assert.That(clock.NowMilliseconds, Is.EqualTo(200));
        Assert.That(clock.PendingCount, Is.Zero);
    }

    [Test]
    public void Advance_RunsContinuationsAtTheirOwnDueTime()
    {
        var clock = new VirtualDummyClock(seed: 1);
        var ticks = new List<long>();
        Loop().Forget();

        clock.Advance(1000);

        Assert.That(ticks, Is.EqualTo(new long[] { 300, 600, 900 }));
        Assert.That(clock.NowMilliseconds, Is.EqualTo(1000));

        async UniTaskVoid Loop()
        {
            for (int i = 0; i < 5; i++)
            {
                await clock.Delay(300);
                ticks.Add(clock.NowMilliseconds);
            }
        }
    }

    [Test]
    public void Yield_CompletesOnNextAdvanceWithoutMovingTime()
    {
        var clock = new VirtualDummyClock(seed: 1);
        bool resumed = false;
        Wait(clock.Yield(), () => resumed = true);

        Assert.That(resumed, Is.False);
        clock.Advance(0);

        Assert.That(resumed, Is.True);
        Assert.That(clock.NowMilliseconds, Is.Zero);
    }

    [Test]
    public void CancelledDelay_IsRemovedAndReportsCancellation()
    {
        var clock = new VirtualDummyClock(seed: 1);
        using var cancellation = new CancellationTokenSource();
        bool cancelled = false;
        Observe().Forget();

        cancellation.Cancel();
        clock.Advance(1000);

        Assert.That(cancelled, Is.True);
        Assert.That(clock.PendingCount, Is.Zero);

        async UniTaskVoid Observe()
        {
            try
            {
                await clock.Delay(500, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
        }
    }

    [Test]
    public void SameSeed_ProducesSameRandomSequence()
    {
        var first = new VirtualDummyClock(seed: 42);
        var second = new VirtualDummyClock(seed: 42);

        for (int i = 0; i < 16; i++)
        {
            Assert.That(second.Random.Next(), Is.EqualTo(first.Random.Next()));
        }
    }

    [Test]
    public void NegativeDurations_AreRejected()
    {
        var clock = new VirtualDummyClock(seed: 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Delay(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(-1));
    }

    private static void Wait(UniTask task, Action onCompleted)
    {
        Observe().Forget();

        async UniTaskVoid Observe()
        {
            await task;
            onCompleted();
        }
    }
}
