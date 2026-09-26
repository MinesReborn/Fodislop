#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.Networking;
using Kern.World.Streaming;
using Kern.World.Textures;
using MinesServer.Data;
using MinesServer.Networking.Connection.Client;
using MinesServer.Networking.Server.Packets.GUI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Random = System.Random;

namespace Kern.Tests.Networking;

[TestFixture]
[Category("FuzzPure")]
public sealed class ProtocolChaosFuzzTests
{
    [Test]
    public void RequestReplacement_StaleCompletionNeverClearsCurrentRequest()
    {
        var random = new Random(0x5EED);
        CancellationTokenSource? active = null;
        var requests = new List<CancellationTokenSource>();

        try
        {
            for (int step = 0; step < 2_000; step++)
            {
                int action = random.Next(4);
                switch (action)
                {
                    case 0:
                    {
                        var next = new CancellationTokenSource();
                        CancellationTokenSource? previous = active;
                        DummyRequestCoordinator.ReplaceRequest(ref active, next);
                        requests.Add(next);
                        if (previous != null)
                        {
                            Assert.That(previous.IsCancellationRequested, Is.True, $"step={step}");
                        }

                        break;
                    }
                    case 1:
                    {
                        if (active != null)
                        {
                            CancellationTokenSource stale = active;
                            var next = new CancellationTokenSource();
                            DummyRequestCoordinator.ReplaceRequest(ref active, next);
                            requests.Add(next);
                            Assert.That(
                                DummyRequestCoordinator.TryCompleteRequest(ref active, stale),
                                Is.False,
                                $"step={step}");
                            Assert.That(active, Is.SameAs(next), $"step={step}");
                        }

                        break;
                    }
                    case 2:
                        DummyRequestCoordinator.CancelRequest(ref active);
                        Assert.That(active, Is.Null, $"step={step}");
                        break;
                    case 3:
                        if (active != null)
                        {
                            CancellationTokenSource current = active;
                            Assert.That(
                                DummyRequestCoordinator.TryCompleteRequest(ref active, current),
                                Is.True,
                                $"step={step}");
                            Assert.That(active, Is.Null, $"step={step}");
                        }

                        break;
                }
            }
        }
        finally
        {
            DummyRequestCoordinator.CancelRequest(ref active);
            foreach (CancellationTokenSource request in requests)
            {
                request.Dispose();
            }
        }
    }

    [Test]
    public void StreamingGovernor_RandomPlansHaveConsistentKindAndDelta()
    {
        var random = new Random(0xC0FFEE);
        var governor = new StreamingGovernor(StreamingPolicy.Default);

        for (int step = 0; step < 10_000; step++)
        {
            var current = new StreamingWindow(
                new Vector2Int(NextCoordinate(random), NextCoordinate(random)),
                new Vector2Int(32 + random.Next(6) * 32, 32 + random.Next(6) * 32));
            var target = new StreamingWindow(
                new Vector2Int(NextCoordinate(random), NextCoordinate(random)),
                new Vector2Int(32 + random.Next(6) * 32, 32 + random.Next(6) * 32));
            bool dimensionsChanged = random.Next(5) == 0;

            StreamingPlan plan = governor.Plan(
                current,
                target.Origin,
                target.Size,
                dimensionsChanged);

            Assert.That(plan.Delta, Is.EqualTo(target.Origin - current.Origin), $"step={step}");
            Assert.That(plan.Target.Origin, Is.EqualTo(target.Origin), $"step={step}");
            Assert.That(plan.Target.Size, Is.EqualTo(target.Size), $"step={step}");

            if (dimensionsChanged || current.Size != target.Size)
            {
                Assert.That(plan.Kind, Is.EqualTo(StreamingPlanKind.Resize), $"step={step}");
            }
            else if (plan.Delta == Vector2Int.zero)
            {
                Assert.That(plan.Kind, Is.EqualTo(StreamingPlanKind.Keep), $"step={step}");
            }
            else
            {
                bool canScroll =
                    Math.Abs(plan.Delta.x) < current.Size.x &&
                    Math.Abs(plan.Delta.y) < current.Size.y;
                Assert.That(
                    plan.Kind,
                    Is.EqualTo(canScroll ? StreamingPlanKind.ScrollTerrain : StreamingPlanKind.FullRebuild),
                    $"step={step}");
            }
        }
    }

    [Test]
    public void StreamingPolicy_ExtremeDimensionsStayBoundedAndAligned()
    {
        var policy = StreamingPolicy.Default;
        int[] values =
        [
            int.MinValue,
            -1_000_000,
            -1,
            0,
            1,
            31,
            32,
            33,
            1_000_000,
            int.MaxValue,
        ];

        foreach (int value in values)
        {
            int dimension = policy.QuantizeDimensionWithHeadroom(value);
            Assert.That(dimension, Is.InRange(
                StreamingPolicy.DefaultMinimumWindowDimension,
                StreamingPolicy.DefaultMaximumWindowDimension));
            Assert.That(dimension % StreamingPolicy.DefaultAllocationQuantumCells, Is.Zero);
        }

        foreach (int value in values)
        {
            int aligned = policy.AlignOrigin(value);
            Assert.That(aligned % StreamingPolicy.DefaultAllocationQuantumCells, Is.Zero);
        }
    }

