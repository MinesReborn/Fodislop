#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Kern.Core.Lifecycle;

public sealed class AsyncOperationSupervisor : IAsyncOperationSupervisor, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<long, string> _activeOperations = [];
    private readonly object _gate = new();
    private long _nextOperationID;
    private bool _disposed;

    public int ActiveCount
    {
        get
        {
            lock (_gate)
            {
                return _activeOperations.Count;
            }
        }
    }

    // Имена живых операций: по одному счётчику не понять, какая из них не
    // завершилась.
    public string[] ActiveOperationNames
    {
        get
        {
            lock (_gate)
            {
                var names = new string[_activeOperations.Count];
                _activeOperations.Values.CopyTo(names, 0);
                return names;
            }
        }
    }

    public void Run(
        string operationName,
        Func<CancellationToken, UniTask> operation)
    {
        if (string.IsNullOrWhiteSpace(operationName))
        {
            throw new ArgumentException("Operation name is required.", nameof(operationName));
        }

        if (operation == null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AsyncOperationSupervisor));
        }

        long operationID = Interlocked.Increment(ref _nextOperationID);
        lock (_gate)
        {
            _activeOperations.Add(operationID, operationName);
        }

        ExecuteAsync(operationID, operationName, operation).Forget();
    }

    public async UniTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_lifetime.IsCancellationRequested)
        {
            _lifetime.Cancel();
        }

        while (ActiveCount > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private async UniTaskVoid ExecuteAsync(
        long operationID,
        string operationName,
        Func<CancellationToken, UniTask> operation)
    {
        try
        {
            await operation(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.LogException(new InvalidOperationException(
                $"Supervised async operation '{operationName}' failed.",
                exception));
        }
        finally
        {
            lock (_gate)
            {
                _activeOperations.Remove(operationID);
            }
        }
    }
}
