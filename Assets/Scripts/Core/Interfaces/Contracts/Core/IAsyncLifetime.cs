#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Kern;

public interface IAsyncLifetime
{
    UniTask StopAsync(CancellationToken cancellationToken = default);
}

public interface IAsyncOperationSupervisor : IAsyncLifetime
{
    int ActiveCount { get; }

    void Run(string operationName, Func<CancellationToken, UniTask> operation);
}