    [Test]
    public void WindowVisibility_RandomSequenceEmitsOnlyStateTransitions()
    {
        var random = new Random(0xBADC0DE);
        var stream = new WindowCommandStream();
        var events = new List<bool>();
        stream.OpenWindowVisibilityChanged += events.Add;
        bool expected = false;

        for (int step = 0; step < 10_000; step++)
        {
            bool next = random.Next(2) == 0;
            stream.SetServerWindowVisibility(next);
            if (next != expected)
            {
                Assert.That(events[^1], Is.EqualTo(next), $"step={step}");
            }

            expected = next;
            Assert.That(stream.HasOpenWindows, Is.EqualTo(expected), $"step={step}");
        }

        int expectedTransitions = 0;
        expected = false;
        random = new Random(0xBADC0DE);
        for (int step = 0; step < 10_000; step++)
        {
            bool next = random.Next(2) == 0;
            if (next != expected)
            {
                expectedTransitions++;
            }

            expected = next;
        }

        Assert.That(events, Has.Count.EqualTo(expectedTransitions));
    }

    [UnityTest]
    public IEnumerator AsyncSupervisor_RandomCompletionDrains()
    {
        using var supervisor = new AsyncOperationSupervisor();
        var random = new Random(0xA551);
        var operations = new List<UniTaskCompletionSource>();

        for (int index = 0; index < 128; index++)
        {
            var completion = new UniTaskCompletionSource();
            operations.Add(completion);
            supervisor.Run(
                $"chaos_{index}",
                _ => completion.Task);
        }

        Assert.That(supervisor.ActiveCount, Is.EqualTo(128));
        for (int index = 0; index < operations.Count; index++)
        {
            if (random.Next(4) != 0)
            {
                operations[index].TrySetResult();
            }
        }

        // The supervisor must also drain operations that finish in a different
        // order from their registration order. Cancellation is covered by the
        // existing StopAsync_CancelsOwnedOperationsAndWaitsForCompletion test;
        // these completion sources intentionally do not observe cancellation.
        foreach (UniTaskCompletionSource operation in operations)
        {
            operation.TrySetResult();
        }

        yield return null;
        yield return supervisor.StopAsync().ToCoroutine();
        Assert.That(supervisor.ActiveCount, Is.Zero);
    }

    [UnityTest]
    public IEnumerator CellTextureRetryTracker_DuplicateRequestsAreSuppressedAndReleased()
    {
        var tracker = new CellTextureRetryTracker();
        var gate = new UniTaskCompletionSource();
        Assert.That(tracker.ShouldThrottle(CellType.Rock), Is.False);
        UniTask first = tracker.RunTrackedRequestAsync(
            CellType.Rock,
            (_, _) => gate.Task,
            CancellationToken.None);

        yield return null;
        Assert.That(tracker.PendingRequestsCount, Is.EqualTo(1));
        Assert.That(tracker.ShouldThrottle(CellType.Rock), Is.True);

        gate.TrySetResult();
        yield return first.ToCoroutine();
        Assert.That(tracker.PendingRequestsCount, Is.Zero);
        Assert.That(tracker.ShouldThrottle(CellType.Rock), Is.False);
    }

    [Test]
    public void SceneTransitionTicket_RandomOperationsNeverRegressPhase()
    {
        SceneSetup[] originalSetup = EditorSceneManager.GetSceneManagerSetup();
        Scene scene = EditorSceneManager.OpenScene(
            "Assets/Scenes/Bootstrap.unity",
            OpenSceneMode.Single);
        try
        {
            using var ticket = new SceneTransitionTicket(scene.name);
            SceneTransitionPhase last = ticket.Phase;
            var random = new Random(0x71C);

            for (int step = 0; step < 2_000; step++)
            {
                try
                {
                    switch (random.Next(5))
                    {
                        case 0:
                            ticket.Attach(scene);
                            break;
                        case 1:
                            ticket.RequestActivation();
                            break;
                        case 2:
                            ticket.MarkStartupReady();
                            break;
                        case 3:
                            ticket.MarkPresentationReady();
                            break;
                        default:
                            ticket.Fail(new InvalidOperationException("chaos failure"));
                            break;
                    }
                }
                catch (InvalidOperationException)
                {
                    // Invalid transitions must be rejected without mutating the ticket.
                }

                Assert.That(ticket.Phase, Is.GreaterThanOrEqualTo(last), $"step={step}");
                last = ticket.Phase;
                if (ticket.Phase is SceneTransitionPhase.Failed or SceneTransitionPhase.PresentationReady)
                {
                    break;
                }
            }
        }
        finally
        {
            if (originalSetup.Length > 0)
            {
                EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
            }
        }
    }

    private static int NextCoordinate(Random random)
    {
        return random.Next(-1_000_000, 1_000_001);
    }
}
