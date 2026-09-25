#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using UnityEngine.SceneManagement;

namespace Kern.Core;

public sealed class SceneTransitionTicket : IDisposable
{
    public static readonly TimeSpan DefaultTransitionTimeout = TimeSpan.FromSeconds(30);

    private readonly UniTaskCompletionSource _attached = new();
    private readonly UniTaskCompletionSource _activationRequested = new();
    private readonly UniTaskCompletionSource _startupReady = new();
    private readonly UniTaskCompletionSource _presentationReady = new();
    private readonly UniTaskCompletionSource _failureSignal = new();
    private readonly CancellationTokenSource _timeoutCts;
    private readonly TimeSpan _timeout;
    private bool _isDisposed;
    private Exception? _failure;

    public SceneTransitionTicket(string targetSceneName, TimeSpan? timeout = null)
    {
        TargetSceneName = !string.IsNullOrWhiteSpace(targetSceneName)
            ? targetSceneName
            : throw new ArgumentException("Target scene name is required.", nameof(targetSceneName));

        _timeout = timeout ?? DefaultTransitionTimeout;
        if (_timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Transition timeout must be positive.");
        }

        _timeoutCts = new CancellationTokenSource(_timeout);
        _timeoutCts.Token.Register(OnTimeout, useSynchronizationContext: true);
    }

    public string TargetSceneName { get; }

    public SceneTransitionPhase Phase { get; private set; } = SceneTransitionPhase.Created;

    public bool IsAttached => Phase >= SceneTransitionPhase.Attached && Phase != SceneTransitionPhase.Failed;

    public bool IsStartupReady => Phase >= SceneTransitionPhase.StartupReady && Phase != SceneTransitionPhase.Failed;

    public bool IsPresentationReady => Phase == SceneTransitionPhase.PresentationReady;

    internal event Action<SceneTransitionStatus>? Changed;

    public void Attach(Scene scene)
    {
        if (!scene.IsValid() || !string.Equals(scene.name, TargetSceneName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Scene transition ticket for '{TargetSceneName}' was attached by invalid scene '{scene.name}'.");
        }

        if (Phase != SceneTransitionPhase.Created)
        {
            throw new InvalidOperationException(
                $"Scene '{TargetSceneName}' attached more than one composition root to the same transition.");
        }

        SetPhase(SceneTransitionPhase.Attached);
        _attached.TrySetResult();
    }

    public void RequestActivation()
    {
        if (Phase != SceneTransitionPhase.Attached)
        {
            throw new InvalidOperationException(
                $"Scene '{TargetSceneName}' received an invalid duplicate activation request.");
        }

        SetPhase(SceneTransitionPhase.ActivationRequested);
        _activationRequested.TrySetResult();
    }

    public void MarkStartupReady()
    {
        if (Phase != SceneTransitionPhase.ActivationRequested)
        {
            throw new InvalidOperationException(
                $"Scene '{TargetSceneName}' reported startup readiness in an invalid transition state.");
        }

        SetPhase(SceneTransitionPhase.StartupReady);
        _startupReady.TrySetResult();
    }

    public void MarkPresentationReady()
    {
        if (Phase != SceneTransitionPhase.StartupReady)
        {
            throw new InvalidOperationException(
                $"Scene '{TargetSceneName}' reported presentation readiness in an invalid transition state.");
        }

        SetPhase(SceneTransitionPhase.PresentationReady);
        _presentationReady.TrySetResult();
    }

    public void Fail(Exception exception)
    {
        if (exception == null)
        {
            throw new ArgumentNullException(nameof(exception));
        }
        if (Phase is SceneTransitionPhase.Failed or SceneTransitionPhase.PresentationReady)
        {
            return;
        }

        _failure = exception;
        SetPhase(SceneTransitionPhase.Failed, exception);
        _failureSignal.TrySetResult();
        _attached.TrySetResult();
        _activationRequested.TrySetResult();
        _startupReady.TrySetResult();
        _presentationReady.TrySetResult();
    }

    public UniTask WaitUntilAttachedAsync() => AwaitPhaseAsync(_attached.Task);

    public UniTask WaitForActivationAsync() => AwaitPhaseAsync(_activationRequested.Task);

    public UniTask WaitForStartupAsync() => AwaitPhaseAsync(_startupReady.Task);

    public UniTask WaitForPresentationAsync() => AwaitPhaseAsync(_presentationReady.Task);

    public UniTask WaitForFailureAsync() => _failureSignal.Task;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        // Cancelling the timeout token triggers OnTimeout, which is a no-op once
        // the transition already completed or failed - both states dispose in
        // BootstrapLifetimeScope only after the transition is over.
        _timeoutCts.Dispose();
    }

    private void OnTimeout()
    {
        if (_isDisposed || Phase is SceneTransitionPhase.Failed or SceneTransitionPhase.PresentationReady)
        {
            return;
        }

        Fail(new TimeoutException(
            $"Scene transition to '{TargetSceneName}' timed out after {_timeout.TotalSeconds:F0} seconds."));
    }

    private async UniTask AwaitPhaseAsync(UniTask phase)
    {
        await phase;
        if (_failure != null)
        {
            throw _failure;
        }
    }

    private void SetPhase(SceneTransitionPhase phase, Exception? failure = null)
    {
        Phase = phase;
        Changed?.Invoke(new SceneTransitionStatus(TargetSceneName, phase, failure));
    }
}
