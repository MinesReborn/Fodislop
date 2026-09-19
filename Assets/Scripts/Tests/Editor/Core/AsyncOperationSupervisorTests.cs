#nullable enable

using System.Collections;
using Cysharp.Threading.Tasks;
using Kern.Core.Lifecycle;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Kern.Tests.Core;

[TestFixture]
public sealed class AsyncOperationSupervisorTests
{
    [UnityTest]
    public IEnumerator Run_TracksOperationUntilCompletion()
    {
        using var supervisor = new AsyncOperationSupervisor();
        var completion = new UniTaskCompletionSource();

        supervisor.Run("controlled_operation", _ => completion.Task);

        Assert.That(supervisor.ActiveCount, Is.EqualTo(1));
        completion.TrySetResult();
        yield return UniTask.WaitUntil(() => supervisor.ActiveCount == 0).ToCoroutine();
        Assert.That(supervisor.ActiveCount, Is.Zero);
    }

    [UnityTest]
    public IEnumerator StopAsync_CancelsOwnedOperationsAndWaitsForCompletion()
    {
        using var supervisor = new AsyncOperationSupervisor();
        supervisor.Run(
            "cancelled_operation",
            cancellationToken => UniTask.WaitUntilCanceled(cancellationToken));

        Assert.That(supervisor.ActiveCount, Is.EqualTo(1));
        yield return supervisor.StopAsync().ToCoroutine();
        Assert.That(supervisor.ActiveCount, Is.Zero);
    }

    [Test]
    public void Run_AfterDispose_Throws()
    {
        var supervisor = new AsyncOperationSupervisor();
        supervisor.Dispose();

        Assert.Throws<System.ObjectDisposedException>(() =>
            supervisor.Run("late_operation", _ => UniTask.CompletedTask));
    }
}
